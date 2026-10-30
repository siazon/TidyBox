# TidyBox — Microsoft Store 上架材料

整理日期：2026-10-25；2026-10-29 补上"文件整理"模块，并纠正"不联网"的说法（反馈功能已接到线上服务）。依据：Store Policies 7.20（2026-09-14 版）、App capability declarations（2026-10-02 版），以及本仓库当前代码和实测。
标 **［实测］** 的是在本机开发包上验证过的；标 **［未验证］** 的只有文档依据，没在 Store 签名的包上试过。

## 0. 先看这三个决定

| # | 问题 | 为什么重要 | 我的建议 |
|---|---|---|---|
| 1 | **`unvirtualizedResources` 很可能过不了审** — **已解决** | 官方原文：这个 capability "is designed for certain types of desktop PC games published by Microsoft and our partners … It is not intended to be used for other scenarios, because it could compromise the system's ability to uninstall cleanly."（并与政策 10.2.7 "cleanly uninstall" 相冲）。审核不通过则整个提交失败 | 右键菜单模块的写入已改为 `reg.exe` 子进程（`RegExeRegistryWriter`），manifest 里已去掉这个 capability 和 `virtualization:` 块。在打包应用里关/开"经典右键菜单"开关，真实注册表的键随之消失/恢复 ［实测］ |
| 2 | **`packageManagement`** 要不要声明 | 官方说它是 `PackageManager` 类"部分方法"所需。本机开发包上不声明也能卸载商店应用（天气 ［实测］），但那是旁加载包 | 先不声明；用 Store 流程里的预认证包（或 WACK + 一次真实提交的 flight）复测。若失败，改为子进程调 `Remove-AppxPackage`（子进程没有包身份，不受这个 capability 限制），不申请它 |
| 3 | **产品描述的措辞** | Microsoft 对"清理/优化"类软件的反感集中在恐吓式宣传；政策 10.2.3 引用了其"Unwanted and Malicious Software"标准 | 只描述功能，不写"提速""修复"，不写系统问题数量，不做危言耸听的提示（见第 4 节） |

## 1. 清单中的 capability

当前 manifest：只有 `runFullTrust`。没有 `unvirtualizedResources`（已去掉，见 1.2），没有 `requireAdministrator`，没有 `allowElevation`。

提交时 Partner Center 会在 **Submission options** 页要求逐项写明用途；官方说文字越详细越好，审核员据此决定是否批准，不批准则该次提交失败，可以去掉 capability 重新提交。
有人反映 `runFullTrust` 的说明框字数上限很低、超出会被无声截断，所以每项先给**短版**（粘进框里），长版放"认证备注"（第 5 节）。

### 1.1 `runFullTrust` — 必需，门槛低

官方：中等完整性级别（full trust）的打包桌面应用必须声明它。

短版（粘进说明框）：
> Packaged WinUI 3 desktop app (.NET). It runs as a normal-user, medium-integrity process to read installed-program data, start each program's own uninstaller, and move files inside a folder the user picks (after the user confirms).

### 1.2 `unvirtualizedResources` — 已去掉，不申请

右键菜单模块要把"禁用某个菜单项"写进用户真实的 `HKCU\Software\Classes` 和 `...\Shell Extensions\Blocked`，资源管理器才看得到。打包应用自己的注册表写入会被虚拟化，所以原来靠这个 capability 排除这两个键。官方说它不是给这类应用用的，所以现在改成：

- 写入一律走 `reg.exe` 子进程（`RegExeRegistryWriter`，参数列表，不拼命令行字符串，`/reg:64`），读取仍在进程内。
- manifest 里已去掉 capability 和 `virtualization:` 块。
- 取舍：每次写操作多一个子进程。"卸载后这些改动会留在用户注册表里"这一点和原来一样，所以描述里必须说明，并建议提供"恢复全部"入口（见 4.3）。

### 1.3 不声明的

- `allowElevation`：官方说明它给微软合作伙伴/企业用，且审批严格；我们不需要，提权靠 `runas` + UAC。
- `packageManagement`：见第 0 节决定 2。

## 2. 提权机制说明（给审核员，也是自查清单）

事实（均来自代码）：

- 应用本身以**普通用户**运行，日常不弹 UAC。
- 只有用户**主动点"删除选中项"** 清理 `Program Files`/`ProgramData` 下的残留，或清理 `HKLM\SOFTWARE` 下的残留键，才会通过 `runas` 启动随包的 `TidyBox.ElevatedHelper.exe`，弹出 Windows 自己的 UAC。拒绝 UAC 则该操作失败，应用不受影响。
- 帮手通过**随机命名的命名管道**与应用通信，管道只允许启动它的那个用户的 SID 连接；空闲 5 分钟自动退出。
- 帮手**不信任请求**：路径必须在白名单根目录下（`Program Files`、`Program Files (x86)`、`ProgramData`），不得经过符号链接/junction，Windows 自带和多程序共享的名字（`Microsoft`、`Windows`、`WindowsApps`、`Common Files` 等）一律拒绝；注册表键只允许 `HKLM\SOFTWARE[\WOW6432Node]\<厂商>[\<应用>]`，共享键名拒绝。
- 帮手**不删除**：文件夹被移动到 `C:\ProgramData\TidyBox\Quarantine\<时间戳>\`，注册表键先用 `reg export` 备份到同一隔离区，备份文件不存在或为空则不删除。
- 隔离区目录属主为 Administrators，权限不继承，只有 SYSTEM 和 Administrators 可访问 ［实测，提权下核对过］。
- "以管理员身份重试"（卸载程序只允许管理员运行时）：先弹确认对话框，用户同意后通过提权的 `cmd.exe` 启动该程序登记的卸载命令。命令里含 `& | < > ^ %` 或换行时拒绝。UAC 窗口里显示的程序是 `cmd.exe`。
- "分析此文件夹"右键菜单（可选，由用户开启）通过 manifest 声明的应用执行别名 `TidyBox.exe`（`windows.appExecutionAlias`，不需要任何 capability）启动应用，命令是 `TidyBox.exe --analyze "<文件夹>"`。若应用已在运行，新启动的副本通过本地命名管道（名称含当前用户 SID，只接受当前用户连接，只收一条不超过 32 KB 的绝对路径）把文件夹交给已运行的窗口后立即退出。收到路径后只做磁盘扫描（只读），不修改任何东西。
- 目前帮手只注册了一个操作：`uninstall.removeResidual`。右键菜单模块只写 `HKCU`，不使用帮手。
- "文件整理"模块以普通用户运行，**不使用帮手、不提权**：只在用户选定的文件夹里动文件（默认下载文件夹；磁盘根目录、Windows、Program Files、ProgramData 及其子文件夹、用户主目录本身不能选）；每个路径先校验在该文件夹之内，分类文件夹是链接时拒绝；移动不覆盖任何已有文件；不永久删除，只送回收站；所有动作都要用户确认。
- 隐藏的开发者模式（设置页连续点击版本号 5 次）会显示一个实验性的任务栏单词条（把窗口嵌进任务栏）。默认关闭，Store 版是否保留见第 6 节第 7 条。

仍需知道的局限（已记入遗留项，非阻塞）：帮手信任应用在 `ProgramData` 内选择的子目录（受白名单约束）；移动失败时会留下空的时间戳目录；同一用户下的其他进程理论上也能连管道，所以白名单就是安全边界，不是管道。

## 3. 打包与账号清单（阻塞项）

| 项 | 现状 | 要做 |
|---|---|---|
| 包身份 | `Name="TidyBox.Dev"`、`Publisher="CN=TidyBoxDev"`、`PublisherDisplayName="TidyBox Dev"` 是占位 | 在 Partner Center 预留应用名后，用它给出的 `Identity Name / Publisher / PublisherDisplayName` 替换（Store 提交包由 Store 重新签名，本地不需要证书） |
| 运行时 | 框架依赖：.NET 与 Windows App SDK 依赖用户机器上已有运行时 | 用 Store 提交所需的发布配置（自包含）做一次完整打包，装到**没有 SDK/运行时的干净 Win11** 上测试。具体属性（如 `WindowsAppSDKSelfContained`、`SelfContained`）和包体大小我没验证，请以一次真实打包结果为准 ［未验证］ |
| 帮手大小 | 帮手是第二个 exe，自包含会带第二份 .NET 运行时 | 看打出的包大小；必要时让两个 exe 共享同一份运行时目录 |
| 架构 | 本机开发用 x86 | 提交 x64 + ARM64（可打成 bundle）。帮手已处理"32 位进程看到 `Program Files (x86)`"的问题，64 位版本也需复测提权清理 |
| 最低系统 | `MinVersion="10.0.22000.0"`（Win11） | 保持；描述里写明"仅支持 Windows 11" |
| 图标/Logo | 已换成原创的饼图（磁贴、任务栏、Store Logo、启动画面、窗口左上角、程序文件图标、右键菜单图标，源文件 `Assets\TidyBox.ico` 和 `Assets\*.png`） | 脚手架原来那个图形太像别家的商标，已不再使用。上架前如有正式设计，替换这些文件即可 |
| 应用内占位图标 | 列表里找不到应用图标时显示 Fluent 的 `store-microsoft-20-filled`（MIT） | 形状接近 Microsoft Store 的购物袋，有商标顾虑（政策 11.2 对第三方名称和 Logo 有要求）。上架前建议换成一个通用的、非品牌的图形 |
| 第三方声明 | `THIRD-PARTY-NOTICES.md` 已含 Fluent UI System Icons (MIT) | 提交前核对依赖里其余许可证（WindowsAppSDK 等）是否都已列出 |
| 隐私政策 | 默认不联网、不收集数据。**设置页的“发送反馈”上线后**（`FeedbackConfig.Endpoint` 非空），只在用户点击“发送”时联网，发送：用户写的内容、可选联系方式、TidyBox 版本号，以及（用户选“错误”并勾选时）脱敏后的运行日志；服务器只存 IP 的哈希。不收集系统版本、设备标识、使用统计 | 在 Partner Center 的隐私声明表单里按实际选项如实填写（反馈上线前仍可按“不联网”填，上线后必须改）；如果表单要求隐私政策链接，把 `docs/privacy-policy.md` 发布成一个静态页面（GitHub Pages、Cloudflare Pages 都行）。该页内容必须与实现保持一致 |
| 年龄分级 | — | 填问卷即可，无敏感内容 |
| WACK | 未跑 | 提交前用 Windows App Certification Kit 对最终包跑一遍 |
| Pro 解锁 | 尚未实现内购；设置页和各模块里没有任何付费功能 | **V1 实际上只能是免费**：描述里不要写 Pro 或一次性付费（4.3 已改）。以后加付费功能时，再实现 Store 内购并在认证备注里说明如何测试 |

## 4. Store 描述（简体中文，已按政策收敛措辞）

### 4.1 简介（一句话）
TidyBox 是一个 Windows 11 维护工具箱：查看磁盘空间占用、整理文件夹里的文件、更干净地卸载程序、管理右键菜单。

### 4.2 功能列表（只写已实现的）
- **磁盘分析**：按文件夹显示占用，找出大文件。
- **文件整理**：扫描一个文件夹（默认是下载文件夹，也可以换成别的），按文件类型给出整理建议；你勾选并确认后，才会把文件移进该文件夹里的分类文件夹，之后可以按批次撤销。另外会列出超过一定天数（默认 90 天，可在设置里改）没动过的文件，你可以选择移到回收站，或标记"保留，不再提醒"。不会自动整理，不会永久删除文件；移到回收站不会立刻释放空间，要清空回收站才会。
- **右键菜单管理**：列出资源管理器右键菜单里的各个项目，可以停用或重新启用；系统核心项目会被锁定，不能改。另有"恢复完整（经典）右键菜单"开关，由你手动打开或关闭（政策 10.2.8 要求改变用户的 Windows 体验必须取得用户同意，这个开关是用户主动操作，界面上已写明"卸载本应用不会自动恢复"）。
- **在右键菜单中添加"分析此文件夹"**：可选开关，由你主动开启。开启后，右键点一个文件夹，或在文件夹空白处右键，就能直接用磁盘分析打开它；如果 TidyBox 已经开着，就用这个窗口，不会再开一个。
- **卸载工具**：列出已安装的桌面程序、商店应用和 Steam 游戏，运行程序自带的卸载程序；卸载后检查可能的残留，由你勾选后再处理。
  - 残留中的文件夹会移到回收站或隔离区，注册表项会先备份为 `.reg` 文件再删除，可以找回。
  - 清理 `Program Files` 等系统目录的残留时，会弹出 Windows 的管理员确认（UAC），由你决定是否继续。

> 上架前请按实际完成的功能增删这一段，不要写还没做的。

### 4.3 必须写明的事项
- "仅支持 Windows 11。"
- "清理残留和修改右键菜单会改动系统，所有改动都是你点了才发生，并且可以撤销：残留进回收站/隔离区，注册表先备份；右键菜单项可重新启用。"
- "'分析此文件夹'的右键菜单项写在你的用户注册表里，卸载 TidyBox 不会自动删除；要移除请先在'右键菜单'页关掉这个开关。"
- "卸载 TidyBox 本身不会自动删除：隔离区（`C:\ProgramData\TidyBox\Quarantine`）、注册表备份（文档文件夹下 `TidyBox\RegistryBackups`），以及你在右键菜单里做过的停用设置。"（政策 10.2.7 要求清楚告知如何干净卸载。**建议在应用里加一个"恢复所有已停用的右键菜单项"入口；我在界面代码里没找到这样的入口，只有单项重新启用和经典菜单开关**）
- "文件整理会移动你选定的文件夹里的文件：每一次移动都要你确认；移动之后可以在应用里撤销；移到回收站的文件要在回收站里还原；TidyBox 不会永久删除文件。"
- "文件整理在你的电脑上保存整理历史和'已保留'列表（`%LOCALAPPDATA%\TidyBox\Downloads`），卸载 TidyBox 不会自动删除它们。" ［未验证：Store 签名的包里这个目录是否被虚拟化到包的 LocalCache 下，要在真实提交的包上看一次］
- 价格：免费。（目前没有任何付费功能，不要写 Pro 或"一次性付费"。）

### 4.4 不要写
- "加速""优化性能""修复系统问题"之类的承诺，以及"发现 N 个问题"式的吓人提示。
- 任何微软、Microsoft Store、Windows 的商标当作应用自身的 Logo 或名称的一部分。

## 5. 认证备注（Notes for certification，英文，粘贴用）

```
TidyBox is a packaged WinUI 3 desktop app (full trust, medium integrity). It runs as the normal user and never requests administrator rights at launch. No account or login is needed. The only network use is the optional "Send feedback" in Settings, and only at the moment the user presses "Send" (it sends what the user typed, an optional contact, the app version and, if chosen, a redacted log); nothing is sent automatically.

How to test:
1) Disk analyzer: open "Disk analyzer", pick a folder, run a scan.
2) Context menu: open "Context menu"; entries can be disabled and re-enabled ("restore all" re-enables everything the user disabled). Core Explorer entries are locked. An optional switch adds "Analyze this folder (TidyBox)" to the Explorer right-click menu of folders and of the empty space inside a folder; it is off by default and turning it off removes it. It starts the app through its execution alias TidyBox.exe (no capability needed).
3) File tidy ("文件整理"): it looks at the Downloads folder by default; "更改文件夹…" picks another folder (drive roots and system folders are refused). Nothing is moved when the page opens. Tick items and press "执行所选操作", confirm the summary, and the files are moved into category sub-folders of that folder; the history list below has an undo button per batch. Files not touched for more than 90 days (changeable in Settings) are listed separately and can be sent to the Recycle Bin or marked "keep"; no file is ever deleted permanently, and no administrator rights are used.
4) Uninstaller: open "Uninstaller". Pick any installed desktop program and run its own uninstaller; afterwards the app lists possible leftovers, nothing is removed until the user ticks items and confirms. Folders go to the Recycle Bin (or to C:\ProgramData\TidyBox\Quarantine when administrator rights are needed); registry keys are exported to a .reg backup first.

Administrator rights (UAC): only when the user confirms removal of leftovers under Program Files/ProgramData or HKLM\SOFTWARE, the app starts its bundled TidyBox.ElevatedHelper.exe through the standard Windows UAC prompt. If the user declines, that one action fails and the app continues. The helper accepts only allowlisted locations, moves folders instead of deleting them, and exits after 5 idle minutes. If a program's uninstaller can only be run by administrators, the app asks first and then starts it through an elevated cmd.exe (UAC shows cmd.exe).

Restricted capabilities: runFullTrust only (packaged desktop app). The context-menu changes are written to the user's registry (HKCU\Software\Classes and ...\Shell Extensions\Blocked) by starting reg.exe; no virtualization exclusion is declared.
```

## 6. 提交前的自查

1. （已完成）`unvirtualizedResources` 已去掉。
2. 补"恢复所有已停用的右键菜单项"入口（4.3）。
3. 换掉占位 Logo 和应用内占位图标。
4. 用预留的包身份、自包含配置打 x64 和 ARM64 包，在干净的 Win11 上完整走一遍：安装、三个模块、提权清理、卸载应用。
5. 跑 WACK。
6. 先用 Partner Center 提交一个**不公开**的版本，让认证流程给出真实反馈，再决定是否需要调整 capability。
7. 决定 Store 版是否保留"开发者模式 + 背单词"。它默认关闭、菜单里不显示，但开启后会把窗口嵌进任务栏。若保留，认证备注里写明；若不想冒审核风险，就在发布配置里去掉。（我没有核实 Store 政策对"隐藏功能"的具体要求，请以政策原文为准。）
8. 把隐私声明表单和 `docs/privacy-policy.md` 发布成网页，内容必须一致（反馈会联网；文件整理的数据只在本机）。
9. 本地打包与 WACK（2026-10-29 做过一次，包身份仍是占位的 `TidyBox.Dev`）：
   - 做法：`dotnet build src/TidyBox.App -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true`（`dotnet build` 不会生成 `.msix`）。**不能**直接把输出文件夹整个打包：图标等文件是由构建生成的包清单（输出目录里的 `TidyBox.App.build.appxrecipe`）从别处映射进来的，漏掉就会缺 `Assets`。要按这份清单生成映射文件，再 `makeappx pack /f 映射文件 /p 包.msix`，并排除 `.pdb` 和清单文件本身。包约 88.5 MB，539 个文件；提权帮手也是自包含的，和主程序共用运行时。
   - 签名：本地检测用一次性自签名证书（`CN=TidyBoxDev`），签完立即从证书库移除、删除私钥文件；Store 提交时由微软重新签名。
   - WACK 必须在管理员终端里运行：`appcert.exe reset`，再 `appcert.exe test -appxpackagepath <包> -reportoutputpath <报告.xml>`。
   - 结果：总体 **PASS**，24 项里 22 项通过，2 项失败且都是标注为"可选"的测试：
     - "一般元数据正确性"（14 条）：全部是 Windows App SDK 自带的 `Microsoft.UI.Xaml.winmd`、`Microsoft.UI.winmd`、`Microsoft.Windows.Internal.AI.Generative.winmd` 的 GUID 重复等，与我们的代码无关。
     - "已阻止的可执行文件"（52 条）：运行时文件，以及我们的 `TidyBox.App/Core/ElevatedHelper/ContextMenu/DiskAnalyzer/Uninstaller` 引用了 `Process.Start`、`ShellExecute`，字符串里有 `reg.exe`、`cmd.exe`。这与第 2 节和第 5 节已写明的行为一致（卸载程序、UAC 提权帮手、右键菜单经 `reg.exe` 写入）。文件整理模块没有出现在这一项里（它不启动任何进程）。
   - 第一次打包因漏了 `Assets` 而让"应用资源"失败，修正打包方式后已通过；"品牌""应用清单"也通过。
   - 这只是自查，不是 Store 的正式认证，以真实提交的认证结果为准。

## 来源

- Microsoft Store Policies 7.20：https://learn.microsoft.com/windows/uwp/publish/store-policies （10.2 Security、10.2.3、10.2.7、10.2.8、10.3 Testable、11.2）
- App capability declarations：https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations （Restricted capability approval process；`runFullTrust`、`unvirtualizedResources`、`packageManagement`、`allowElevation` 条目）
- Flexible virtualization：https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization
