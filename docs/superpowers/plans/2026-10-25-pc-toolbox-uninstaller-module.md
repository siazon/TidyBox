# PC 维护工具箱 —— 卸载工具模块 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现卸载工具作为一个 `IToolModule`：一个统一的"已安装项目"列表（Win32 / Microsoft Store / Steam），按来源路由到三种卸载机制；Win32 卸载后做残留扫描，结果分组展示，用户勾选后**可恢复地**删除。

**Architecture:** 新增 `PowerTools.Modules.Uninstaller` 类库（纯逻辑，依赖 `PowerTools.Core`，**不引用**磁盘分析模块）。三个来源各有一个"目录"（读取已安装项目）；`UninstallRouter` 按来源分发；`ResidueScanner` 在卸载前后各扫一次已知根目录/注册表根，得出"仍然存在且与该应用相关"的残留；`ResidueRemover` 把文件夹送进回收站；注册表残留只展示不删除（包身份下删不掉，见 spike）。需要管理员权限的文件夹（`Program Files`、`ProgramData`）通过已有的 `IElevationClient` 交给提权帮手，帮手端用**白名单**自行再校验路径。注册表/文件系统根都可注入，测试在沙箱里跑。

**Tech Stack:** .NET 8（`net8.0-windows10.0.19041.0`，为了 `Windows.Management.Deployment.PackageManager`）, xUnit, `Microsoft.Win32.Registry`, `Microsoft.VisualBasic.FileIO`（回收站）, WinUI3。

**Spec:** `docs/superpowers/specs/2026-10-24-pc-maintenance-toolbox-design.md`（"卸载工具模块"一节）

## Global Constraints

- .NET 8，只支持 Windows 11，App 以普通用户权限运行；只有 `Program Files` / `HKLM` 的残留清理才经 `ElevationClient` 临时提权（操作类型 `uninstall.removeResidual`，见 `ElevationRequest` 注释）。
- 删除必须可恢复：文件夹**只进回收站**（需要管理员的移到隔离目录）。直接删除属于设计文档的反目标。
- **注册表残留只展示、不提供删除**（Task 1 实测：包身份下 `HKCU` 和 `HKLM` 的删除都被虚拟化，真实键删不掉，见 `2026-10-25-uninstaller-mechanism-spike.md`）。因此本模块不写 `.reg` 备份、不删注册表。
- 残留结果必须分组/过滤，**不能**把原始 diff 甩给用户（设计文档第 5 条，吸取 Revo 反馈）。
- 不把 GPL 项目的代码编译进来；Bulk Crap Uninstaller（Apache 2.0）可借鉴思路。
- 模块之间不互相引用内部实现：本模块**不**引用 `PowerTools.Modules.DiskAnalyzer`。
- 免费范围：列表、三条卸载路由、卸载后残留扫描与审核删除。**不在本计划内**：强制残留清理（程序已卸载/卸载失败时仍强删）、批量卸载（付费功能，等付费解锁机制定了再做）、非 Steam 大型应用识别、卸载**前**的安装监控。
- 用户可见文案用简体中文。
- 应用以 MSIX 包身份运行：`HKCU\Software` 下的写入/删除默认被虚拟化，`unvirtualizedResources` 目前只排除了 `Software\Classes` 和 `Shell Extensions`。**不要为了本模块把排除范围扩大到整个 `HKCU\Software`**，是否可行由 Task 1 决定。

## 已知的真实情况（决定了边界用例，实现前仍以 Task 1 的实测为准）

- Win32 已安装程序登记在三个位置：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`（64 位）、`HKLM\SOFTWARE\WOW6432Node\...\Uninstall`（32 位）、`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`（每用户）。App 是 32 位进程，**必须显式用 `RegistryView.Registry64`** 打开 `HKLM`，否则 64 位那一份会被重定向掉（右键菜单模块踩过同样的坑）。
- 要过滤掉的条目：没有 `DisplayName`、`SystemComponent=1`、有 `ParentKeyName`（更新补丁）、`ReleaseType` 为 `Update`/`Hotfix`/`Security Update`。
- `UninstallString` 的常见形态：带引号的路径 + 参数（`"C:\Program Files\X\unins000.exe" /SILENT`）、不带引号的路径、`MsiExec.exe /I{GUID}`（应改成 `/X{GUID}` 才是卸载）、`rundll32 ...`。
- Steam 游戏在 `Uninstall` 下登记为 `Steam App <appid>`，其 `UninstallString` 形如 `"…\steam.exe" steam://uninstall/<appid>`——所以 Steam 来源可以靠 `UninstallString` 识别，不需要读 Steam 的 `appmanifest`，也就不需要引用磁盘分析模块。**Task 1 在真实机器上确认。**
- Microsoft Store 应用来自 `PackageManager.FindPackagesForUser("")`；要排除框架包（`IsFramework`）、资源包（`IsResourcePackage`）、系统包（`SignatureKind == System`）。

## Review Focus

- 应用名很短或很通用（`Git`、`Java`、`Office`）或发布者是 `Microsoft`/`Intel`/`NVIDIA`：名称匹配不能把其他应用的目录当成残留（Task 4）。
- 残留候选恰好是根目录本身（`C:\Program Files`、`%AppData%`）、系统目录或包含其他已安装应用的目录：永远不进入候选列表，也不能被勾选删除（Task 4、Task 6）。
- 卸载程序启动失败、被用户取消（退出码非 0）或程序本来就不存在：不能继续做残留扫描并暗示"已卸载"；列表在失败后仍要能刷新（Task 3、Task 7）。
- 卸载程序自己启动一个子进程后立刻退出（MSI、`Update.exe`、自解压器）：等待逻辑要给出明确的"可能尚未完成"，不能把"进程退出了"当成"卸载完成"（Task 3）。
- 删除过程中某个路径被占用/拒绝访问/已不存在：只让这一项失败并给出原因，其余项继续（Task 5、Task 6）。
- 提权帮手收到的路径不在白名单内（被篡改的请求）：必须拒绝，不能因为请求来自我们自己的管道就照做（Task 6）。
- 注册表残留在界面里不可勾选、不可删除，不能被"全选/删除选中项"带上（Task 5、Task 7）。

---

## File Structure

```
src/PowerTools.Modules.Uninstaller/
  PowerTools.Modules.Uninstaller.csproj
  Model/InstalledApp.cs            InstalledApp / AppSource / UninstallCommand
  Catalog/Win32AppCatalog.cs       读三个 Uninstall 根，过滤，识别 Steam
  Catalog/IStorePackageSource.cs   接口 + StorePackage 记录 + 真实实现（PackageManager）
  Catalog/StoreAppCatalog.cs       把 StorePackage 过滤成 InstalledApp
  Routing/UninstallCommandParser.cs  UninstallString → 可执行文件 + 参数（含 MsiExec /I→/X）
  Routing/UninstallRouter.cs       按来源分发；Win32 等待退出；Store 走 Task 1 选定的机制；Steam 打开 URL
  Residue/ResidueItem.cs           ResidueItem / ResidueKind / ResidueConfidence
  Residue/ResidueRoots.cs          可注入的文件夹根 + 注册表根；固定的受保护路径
  Residue/ResidueScanner.cs        卸载前快照 + 卸载后匹配
  Residue/ResidueRemover.cs        文件夹进回收站；需要提权的走 IElevationClient；注册表项拒绝
  Residue/IRecycleBin.cs           接口 + 真实实现
  UninstallerModule.cs             IToolModule + 对界面暴露的工作流
src/PowerTools.ElevatedHelper/RemoveResidualHandler.cs   uninstall.removeResidual 的帮手端实现（白名单校验）
tests/PowerTools.Modules.Uninstaller.Tests/
  Support/RegistrySandbox.cs       Uninstall 根沙箱（HKCU\Software\PowerToolsTests\<guid>）
  ... 与上面一一对应的测试文件
tests/PowerTools.ElevatedHelper.Tests/RemoveResidualHandlerTests.cs
src/PowerTools.App/Views/UninstallerView.xaml(.cs)   界面
docs/superpowers/specs/2026-10-25-uninstaller-mechanism-spike.md   Task 1 的结论记录
```

---

## Task 1: 机制验证（需要人工，先于所有实现）

三个未知会改变实现路径，文档没有说清，不能靠猜。**先验证，再写代码。** 可以用一个脚本/小控制台程序辅助，但"打包后的 App 里是否生效"必须在真实 MSIX 部署里看。

**Files:**
- Create: `docs/superpowers/specs/2026-10-25-uninstaller-mechanism-spike.md`

**Interfaces:** 无（产出是一份结论文档，后续任务按它选择实现路径）。

- [ ] **Step 1: 验证 A——Store 应用卸载。** 找一个无害、可随时重装的 Store 应用（如"便笺"以外的小应用，用后在 Store 重装）。在**已部署的 MSIX 包**里（临时在 App 里加一个按钮，验证完删掉）依次试：(1) `new PackageManager().RemovePackageAsync(fullName)`；(2) 失败的话，`Process.Start("powershell.exe", "-NoProfile -Command Remove-AppxPackage <fullName>")`。记录：各自是否成功、报什么错（`ERROR_ACCESS_DENIED` / 需要 `packageManagement` 能力 / 其他）、是否弹 UAC。

- [ ] **Step 2: 验证 B——注册表残留能否真正删除。** 在真实注册表里手建 `HKCU\Software\PowerToolsSpike\Leftover` 和 `HKLM\SOFTWARE\PowerToolsSpike\Leftover`（用 `regedit` 建，不要在包里建）。从已部署的 MSIX 包里分别：用 `RegistryKey.DeleteSubKeyTree` 删 `HKCU` 那个；通过提权帮手删 `HKLM` 那个。用 `regedit` 查看**真实**注册表，记录是否真的被删掉（被虚拟化的话包内读取显示已删、`regedit` 里仍在）。

- [ ] **Step 3: 验证 C——Steam 登记形态。** 读取本机 `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App *`，记录 `DisplayName`、`UninstallString` 的真实内容，确认能靠 `steam://uninstall/<digits>` 识别；记录有没有已装 Steam 但没有这类键的游戏。

- [ ] **Step 4: 把 A–C 的结果写进 `2026-10-25-uninstaller-mechanism-spike.md`**（Windows build 号、每项"是否生效 / 报错原文"），并按下表给出结论，提交。

  | 结果 | 影响 |
  |---|---|
  | A(1) 成功 | Store 路由用 `PackageManager`；Task 3 照写，并在 `Package.appxmanifest` 里按需补能力声明（记录 Store 审核风险）。 |
  | A(1) 失败、A(2) 成功 | Store 路由改为启动 PowerShell 命令（不需要受限能力）；`UninstallRouter` 的 Store 分支换实现，测试接口不变。 |
  | A 都失败 | Store 路由改为打开 `ms-settings:appsfeatures`，列表里对 Store 应用只提供"打开系统设置卸载"；在文档里写明原因。 |
  | B 的 `HKCU` 删除被虚拟化 | `HKCU\Software` 下的注册表残留**不提供删除**，只展示并提示手动处理；同时在文档里记录"需要扩大 `unvirtualizedResources` 的 Store 风险"，由你决定是否值得。 |
  | B 的 `HKLM` 经帮手删除被虚拟化/拒绝 | 注册表残留整体降级为"只展示不删除"，文件残留不受影响。 |
  | C 的 Steam 键不可靠 | Task 2 增加 `appmanifest` 回退识别，依赖的 Steam 解析代码复制进本模块而不是引用磁盘分析模块。 |

---

## Task 2: Win32 与 Steam 目录

**Files:**
- Create: `src/PowerTools.Modules.Uninstaller/PowerTools.Modules.Uninstaller.csproj`（`net8.0-windows10.0.19041.0`，引用 `PowerTools.Core`，`InternalsVisibleTo` 测试项目）
- Create: `src/PowerTools.Modules.Uninstaller/Model/InstalledApp.cs`, `src/PowerTools.Modules.Uninstaller/Catalog/Win32AppCatalog.cs`
- Create: `tests/PowerTools.Modules.Uninstaller.Tests/PowerTools.Modules.Uninstaller.Tests.csproj`, `Support/RegistrySandbox.cs`, `Catalog/Win32AppCatalogTests.cs`
- Modify: `PowerTools.sln`（只加这两个新项目；**该文件里已有用户未提交的 `App1` 改动，不要整理、不要还原，只追加**）

**Interfaces:**
- Produces: `enum AppSource { Win32, Store, Steam }`
- Produces: `record InstalledApp(string Id, string DisplayName, string? Publisher, string? Version, long? EstimatedSizeBytes, DateTime? InstalledOn, AppSource Source, string? InstallLocation, string? UninstallString, string? SteamAppId, string? PackageFullName)` —— `Id` 是来源内唯一键（Win32 = `"<hive>\\<子键名>"`，Store = `PackageFullName`）。
- Produces: `class Win32AppCatalog { Win32AppCatalog(RegistryKey machine64, RegistryKey machine32, RegistryKey user); IReadOnlyList<InstalledApp> LoadAll(); }`；`static Win32AppCatalog Real()` 用 `RegistryView.Registry64` 打开 `HKLM`、`Registry32` 视图读 WOW6432Node 那一份。

- [ ] **Step 1: 写失败的测试** `Win32AppCatalogTests`（沙箱里造三个根）：
  - `Lists_entries_from_all_three_roots`
  - `Skips_entries_without_display_name_system_components_and_updates`（`SystemComponent=1`、`ParentKeyName`、`ReleaseType=Update` 各一条，全被过滤）
  - `Detects_steam_games_from_uninstall_string`：`UninstallString` 为 `"C:\Steam\steam.exe" steam://uninstall/220` → `Source == Steam`、`SteamAppId == "220"`；`steam://uninstall/22x`（非纯数字）**不**识别为 Steam，按普通 Win32 处理。
  - `Reads_estimated_size_kb_as_bytes`（`EstimatedSize` 是 KB 的 DWORD，`InstallDate` 是 `yyyyMMdd` 字符串，格式坏了就是 null，不抛）
  - `One_unreadable_key_does_not_fail_the_whole_list`（某子键读取时抛出的情况用接口外的方式造不出时，至少覆盖"值类型不对"：`DisplayName` 写成 DWORD → 该条被跳过）。

- [ ] **Step 2: 运行测试确认失败。** Run: `dotnet test tests/PowerTools.Modules.Uninstaller.Tests`　Expected: 编译失败/FAIL，原因是 `Win32AppCatalog` 不存在。

- [ ] **Step 3: 实现** `Win32AppCatalog.LoadAll()`（`PowerTools.Modules.Uninstaller.Catalog`）：遍历三个根的 `SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`，按"已知的真实情况"里的规则过滤，Steam 识别用正则 `steam://uninstall/(\d+)\s*$`；每个子键用 try/catch 隔离。结果按 `DisplayName` 不区分大小写排序。

- [ ] **Step 4: 运行测试确认通过。** Expected: PASS。

- [ ] **Step 5: Commit** `feat: add Win32 and Steam installed-app catalog`

---

## Task 3: Store 目录与卸载路由

**Files:**
- Create: `Catalog/IStorePackageSource.cs`, `Catalog/StoreAppCatalog.cs`, `Routing/UninstallCommandParser.cs`, `Routing/UninstallRouter.cs`
- Test: `tests/PowerTools.Modules.Uninstaller.Tests/Catalog/StoreAppCatalogTests.cs`, `Routing/UninstallCommandParserTests.cs`, `Routing/UninstallRouterTests.cs`

**Interfaces:**
- Consumes: `InstalledApp`, `AppSource`（Task 2）
- Produces: `record StorePackage(string FullName, string DisplayName, string? Publisher, string? Version, DateTime? InstalledOn, string? InstallLocation, bool IsFramework, bool IsResource, bool IsSystem)`；`interface IStorePackageSource { IReadOnlyList<StorePackage> FindForCurrentUser(); }`（真实实现包一层 `PackageManager`，不在单元测试里跑）。
- Produces: `class StoreAppCatalog { StoreAppCatalog(IStorePackageSource source); IReadOnlyList<InstalledApp> LoadAll(); }` —— 过滤框架/资源/系统包，`Source = Store`。
- Produces: `record UninstallCommand(string FileName, string Arguments)`；`static class UninstallCommandParser { static UninstallCommand? Parse(string uninstallString); }` —— 返回 `null` 表示无法解析。
- Produces: `enum UninstallStatus { Completed, Failed, MaybeStillRunning, NotAvailable }`；`record UninstallOutcome(UninstallStatus Status, int? ExitCode, string? Message)`。
- Produces: `interface IProcessRunner { Task<int> RunAndWaitAsync(string fileName, string arguments, CancellationToken ct); }`、`interface IStoreRemover { Task RemoveAsync(string packageFullName, CancellationToken ct); }`、`interface IUrlLauncher { void Open(Uri uri); }`（`Steam`/`Store` 的真实实现按 Task 1 的结论写）。
- Produces: `class UninstallRouter { UninstallRouter(IProcessRunner runner, IStoreRemover store, IUrlLauncher urls); Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken ct = default); }`

- [ ] **Step 1: 写失败的测试。**
  - `StoreAppCatalogTests.Filters_out_framework_resource_and_system_packages`
  - `UninstallCommandParserTests`：带引号路径+参数；不带引号的 `C:\a\b.exe /S`；带空格但不带引号的路径（`C:\Program Files\X\u.exe /S`，要逐段尝试找到真实存在的 exe——测试里用临时文件造）；`MsiExec.exe /I{GUID}` → 参数变 `/X{GUID}`，`/X{GUID}` 保持不变；空串/纯空白 → `null`；
  - `UninstallRouterTests`（假的 runner/store/urls）：Steam → `urls.Open` 收到 `steam://uninstall/220`，不调 runner；`SteamAppId` 非纯数字 → `NotAvailable` 且什么都没启动；Win32 退出码 0 → `Completed`；退出码非 0 → `Failed` 且带退出码；runner 抛 `Win32Exception`（用户在 UAC 里取消，错误码 1223）→ `Failed`，消息说明"已取消"；`UninstallString` 为 null/解析失败 → `NotAvailable`；Win32 的命令是 `MsiExec` 或 `Update.exe`（启动后立刻转交子进程）→ 即使退出码为 0 也返回 `MaybeStillRunning`；Store → `store.RemoveAsync` 收到包全名，抛异常 → `Failed`。

- [ ] **Step 2: 运行测试确认失败。** Run: `dotnet test tests/PowerTools.Modules.Uninstaller.Tests --filter "FullyQualifiedName~Store|FullyQualifiedName~Routing"`　Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现** 上述类型。`UninstallRouter` 对 Win32 用 `Process` 启动并等退出；`MaybeStillRunning` 的判定规则是"可执行文件名是 `msiexec.exe` 或 `update.exe`"，其余按退出码。Store 的真实 `IStoreRemover` 按 Task 1 的结论实现（`PackageManager.RemovePackageAsync` / PowerShell / 只打开设置页）。Steam 的 URL 校验照 `SteamUninstallLauncher.BuildUri` 的做法自己写一份（只接受纯数字 appid），不引用磁盘分析模块。

- [ ] **Step 4: 运行测试确认通过。** Expected: PASS。

- [ ] **Step 5: Commit** `feat: route uninstalls to Win32, Store and Steam`

---

## Task 4: 残留扫描

核心赌注：**宁可漏掉，不可误报**。残留候选 = 卸载**后**仍存在、并且与该应用有可证明关联的文件夹/注册表键。"卸载前快照"用来排除卸载过程自己新建的东西（卸载器的日志/临时文件），只把"卸载前就存在、卸载后仍在"的算作残留。

**Files:**
- Create: `Residue/ResidueItem.cs`, `Residue/ResidueRoots.cs`, `Residue/ResidueScanner.cs`
- Test: `tests/PowerTools.Modules.Uninstaller.Tests/Residue/ResidueScannerTests.cs`

**Interfaces:**
- Consumes: `InstalledApp`（Task 2）
- Produces: `enum ResidueKind { Folder, RegistryKey }`、`enum ResidueConfidence { High, Medium }`（High = 就是 `InstallLocation` 本身或其内部；Medium = 名称匹配）。
- Produces: `record ResidueItem(ResidueKind Kind, string Path, ResidueConfidence Confidence, string Reason, bool NeedsElevation, bool Removable)`；`Path` 对注册表是 `HKCU\Software\X` / `HKLM\SOFTWARE\X` 形式；**注册表项恒为 `Removable == false`**（Task 1），文件夹为 `true`。
- Produces: `class ResidueRoots { IReadOnlyList<string> FolderRoots; IReadOnlyList<(string Hive, RegistryKey Key, string Path)> RegistryRoots; IReadOnlySet<string> ProtectedFolders; static ResidueRoots Real(); }` —— `Real()` 的文件夹根：`ProgramFiles`、`ProgramFiles(x86)`、`ProgramData`、`LocalAppData`、`AppData`（**不**含 `LocalAppData\Packages`、`Windows`、`System32`）；注册表根：`HKCU\Software`、`HKLM\SOFTWARE`、`HKLM\SOFTWARE\WOW6432Node`（只看根下一层子键）。`ProtectedFolders` 包含所有根本身和 `Microsoft`、`Windows`、`Common Files`、`Packages` 等。
- Produces: `class ResidueScanner { ResidueScanner(ResidueRoots roots); ResidueSnapshot Capture(InstalledApp app); IReadOnlyList<ResidueItem> FindLeftovers(InstalledApp app, ResidueSnapshot before); }`；`ResidueSnapshot` 是不透明的、只含"卸载前就存在的匹配候选路径集合"的类型。

- [ ] **Step 1: 写失败的测试** `ResidueScannerTests`（临时目录当文件夹根，沙箱当注册表根）：
  - `Install_location_that_survives_uninstall_is_high_confidence_residue`
  - `Folder_named_after_the_app_in_AppData_is_medium_confidence`
  - `Items_created_only_after_the_uninstall_are_ignored`（卸载器新建的日志目录）
  - `Items_already_removed_by_the_uninstaller_are_not_reported`
  - `Short_or_generic_names_never_match`：应用名 `Git`（≤3 个字符）或名称分词全是通用词（`Microsoft`、`Windows`、`Update`、`Setup`、`Tools`）时，名称匹配不产生任何候选（只有 `InstallLocation` 仍可）。
  - `Roots_and_protected_folders_are_never_candidates`：`InstallLocation` 恰好等于 `C:\Program Files` 或某个根 → 不返回；
  - `Folder_shared_with_another_installed_app_is_not_residue`：发布者目录（如 `…\Contoso`）下还有别的子目录属于另一个仍装着的应用时，只考虑本应用自己的子目录，不报整个发布者目录。
  - `Registry_residue_is_reported_but_never_removable`；`Folder_residue_under_program_files_needs_elevation_and_under_appdata_does_not`

- [ ] **Step 2: 运行测试确认失败。** Run: `dotnet test tests/PowerTools.Modules.Uninstaller.Tests --filter FullyQualifiedName~ResidueScanner`　Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现** `Capture`/`FindLeftovers`。匹配规则：把 `DisplayName`、`Publisher` 拆成小写分词，去掉版本号和通用词；候选名需包含**整个应用名**（归一化后）才算 Medium，避免只因发布者相同就命中；`InstallLocation` 做前缀判断（规范化路径、不区分大小写、以目录分隔符结尾比较，防止 `C:\Apps\Foo` 匹配到 `C:\Apps\FooBar`）。

- [ ] **Step 4: 运行测试确认通过。** Expected: PASS。

- [ ] **Step 5: Commit** `feat: scan for uninstall leftovers conservatively`

---

## Task 5: 可恢复的文件夹残留删除（用户权限部分）

注册表残留不在此处理（Task 1：被虚拟化，删不掉）。

**Files:**
- Create: `Residue/IRecycleBin.cs`, `Residue/ResidueRemover.cs`
- Test: `tests/PowerTools.Modules.Uninstaller.Tests/Residue/ResidueRemoverTests.cs`

**Interfaces:**
- Consumes: `ResidueItem`, `ResidueKind`, `ResidueRoots`（Task 4）, `PowerTools.Core.Elevation.IElevationClient`
- Produces: `interface IRecycleBin { void Delete(string path); }`（真实实现用 `Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)`）。
- Produces: `record RemovalResult(ResidueItem Item, bool Success, string? Error)`；`class ResidueRemover { ResidueRemover(IRecycleBin recycleBin, IElevationClient elevation, ResidueRoots roots); Task<IReadOnlyList<RemovalResult>> RemoveAsync(IReadOnlyList<ResidueItem> items, CancellationToken ct = default); }`

- [ ] **Step 1: 写失败的测试** `ResidueRemoverTests`（假的 `IRecycleBin`、假的 `IElevationClient`）：
  - `Folders_go_to_the_recycle_bin_not_deleted_directly`
  - `Registry_items_are_refused_and_never_touched`（`Removable == false` → `Success=false`，错误说明"无法在此删除"，回收站和帮手都没被调用）
  - `One_failing_item_does_not_stop_the_others`（被占用/不存在/拒绝访问各一，假回收站按路径抛异常）
  - `Items_needing_elevation_are_sent_to_the_helper_and_not_touched_locally`：假 `IElevationClient` 收到 `OperationType == "uninstall.removeResidual"` 的请求，payload `{"folders":[…]}`；不需要提权的不进请求；帮手返回逐项结果时映射回对应的 `RemovalResult`；帮手整体失败（`Success=false` 且无逐项结果）→ 这些项全部失败并带 `ErrorMessage`。
  - `Protected_paths_are_refused_before_anything_is_sent`：路径是根或在 `ProtectedFolders` 里 → 直接 `Success=false`，既不进回收站也不发给帮手。

- [ ] **Step 2: 运行测试确认失败。** Expected: FAIL，类型不存在。

- [ ] **Step 3: 实现** `ResidueRemover`，返回每一项的结果（顺序与输入一致）。

- [ ] **Step 4: 运行测试确认通过。** Expected: PASS。

- [ ] **Step 5: Commit** `feat: remove folder leftovers recoverably`

---

## Task 6: 提权帮手端的删除处理（安全敏感）

帮手以管理员权限运行，管道虽然只允许我们的用户连接，但**帮手不能无条件信任请求内容**。只处理文件夹。

**Files:**
- Create: `src/PowerTools.ElevatedHelper/RemoveResidualHandler.cs`
- Modify: `src/PowerTools.ElevatedHelper/Program.cs`（把 handler 加入 `handlers` 数组，替换"还没有真实 handler"那行注释）
- Test: `tests/PowerTools.ElevatedHelper.Tests/RemoveResidualHandlerTests.cs`

**Interfaces:**
- Consumes: `IElevatedOperationHandler`（`OperationType`、`Task<ElevationResponse> HandleAsync(string payloadJson)`）, `ElevationResponse`
- Produces: `class RemoveResidualHandler : IElevatedOperationHandler`，`OperationType == "uninstall.removeResidual"`；payload：`{"folders":["…"]}`；响应 `ResultJson` 为逐项结果 `[{"path":"…","success":true,"error":null}]`；构造函数接受可注入的白名单根（测试用临时目录）和隔离目录。
- 白名单：文件夹必须位于 `ProgramFiles`、`ProgramFiles(x86)`、`ProgramData` 之下的**至少一层子目录**，且该子目录名不是受保护名（`Microsoft`、`Windows`、`Common Files`、`Packages` 等）。路径先 `Path.GetFullPath` 规范化，拒绝含 `..`、符号链接/联接点指向白名单外的目录。

- [ ] **Step 1: 写失败的测试** `RemoveResidualHandlerTests`：
  - `Rejects_path_outside_allowed_roots`（`C:\Windows\System32\x`、`C:\Users\…`）→ 该项 `success=false`，什么都没动
  - `Rejects_path_traversal`（`…\Program Files\Foo\..\..\Windows`）
  - `Rejects_the_roots_themselves_and_protected_vendor_folders`
  - `Rejects_junction_that_escapes_the_allowed_root`（临时目录里用 `mklink /J` 造联接点指向白名单外）
  - `Allowed_folder_is_moved_to_quarantine_not_deleted`：处理后原路径不存在、隔离目录 `%ProgramData%\PowerTools\Quarantine\<时间戳>\` 里有该文件夹（帮手不用回收站，对提权进程它按管理员用户算，用户找不到）。
  - `Mixed_request_reports_per_item_results`：一个被拒绝、一个成功，整体 `Success` 为 false，`ResultJson` 逐项说明。

- [ ] **Step 2: 运行测试确认失败。** Run: `dotnet test tests/PowerTools.ElevatedHelper.Tests`　Expected: FAIL，`RemoveResidualHandler` 不存在。

- [ ] **Step 3: 实现** `RemoveResidualHandler` 并在 `Program.cs` 注册。移动失败（跨卷等）则放弃该项并报告原因，**不**回退成直接删除。

- [ ] **Step 4: 运行测试确认通过**，并运行 `dotnet test tests/PowerTools.ElevatedHelper.Tests` 全部（原有 8 个仍应通过）。Expected: PASS。

- [ ] **Step 5: Commit** `feat: add elevated folder quarantine with an allowlist`

---

## Task 7: 模块工作流与界面

**Files:**
- Create: `src/PowerTools.Modules.Uninstaller/UninstallerModule.cs`
- Create: `src/PowerTools.App/Views/UninstallerView.xaml`, `UninstallerView.xaml.cs`, `Views/UninstallRow.cs`（沿用 `MenuRow` 的做法：行对象在 App 里，逻辑在模块里）
- Modify: `src/PowerTools.App/App.xaml.cs`（`Modules.Register(new UninstallerModule(...))`）, `src/PowerTools.App/MainWindow.xaml.cs`（`Pages["uninstaller"] = typeof(UninstallerView)`）, `src/PowerTools.App/PowerTools.App.csproj`（项目引用）
- Test: `tests/PowerTools.Modules.Uninstaller.Tests/UninstallerModuleTests.cs`

**Interfaces:**
- Consumes: 上述全部。
- Produces: `class UninstallerModule : IToolModule`，`ModuleId == "uninstaller"`，`DisplayName == "卸载工具"`；`Task<IReadOnlyList<InstalledApp>> LoadAppsAsync()`；`Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct = default)`，其中 `record UninstallResult(UninstallOutcome Outcome, IReadOnlyList<ResidueItem> Leftovers)`；`Task<IReadOnlyList<RemovalResult>> RemoveResidueAsync(IReadOnlyList<ResidueItem> items, CancellationToken ct = default)`。

- [ ] **Step 1: 写失败的测试** `UninstallerModuleTests`（全部依赖注入假实现）：
  - `LoadApps_merges_win32_store_and_steam_sorted_by_name`
  - `Win32_uninstall_that_completes_returns_leftovers_found_after_it`
  - `Failed_uninstall_returns_no_leftovers_and_does_not_scan`
  - `Steam_and_Store_uninstalls_do_not_scan_for_leftovers`（Steam 由 Steam 自己清理；Store 包由系统清理）
  - `MaybeStillRunning_returns_no_leftovers_but_keeps_the_outcome`（界面据此提示"卸载可能还在进行，稍后重新扫描"）
  - `InitializeAsync_throws_when_no_uninstall_root_can_be_read`

- [ ] **Step 2: 运行测试确认失败。** Expected: FAIL，`UninstallerModule` 不存在。

- [ ] **Step 3: 实现** `UninstallerModule`：`UninstallAsync` 先 `Capture`（仅 Win32），调用路由，`Completed` 才 `FindLeftovers`。界面：顶部搜索框 + 来源筛选（全部/Win32/商店/Steam）+ 列表（名称、发布者、版本、大小、来源）；选中后"卸载"按钮 → `ContentDialog` 二次确认；卸载完成后若有残留，弹出分组审核面板（按 文件夹 / 注册表 分组，高置信度文件夹默认勾选、中置信度默认不勾选，需要管理员的项旁标注"需要管理员权限"；注册表项灰显、不可勾选，旁注"无法在此删除，请手动处理"），"删除选中项"调用 `RemoveResidueAsync`，完成后提示"已移到回收站（或隔离目录 %ProgramData%PowerToolsQuarantine）"。列表控件沿用 `ListView`，不要给 `ScrollViewer` 加会拦截滚轮的包装（上一轮刚修过滚轮问题）。

- [ ] **Step 4: 运行测试确认通过，再跑全部测试。** Run: `dotnet test tests/PowerTools.Core.Tests; dotnet test tests/PowerTools.ElevatedHelper.Tests; dotnet test tests/PowerTools.Modules.ContextMenu.Tests; dotnet test tests/PowerTools.Modules.DiskAnalyzer.Tests; dotnet test tests/PowerTools.Modules.Uninstaller.Tests`　Expected: 全部 PASS（不要直接对 `.sln` 跑，里面有用户的 `App1`）。再 `dotnet build src/PowerTools.App -p:Platform=x86` 确认能编译。

- [ ] **Step 5: Commit** `feat: add uninstaller module view and wire it into the shell`

---

## Task 8: 真机手动验证（需要人工）

**Files:** 无（结果记入 `2026-10-25-uninstaller-mechanism-spike.md` 末尾"Task 8 手动验证"一节）

- [ ] **Step 1:** 部署后（单项目 MSIX 的松散部署不会随 `dotnet build` 刷新，需要先停掉 App，再把构建输出 `robocopy` 进 `bin\<plat>\Debug\…\AppX`）核对列表：与"设置 → 应用 → 已安装的应用"比对数量与名称，记录差异（预期：我们过滤掉更新补丁与系统组件）。
- [ ] **Step 2:** 卸载一个无害的 Win32 程序（先装一个小工具，如 7-Zip 便携以外的安装版），确认：UAC 由卸载程序自己弹出、取消后界面显示"已取消"、完成后出现残留面板、勾选 `AppData` 下的文件夹删除后出现在回收站里。
- [ ] **Step 3:** 对一个需要提权的残留（`Program Files` 下遗留目录）执行删除，确认出现一次 UAC、目录出现在 `%ProgramData%\PowerTools\Quarantine`。
- [ ] **Step 4:** 卸载一个 Steam 游戏（只确认 Steam 弹出卸载确认框）与一个 Store 应用（按 Task 1 选定的机制）。
- [ ] **Step 5:** 记录未覆盖的问题，提交文档。
