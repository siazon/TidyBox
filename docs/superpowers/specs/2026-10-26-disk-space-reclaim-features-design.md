# 磁盘空间回收功能群——设计文档（孤儿目录 / Workshop 残留 / 批量卸载预览 / 真正释放空间）

日期：2026-10-26
背景：现有 `ResidueRemover`（普通残留走回收站，管理员残留走 `ElevatedHelper` 隔离区）在用户感知上"清理了"，但**磁盘实际占用在清空回收站/隔离区之前不会变化**——这是本次新功能群的前提修复项，必须先解决，否则新功能会继承同样的误导。

来源依据：见 `docs/superpowers/specs/2026-10-25-...`（已有卸载模块 spike）+ 本次 Reddit 数据分析（workshop 残留 20 帖 / 孤儿目录 36 帖 / 批量卸载 14 帖，均在对话记录中）。

---

## 0. 前提修复：让"清理"真的释放空间

### 0.1 回收站：加"立即清空"入口，而不是让用户自己找
- 新增 `IRecycleBin.EmptyAsync()`（P/Invoke `SHEmptyRecycleBinW`，flag 用 `SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI`，保留 `SHERB_NOSOUND`）。
- `ResidueRemover.RemoveAsync` 完成后，`UninstallResult`/`RemovalResult` 汇总里追加 `BytesMovedToRecycleBin`；UI 侧在清理完成提示里加一个"清空回收站以释放这些空间"的按钮，点击才调用 `EmptyAsync`（**不自动清空**——清空回收站影响的是整台机器所有回收项，不止本次操作的，必须让用户自己决定）。
- **需要 spike 验证**：`SHEmptyRecycleBinW` 在 MSIX 包身份下能否直接调用、是否需要额外 capability、是否受 AppContainer 限制。这个放进 Task 1 的机制验证里，和下面孤儿目录/Workshop 的验证一起做。

### 0.2 隔离区：加大小展示 + 手动清空 + 默认保留期
- `ElevatedHelper` 侧 `RemoveResidualHandler` 已有的隔离目录（`ProgramData\PowerTools\Quarantine`）增加：
  - 一个只读查询操作（新 `IElevatedOperationHandler`：`quarantine.inspect`，返回总大小 + 各次清理的时间戳子目录列表），不需要新开权限，复用现有管道。
  - 一个清空操作（`quarantine.purge`，同样走提权帮手，因为隔离区本身是管理员权限创建的，普通用户进程未必能直接删）。
  - 默认保留策略：**不做自动静默删除**（隔离区的意义就是给用户后悔的机会，自动删除会违背这个设计前提）；改为在 UI 入口提示"隔离区已保留超过 30 天的清理，占用 X GB，是否清空"，由用户确认，不做后台定时任务。
- UI 落点：卸载工具模块的设置/关于区加一个"已清理的残留管理"入口，展示回收站本次操作占用 + 隔离区总占用，两个"清空"按钮分开（语义不同：回收站是系统级，隔离区是本应用专属）。

---

## 1. 孤儿安装目录检测（优先级 1）

### 目标
`steamapps/common/<folder>` 下没有任何 `appmanifest_*.acf` 的 `installdir` 指向它 → 候选"Steam 已经不认得，但文件还在占盘"的目录。

### 数据来源
复用 `SteamLibraryParser.FindLibraryFolders` / `ParseLibrary`；新增一个内部方法把所有库的 `installdir` 汇总成 `HashSet<string>`（不改变 `SteamGameInfo` 的公开结构）。

### 算法
1. 对每个库目录枚举 `steamapps/common/*` 一级子目录。
2. 不在 `installdir` 集合里的 → 候选。
3. 查 `Reference.db`（见第 4 节）的 `known_shared_folders` 表，命中则跳过（`SteamVR`、`Steamworks Common Redistributables` 等）。
4. 排除正在下载/更新中的目录：检查同名 `.acf` 是否在 `downloading` 状态（`StateFlags` 位），或目录的 `LastWriteTime` 在很短时间内（几分钟内）发生变化时先跳过，避免误判正在安装的游戏。
5. `FolderSizer.Measure` 算实际大小。

### 安全策略
只展示 + 用户确认后移入回收站（复用 §0.1 的回收站路径，不新造一套删除逻辑）；不需要管理员权限。

### 文件结构
```
src/PowerTools.Modules.DiskAnalyzer/Steam/
  OrphanFolderScanner.cs      扫描 + 白名单过滤 + 大小测量
  OrphanGameFolder.cs         record：LibraryPath, FolderName, FullPath, SizeBytes, LastWriteTimeUtc
tests/PowerTools.Modules.DiskAnalyzer.Tests/Steam/OrphanFolderScannerTests.cs
```

### Review Focus（易错点）
- 正在安装/更新中的游戏目录不能被判定为孤儿（最容易出的误报）。
- 白名单缺失条目导致误报——这是 `Reference.db` 需要持续补充维护的原因，不是一次性能穷举完的。
- 多个库文件夹路径重复/符号链接导致同一个目录被扫两次。

---

## 2. Workshop 残留检测（优先级 2）

### 目标
`steamapps/workshop/content/<appid>/`（以及 `appworkshop_<appid>.acf`）在对应游戏卸载后不会被清理——这是 Steam 官方长期已知行为（Reddit 20 帖验证，跨多个游戏社区）。

### 算法
1. 枚举每个库 `steamapps/workshop/content/*`，文件夹名即 appid。
2. 和 `GetSteamGames()` 返回的已安装 appid 集合求差集，不在其中的 = 候选残留。
3. 查本地缓存库（`cache.db`，见第 4 节）的 `steam_apps` 表取游戏名；查不到就显示 `appid:<id>`，不编造名字。
4. `FolderSizer.Measure` 算大小。

### 安全策略（比孤儿目录更谨慎——调研里有玩家故意留着 mod 等将来重装）
- 默认不勾选，仅展示。
- 删除确认文案必须披露后果："该游戏当前未安装；删除后如重新安装并订阅，需要重新下载这些内容。"
- 走回收站路径，不永久删。

### 文件结构
```
src/PowerTools.Modules.DiskAnalyzer/Steam/
  WorkshopResidueScanner.cs
  WorkshopResidueItem.cs      record：AppId, GameNameOrNull, FullPath, SizeBytes
src/PowerTools.Core/Cache/
  SteamAppCache.cs            读写 cache.db 的 steam_apps 表（见第 4 节）
```

### 写入点
`DiskAnalyzerModule.GetSteamGames()` 每次正常扫描时，顺手 `SteamAppCache.UpsertAsync(appid, name)`——这是让"卸载后还能显示游戏名"成立的关键，必须确认这个写入点在当前每次打开磁盘分析页都会触发，不需要额外的后台任务。

---

## 3. 批量卸载 + 空间预览（优先级 3）

### 范围澄清（纠偏）
Steam 客户端已支持 Shift+多选批量卸载，所以"能不能多选"不是卖点。卖点是：**跨 Win32 + Store + Steam 统一操作 + 卸载前预览总共能回收多少空间**，这点官方和调研里找到的第三方工具都没做。

### 设计
1. `UninstallerView.AppList` 的 `SelectionMode` 从 `Single` 改成支持多选（复用 `ResidueList` 已有的 CheckBox 列表模板，保持 UI 一致性，不用改选中态的既有事件逻辑）。
2. 新方法 `UninstallerModule.PreviewBatchAsync(IReadOnlyList<InstalledApp> apps)`：对每个 app 调 `MeasureSizeAsync`（已有方法），汇总 `Σ SizeBytes`，返回给 UI 显示确认条"选中 N 项，预计释放 X GB"。
3. 执行阶段**顺序执行**（复用现有单项 `UninstallAsync`，不并发），避免多个 UAC/卸载进程互相冲突；每完成一项更新进度和"已释放"的累计大小（这里"已释放"要诚实：Win32/Store 卸载是真删除，立刻生效；但卸载后发现的 Residue 仍然只是移入回收站，提示语要分开说清楚）。
4. 批量跑完后，汇总所有被卸载 app 各自的 `Leftovers` + 新的孤儿目录/Workshop 残留扫描结果，合并成一个"还能清理 X GB"的收尾提示——把三个功能串成一个"一键腾空间"的闭环体验。

### 文件结构
```
src/PowerTools.Modules.Uninstaller/
  Routing/BatchPreview.cs     PreviewBatchAsync 用的 record + 汇总逻辑
src/PowerTools.App/Views/UninstallerView.xaml(.cs)   多选 UI + 预览条 + 进度
```

---

## 4. 参考数据 / 本地缓存——不用 SQLite，用 JSON（更轻量，复用现有模式）

重新评估：两处都是"按 key 查值"的小规模数据，用不到关系查询/索引，SQLite 是过度设计。改用项目里已有先例的方案——`History/SnapshotStore.cs` 就是"按 key 存一份 JSON 文件"的模式，直接照搬。

### (a) `KnownSharedFolders.json`——只读，随包分发，开发者用 LLM 离线采集维护
```json
{
  "steamvr": "SteamVR 共享运行时",
  "steamworkscommonredistributables": "Steamworks 共享可再发行组件"
}
```
- 作为内容文件打进 MSIX 包；启动时一次性反序列化成 `Dictionary<string,string>`（key 用 `ResidueScanner.Normalize` 同一套归一化规则），内存里查，O(1)。
- 维护方式不变：开发者离线用 LLM 辅助批量生成候选 + 人工抽样核对后更新 JSON 文件，不在运行时联网更新。
- 规模即使到几千条，整份加载进内存也是几百 KB 级别，没有性能问题。

### (b) `Cache.json`——可写，`%LOCALAPPDATA%\PowerTools\Cache.json`，程序自己维护
```json
{
  "steamApps": { "413150": { "name": "Stardew Valley", "lastSeenUtc": "..." } },
  "ignoredItems": { "D:\\SteamLibrary\\steamapps\\common\\Foo": { "reason": "user-dismissed", "createdUtc": "..." } }
}
```
- 复用 `SnapshotStore` 的读写模式（`JsonSerializer` + 整份读/整份写），规模很小，不需要局部更新/并发写锁的复杂度。
- 纯本地读写，不联网、不上报，不涉及隐私审核风险。
- `ignoredItems` 供孤儿目录/Workshop 残留列表的"不是这个,别再提示"按钮使用，两个扫描器共用同一份文件。

### 待验证项（需要一次机制 spike，参照已有的 `2026-10-25-uninstaller-mechanism-spike.md` 做法）
- 内容文件（`KnownSharedFolders.json`）在 MSIX 打包后的访问路径（`Package.Current.InstalledLocation` vs `AppContext.BaseDirectory`）是否一致可靠。
- `SHEmptyRecycleBinW` 在包身份下的实际行为（§0.1）。
- 隔离区清空操作的提权帮手新增两个 operation type 后，既有的白名单校验模式要怎么扩展而不破坏现有 `uninstall.removeResidual` 的测试。

---

## 范围边界（已决定）
孤儿目录检测**只做 Steam**，不扩展到普通 Win32 软件。原因：Steam 的 `appmanifest_*.acf` 是结构化的"真相文件"，判定几乎零误报；普通软件没有这种文件，`InstallLocation` 不可靠、便携版软件没有注册表项，盲扫 Program Files 容易把用户正在用的便携工具误判成垃圾，风险和现有功能的"安全可恢复"定位不符。如果以后要做，应该做成独立的"深度扫描"选项（默认不勾选任何结果 + 强警示文案 + 只看长期无变动的文件夹），不混进这次的三个功能。

## 落地顺序
1. §0 前提修复（回收站清空入口 + 隔离区管理入口）——必须先做，否则后面三个功能的"已清理"提示都不诚实。
2. §4 参考数据/本地缓存（先搭 `Cache.json` 的读写类 + 单测，`KnownSharedFolders.json` 可以先放一张小白名单，后续再补）。
3. §1 孤儿目录检测（Steam 专属，复用度最高，纯用户权限）。
4. §2 Workshop 残留检测（复用 §4 的 `Cache.json`）。
5. §3 批量卸载预览（依赖前两者的残留汇总才能发挥"一键腾空间"的联动价值）。

## 需要先做机制验证（Task 1,和历史 spike 一致的做法）
- [ ] `SHEmptyRecycleBinW` 包身份下可用性
- [ ] `Microsoft.Data.Sqlite` 只读内容文件在 MSIX 包内的路径解析
- [ ] `ElevatedHelper` 新增 `quarantine.inspect` / `quarantine.purge` 两个 operation 的管道/白名单扩展方式


---

## 实现状态（2026-10-26 更新）

全部完成，按 commit 顺序：

1. `refactor: move IRecycleBin to PowerTools.Core` — 模块共享基础设施
2. `feat: add KnownSharedFolders whitelist` — §1 白名单
3. `feat: add OrphanFolderScanner` — §1 核心扫描逻辑
4. `feat: expose orphan folder detection ... on DiskAnalyzerModule` — §1 模块接入
5. `feat: add orphan-folder review panel` — §1 UI
6. `feat: add SteamAppCache` — §2 名字缓存
7. `feat: add WorkshopResidueScanner` — §2 核心扫描逻辑
8. `feat: wire workshop residue detection into DiskAnalyzerModule` — §2 模块接入（含测试隔离修复）
9. `feat: add workshop-residue review panel` — §2 UI
10. `feat: add quarantine inspect/purge operations` — §0 ElevatedHelper 侧
11. `feat: add IRecycleBin.Empty()` — §0 真正清空回收站
12. `feat: expose recycle-bin empty and quarantine inspect/purge on the modules` — §0 模块接入
13. `feat: add recycle-bin empty and quarantine management buttons to the UI` — §0 UI
14. `feat: add batch uninstall with space preview` — §3 批量卸载预览

全量测试：565 个全部通过（DiskAnalyzer 133、Uninstaller 182、Core 43、ElevatedHelper 52、ContextMenu 156）。

**与原设计的偏差**（均已在对话中确认）：
- §4 的"参考数据/缓存"换成了纯 JSON 文件（`KnownSharedFolders.json` 嵌入资源 + `SteamAppCache.json` 本地文件），未引入 SQLite。
- 孤儿目录检测范围确定为仅 Steam，不扩展到普通软件。
- §3 批量卸载预览没有把 `AppList` 整体改成多选模式（风险太高），改为给每行加独立的 CheckBox，不影响现有单选/拖拽定位等流程；卸载前预览的是"已知大小"的总和，未知大小的项目会在确认框里单独提示。
- 批量卸载里，"需要管理员权限"的项目直接跳过（不逐个弹 UAC 确认），统一在结果里提示用户之后单独用"以管理员身份重试"处理；这是为了避免批量流程里连续弹出多个确认框打断操作。
