# PC 维护工具箱 —— 右键菜单管理模块 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现右键菜单管理作为一个 `IToolModule`：列出文件/文件夹/文件夹空白处/驱动器右键菜单里由注册表登记的条目，对每一条做**可恢复的**启用/禁用，并提供"一键恢复经典右键菜单"开关。

**Architecture:** 新增 `TidyBox.Modules.ContextMenu` 类库（纯逻辑，依赖 `TidyBox.Core`）。注册表访问集中在一个可注入根键的 `ContextMenuRegistryRoots` 后面，测试在 `HKCU` 下的沙箱键里跑，不碰真实系统。禁用**只加值、不删键**：处理程序（`shellex\ContextMenuHandlers`）用每用户的 `Shell Extensions\Blocked` 列表，静态动词（`shell\<verb>`）用 `LegacyDisable` 值，全部写 `HKCU`，所以日常使用不需要管理员权限，也不依赖提权帮手。界面放进 `TidyBox.App`，沿用磁盘分析模块的做法（模块纯逻辑，页面只做绑定）。

**Tech Stack:** .NET 8（`net8.0-windows`）, xUnit, `Microsoft.Win32.Registry`, `SHLoadIndirectString`（P/Invoke）, WinUI3。

**Spec:** `docs/superpowers/specs/2026-10-24-pc-maintenance-toolbox-design.md`（"右键菜单管理模块"一节）

## Global Constraints

- .NET 8，只支持 Windows 11，App 以普通用户权限运行；本模块**不使用**管理员权限、不使用 `ElevationClient`。
- 禁用必须可恢复：**不删除任何处理程序键、动词键或 CLSID 键**，只增删我们自己写的值（`Blocked` 值、`LegacyDisable` 值）。直接删除属于设计文档的反目标。
- 不把 GPL 项目（ContextMenuManager、ContextMenuMgr）的代码编译进来，只参考功能思路，实现自己写。
- 用户可见文案用简体中文；CLSID→友好名称的查找逻辑不能硬编码只认中文系统（中文名称表只是"优先显示"的一层，查不到时回落到系统/DLL 提供的名称）。
- 免费范围：查看、启用/禁用、"恢复经典右键菜单"开关。**不在本计划内**：自定义添加菜单项、批量一键清理（付费功能，等付费解锁机制定了再做）。
- "一键恢复经典菜单"写 `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`（默认值为空字符串），生效需要重启资源管理器。

## 这台开发机上看到的真实数据（决定了下面的边界用例）

- 处理程序键名可以带**前导空格**：`" FileSyncEx"`（OneDrive 用它控制排序）。
- 有的键**名字本身就是 CLSID**，默认值是描述文字：`{90AA3A4E-…}` → `Taskband Pin`；有的默认值才是 CLSID：`WinRAR` → `{B41DB860-…}`。
- 有的 `HKCR\CLSID\{…}` 默认值为空（`EPP`，Windows Defender），只能从 `InprocServer32` 指向的 DLL 读 `FileDescription`；有的 CLSID 根本没注册（`WinRAR32`）。
- 同一个位置里的处理程序分布在两个根：`Directory\Background` 的 4 个在 `HKLM`，`PowerRenameExt` 在 `HKCU`（PowerToys 每用户安装）。
- 静态动词里 Windows 自己就在用 `LegacyDisable`：`Directory\shell\find`、`Drive\shell\find`。
- 动词显示名可能是 `MUIVerb`，也可能是 `@shell32.dll,-8506` 这种间接字符串。

## Review Focus

- 键名带前导/尾随空格，或键名本身就是 CLSID：列表里要显示正确、启用/禁用后能找回同一条（Task 3、Task 5）。
- 同一个 CLSID 在 `HKLM` 和 `HKCU` 都登记，或在多个位置重复出现：禁用要一次生效，且每个位置的条目状态一致（Task 3、Task 5）。
- 陈旧登记：CLSID 没注册、DLL 文件不存在：列表要能显示（标注"无法识别/文件缺失"）且不崩溃，仍可禁用（Task 3、Task 4）。
- 某个键在读取/写入时被拒绝访问或刚被别的程序删掉：只让这一条失败并给出原因，不能让整个列表加载失败（Task 3、Task 5）。
- 对 `HKLM` 里已经 `LegacyDisable`（系统默认禁用）的动词：显示为"系统已禁用"，**不允许**在这里启用（我们无法从 `HKCU` 撤销 `HKLM` 的值），而不是假装成功（Task 3、Task 5）。

---

## File Structure

```
src/TidyBox.Modules.ContextMenu/
  TidyBox.Modules.ContextMenu.csproj
  Model/MenuLocation.cs            枚举 + 位置到注册表路径、中文名的映射
  Model/MenuEntry.cs               MenuEntry / MenuEntryKind / MenuEntryState
  Registry/ContextMenuRegistryRoots.cs   可注入的 HKLM/HKCU 根；固定的相对路径常量
  Catalog/ContextMenuCatalog.cs    读取并合并两个根，产出 MenuEntry 列表
  Naming/IMenuNameResolver.cs      接口 + MenuNameInfo
  Naming/MenuNameResolver.cs       中文名称表 → CLSID 名 → DLL 描述 → 键名
  Naming/IndirectStringLoader.cs   SHLoadIndirectString 封装（接口 + 真实实现）
  Toggle/MenuToggler.cs            启用/禁用（只加值不删键）
  Classic/ClassicMenuSwitch.cs     经典菜单开关
  Classic/IExplorerRestarter.cs    接口 + 真实实现（重启资源管理器）
  ContextMenuModule.cs             IToolModule + 对界面暴露的工作流
tests/TidyBox.Modules.ContextMenu.Tests/
  Support/RegistrySandbox.cs       在 HKCU\Software\TidyBoxTests\<guid> 下造假的 HKLM/HKCU 根，Dispose 时清理
  ... 与上面一一对应的测试文件
src/TidyBox.App/Views/ContextMenuView.xaml(.cs)   界面
docs/superpowers/specs/2026-10-25-context-menu-mechanism-spike.md   Task 1 的结论记录
```

---

## Task 1: 机制验证（需要人工，先于所有实现）

这个模块的核心赌注是"只写 `HKCU` 就能让 `HKLM` 登记的条目从菜单里消失"。`Blocked` 在 `HKCU` 下是否被资源管理器认、`HKCU` 里一个只含 `LegacyDisable` 的动词键会不会和 `HKLM` 的同名键合并，文档没有说清，不能靠猜。**先验证，再写代码。**

**Files:**
- Create: `docs/superpowers/specs/2026-10-25-context-menu-mechanism-spike.md`

**Interfaces:** 无（产出是一份结论文档，后续任务按它选择实现路径）。

- [ ] **Step 1: 验证 A——`HKCU` 的 `Blocked` 能否隐藏 `HKLM` 登记的处理程序。** 用一个无害且明显的条目：`WinRAR` 处理程序（CLSID `{B41DB860-64E4-11D2-9906-E49FADC173CA}`，在"显示更多选项"里有 WinRAR 子菜单）。在文件上右键 → 显示更多选项，确认能看到 WinRAR；然后执行
  `reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked" /v "{B41DB860-64E4-11D2-9906-E49FADC173CA}" /t REG_SZ /d "" /f`，记录：不重启资源管理器时是否生效、重启资源管理器后是否生效。

- [ ] **Step 2: 验证 B——`HKCU` 里只含 `LegacyDisable` 的动词键能否隐藏 `HKLM` 的动词。** 用 `Directory\shell\git_shell`（"Git Bash Here"，来自 `HKLM`）：在文件夹上右键 → 显示更多选项确认能看到；然后创建 `HKCU\Software\Classes\Directory\shell\git_shell` 并写入值 `LegacyDisable`（REG_SZ，空），记录同样两项（是否需重启资源管理器）。

- [ ] **Step 3: 验证 C——恢复。** 删除上面两个值（B 还要删掉空的 `git_shell` 键），确认条目回来了。

- [ ] **Step 4: 验证 D——经典菜单开关。** 创建 `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`，默认值设为空字符串，重启资源管理器，确认右键直接出现完整菜单；删除整个 `{86ca1aa0-…}` 键后恢复。

- [ ] **Step 5: 把 A–D 的结果写进 `2026-10-25-context-menu-mechanism-spike.md`**（Windows build 号、每项"是否生效 / 是否需要重启资源管理器"），并按下面的决策表给出结论，提交。

  | 结果 | 影响 |
  |---|---|
  | A、B 都生效 | 按本计划往下做（只写 `HKCU`，不需要管理员权限）。 |
  | A 不生效、B 生效 | 处理程序改用"重命名键 + 备份"，需要提权（见文末"回退方案"）；动词部分不变。 |
  | B 不生效 | 动词也需要改 `HKLM`，同样走提权回退方案。 |
  | 必须重启资源管理器才生效 | 界面上每次切换后提示"需要重启资源管理器"，并提供 Task 6 的重启按钮。 |

---

## Task 2: 项目骨架、数据模型、注册表沙箱

**Files:**
- Create: `src/TidyBox.Modules.ContextMenu/TidyBox.Modules.ContextMenu.csproj`（`net8.0-windows`，引用 `TidyBox.Core`，`InternalsVisibleTo` 测试项目）
- Create: `src/TidyBox.Modules.ContextMenu/Model/MenuLocation.cs`、`Model/MenuEntry.cs`、`Registry/ContextMenuRegistryRoots.cs`
- Create: `tests/TidyBox.Modules.ContextMenu.Tests/TidyBox.Modules.ContextMenu.Tests.csproj`、`Support/RegistrySandbox.cs`、`Registry/ContextMenuRegistryRootsTests.cs`
- Modify: `TidyBox.sln`

**Interfaces:**
- Produces:
  - `enum MenuLocation { AllFiles, AllFileSystemObjects, Directory, DirectoryBackground, Folder, Drive }`，扩展 `string RegistryKeyName(this MenuLocation)`（依次为 `*`、`AllFilesystemObjects`、`Directory`、`Directory\Background`、`Folder`、`Drive`）和 `string DisplayName(this MenuLocation)`（`所有文件`、`所有文件和文件夹`、`文件夹`、`文件夹空白处`、`文件夹和库`、`驱动器`）。
  - `enum MenuEntryKind { Handler, Verb }`；`enum MenuEntryState { Enabled, Disabled, DisabledBySystem }`（`Disabled` = 我们或用户在 `HKCU` 禁用，可恢复；`DisabledBySystem` = 只在 `HKLM` 层被禁用，这里不能改）。
  - `record MenuEntry(string Id, MenuLocation Location, MenuEntryKind Kind, string KeyName, string DisplayName, string? Publisher, string? ModulePath, string? Clsid, bool InMachineHive, bool InUserHive, MenuEntryState State, string? Problem)`；`Id` 恒为 `$"{Location}|{Kind}|{KeyName}"`，`KeyName` **原样保留**（含前导空格）。`Problem` 非空表示这条无法识别或有问题，界面据此只读显示。
  - `sealed class ContextMenuRegistryRoots(RegistryKey machine, RegistryKey user)`：`Machine`/`User` 属性；常量 `ClassesPath = @"Software\Classes"`、`BlockedPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked"`；静态 `Real()` 返回 `(Registry.LocalMachine, Registry.CurrentUser)`。
  - 测试辅助 `RegistrySandbox : IDisposable`：在 `HKCU\Software\TidyBoxTests\<guid>` 下建 `machine` 和 `user` 两个子键，`Roots` 属性返回对应的 `ContextMenuRegistryRoots`，提供 `AddHandler(hive, location, keyName, defaultValue)`、`AddVerb(hive, location, verbName, params (string Name, object Value)[] values)`、`RegisterClsid(hive, clsid, name, dllPath)`、`Block(hive, clsid)` 辅助方法；`Dispose` 删除整个沙箱。

- [ ] **Step 1: 建两个项目并加入解决方案**（`dotnet new` 之后把 TFM 改成 `net8.0-windows`，测试项目照 `TidyBox.Core.Tests` 的 csproj 写）。
- [ ] **Step 2: 写失败的测试** `ContextMenuRegistryRootsTests`：`MenuLocation` 的 `RegistryKeyName`/`DisplayName` 对照上表；`RegistrySandbox` 创建后两个根互相独立，`Dispose` 后 `HKCU\Software\TidyBoxTests\<guid>` 不再存在；`Real()` 的两个根分别是 `Registry.LocalMachine` 和 `Registry.CurrentUser`。
- [ ] **Step 3: 运行，确认失败**：`dotnet test tests/TidyBox.Modules.ContextMenu.Tests`
- [ ] **Step 4: 实现上述类型。**
- [ ] **Step 5: 运行，确认通过，提交** `feat: scaffold context menu module with registry roots and sandbox`

---

## Task 3: 目录扫描 `ContextMenuCatalog`

**Files:**
- Create: `src/TidyBox.Modules.ContextMenu/Catalog/ContextMenuCatalog.cs`
- Create: `src/TidyBox.Modules.ContextMenu/Naming/IMenuNameResolver.cs`（本任务先只建接口和 `MenuNameInfo` 记录，签名见 Task 4；Task 4 再提供真实实现）
- Test: `tests/TidyBox.Modules.ContextMenu.Tests/Catalog/ContextMenuCatalogTests.cs`

**Interfaces:**
- Consumes: `ContextMenuRegistryRoots`、`MenuEntry`（Task 2）；`IMenuNameResolver`（本任务创建，测试里用假实现，返回 `DisplayName = keyName`）。
- Produces: `sealed class ContextMenuCatalog(ContextMenuRegistryRoots roots, IMenuNameResolver names)`，`IReadOnlyList<MenuEntry> LoadAll()`：遍历每个 `MenuLocation` 的 `<Classes>\<Location>\shellex\ContextMenuHandlers`（处理程序）和 `<Classes>\<Location>\shell`（动词），两个根都读，按 `Id` 合并成一条（`InMachineHive`/`InUserHive` 分别标记）。

规则：
- **处理程序的 CLSID**：键的默认值若形如 `{…}` 就用它，否则键名形如 `{…}` 就用键名，否则 `Clsid = null`、`Problem = "无法识别处理程序"`。
- **处理程序的状态**：CLSID（不区分大小写、统一带花括号比较）出现在 `User` 的 `Blocked` 值名里 → `Disabled`；只出现在 `Machine` 的 `Blocked` → `DisabledBySystem`；否则 `Enabled`。
- **动词的状态**：`User` 层该动词键有 `LegacyDisable` 值 → `Disabled`；仅 `Machine` 层有 → `DisabledBySystem`；否则 `Enabled`。
- `ModulePath` 来自 `CLSID\{…}\InprocServer32` 默认值（展开环境变量）；CLSID 没注册 → `Problem = "未找到该处理程序的注册信息"`；DLL 不存在 → `Problem = "文件缺失：<路径>"`。
- 读取单个键失败（`UnauthorizedAccessException`/`IOException`/`SecurityException`）→ 跳过这一条，不影响其余。
- 结果按 位置、种类（处理程序在前）、`DisplayName` 排序。

- [ ] **Step 1: 写失败的测试**（用 `RegistrySandbox`）：
  - `LoadAll_HandlerWithClsidAsDefaultValue_ReturnsEntryWithThatClsid`
  - `LoadAll_HandlerWhoseKeyNameIsTheClsid_UsesTheKeyName`
  - `LoadAll_KeyNameWithLeadingSpace_KeepsItInKeyNameAndId`（`" FileSyncEx"` → `Id == "AllFiles|Handler| FileSyncEx"`）
  - `LoadAll_SameHandlerInMachineAndUserHive_IsOneEntryMarkedInBoth`
  - `LoadAll_HandlerClsidBlockedInUserList_IsDisabled`、`..._BlockedOnlyInMachineList_IsDisabledBySystem`
  - `LoadAll_VerbWithLegacyDisableInUserHive_IsDisabled`、`..._OnlyInMachineHive_IsDisabledBySystem`
  - `LoadAll_UnregisteredClsid_IsListedWithAProblem`、`..._DllMissing_IsListedWithAProblem`、`..._NoClsidAnywhere_IsListedWithAProblem`
  - `LoadAll_OneKeyUnreadable_OtherEntriesStillLoad`（用假的 `IMenuNameResolver` 在某个键上抛 `UnauthorizedAccessException`，或在沙箱里把该键的 ACL 设成拒绝读取，二选一，由实现者按稳定性决定）
  - `LoadAll_IsOrderedByLocationKindThenName`
- [ ] **Step 2: 运行，确认失败。**
- [ ] **Step 3: 实现 `ContextMenuCatalog`。**
- [ ] **Step 4: 运行，确认通过，提交** `feat: add context menu catalog reading handlers and verbs from both hives`

---

## Task 4: 名称解析 `MenuNameResolver`

**Files:**
- Create: `src/TidyBox.Modules.ContextMenu/Naming/MenuNameResolver.cs`、`IndirectStringLoader.cs`
- Test: `tests/TidyBox.Modules.ContextMenu.Tests/Naming/MenuNameResolverTests.cs`

**Interfaces:**
- Consumes: `IMenuNameResolver`、`MenuNameInfo`（Task 3 已创建；签名如下，不要改动）。
- Produces:
  - `record MenuNameInfo(string DisplayName, string? Publisher)`
  - `interface IMenuNameResolver { MenuNameInfo Resolve(MenuEntryKind kind, string keyName, string? clsid, string? modulePath, string? registeredName, string? muiVerb); }`
  - `interface IIndirectStringLoader { string? Load(string indirect); }`，真实实现用 `SHLoadIndirectString`，输入不是 `@…` 形式时原样返回。
  - `sealed class MenuNameResolver(IIndirectStringLoader indirect, Func<string, FileVersionInfo?> versionInfo)`：真实使用时 `versionInfo` 为 `FileVersionInfo.GetVersionInfo`，测试里换成假的。
- 解析顺序（取第一个非空）：① 内置中文名称表（按 CLSID 或按动词/键名，不区分大小写）；② 动词的 `MUIVerb`（先过 `IIndirectStringLoader`），或 `registeredName`（`CLSID\{…}` 默认值）；③ `modulePath` 对应 DLL 的 `FileDescription`；④ 去掉首尾空格的 `keyName`。`Publisher` 取 DLL 的 `CompanyName`。

内置名称表只放有把握的几项，其余走后备：`Sharing`→`共享`、`Open With`→`打开方式`、`SendTo`→`发送到`、`CopyAsPathMenu`→`复制文件地址`、`New`→`新建`、`WorkFolders`→`工作文件夹`、`Offline Files`→`脱机文件`，动词 `find`→`搜索`、`cmd`→`在此处打开命令窗口`、`Powershell`→`在此处打开 PowerShell 窗口`。这是第一版，之后可以按用户反馈扩充。

- [ ] **Step 1: 写失败的测试：**
  - `Resolve_KnownClsid_ReturnsChineseName`
  - `Resolve_UnknownClsidWithRegisteredName_UsesRegisteredName`
  - `Resolve_EmptyRegisteredName_FallsBackToDllFileDescription`（`EPP` 的情形）并 `Publisher` 来自 `CompanyName`
  - `Resolve_NothingKnown_FallsBackToTrimmedKeyName`（`" FileSyncEx"` → `"FileSyncEx"`）
  - `Resolve_MuiVerbIndirect_IsExpandedThroughTheLoader`；加载器返回 `null` 时回落到后面的来源
  - `Resolve_ModuleVersionInfoThrows_FallsBackWithoutThrowing`
- [ ] **Step 2: 运行，确认失败。**
- [ ] **Step 3: 实现。** `SHLoadIndirectString` 的 P/Invoke 只放在 `IndirectStringLoader` 里，其余代码不碰原生 API。
- [ ] **Step 4: 运行，确认通过，提交** `feat: resolve friendly names for context menu entries`

---

## Task 5: 启用/禁用 `MenuToggler`

**Files:**
- Create: `src/TidyBox.Modules.ContextMenu/Toggle/MenuToggler.cs`
- Test: `tests/TidyBox.Modules.ContextMenu.Tests/Toggle/MenuTogglerTests.cs`

**Interfaces:**
- Consumes: `ContextMenuRegistryRoots`、`MenuEntry`（Task 2）。
- Produces: `record ToggleResult(bool Success, string? Error, bool NeedsExplorerRestart = false)`（Task 1 结论：处理程序的禁用/启用需要重启资源管理器才生效，动词立即生效，所以处理程序为 `true`、动词为 `false`；经典菜单开关为 `true`）；`sealed class MenuToggler(ContextMenuRegistryRoots roots)`：`ToggleResult SetEnabled(MenuEntry entry, bool enabled)`。

规则（全部只写 `User` 根）：
- **处理程序**：禁用 = 在 `User` 的 `Blocked` 键里写值（值名 = CLSID，规范成带花括号的大写形式；类型 `REG_SZ`，内容为空）；启用 = 删除该值（不区分大小写地找）。`Blocked` 键不存在时禁用要创建它，启用时不存在就当作已经启用。
- **动词**：禁用 = 在 `User\Software\Classes\<Location>\shell\<verb>` 写 `LegacyDisable`（空 `REG_SZ`），键不存在则创建；启用 = 删除该值，之后如果这个键**既无值也无子键**，并且是我们创建的（`Machine` 层也有同名动词，说明不是用户自己建的），就把这个空键删掉；否则保留。
- `entry.State == DisabledBySystem` 或 `entry.Problem != null && Clsid == null` → 直接返回 `Success = false` 和说明，不改注册表。
- 幂等：已经是目标状态时返回 `Success = true`，不重复写。
- 写完后重新读取一次确认状态确实变了，没变则返回 `Success = false`。
- 同一个 CLSID 在多个位置出现时，禁用是按 CLSID 全局生效的（`Blocked` 的性质），启用同理；调用方（Task 7）负责刷新所有位置的条目。
- **绝不删除**处理程序键、`CLSID` 键，或 `Machine` 根下的任何东西。
- 注册表异常（`UnauthorizedAccessException`/`IOException`/`SecurityException`）→ `Success = false`，`Error` 为异常消息，不抛出。
- 所有注册表写入用 .NET `RegistryKey` API，不调用 `reg.exe`（见 spike 结论里的空字符串陷阱）。

- [ ] **Step 1: 写失败的测试：**
  - `SetEnabled_DisableHandler_AddsClsidToUserBlockedList`；`..._Enable_RemovesIt`；规范化（输入小写、无花括号的情形不会产生重复值）
  - `SetEnabled_DisableHandlerWithLeadingSpaceKeyName_BlocksItsClsid`
  - `SetEnabled_DisableVerb_WritesLegacyDisableInUserHive_AndLeavesMachineHiveUntouched`
  - `SetEnabled_EnableVerb_RemovesValueAndDeletesTheEmptyKeyWeCreated`；用户自己建的动词键启用后**保留**
  - `SetEnabled_AlreadyInTargetState_IsANoOpSuccess`
  - `SetEnabled_DisabledBySystemEntry_FailsWithoutWriting`；`..._HandlerWithoutClsid_Fails`
  - `SetEnabled_NeverDeletesAnyHandlerOrClsidKey`：对一份沙箱做"禁用再启用"，比较前后 `Machine` 根下所有键和值完全一致
  - `SetEnabled_RegistryThrows_ReturnsFailureInsteadOfThrowing`（把沙箱的 `User\Software\Classes` 的 ACL 设为拒绝写入，或用可注入的失败点，由实现者按稳定性决定）
- [ ] **Step 2: 运行，确认失败。**
- [ ] **Step 3: 实现 `MenuToggler`。**
- [ ] **Step 4: 运行，确认通过，提交** `feat: add reversible enable/disable for context menu entries`

---

## Task 6: 经典菜单开关与重启资源管理器

**Files:**
- Create: `src/TidyBox.Modules.ContextMenu/Classic/ClassicMenuSwitch.cs`、`IExplorerRestarter.cs`
- Test: `tests/TidyBox.Modules.ContextMenu.Tests/Classic/ClassicMenuSwitchTests.cs`

**Interfaces:**
- Consumes: `ContextMenuRegistryRoots`（Task 2）、`ToggleResult`（Task 5）。
- Produces:
  - `sealed class ClassicMenuSwitch(ContextMenuRegistryRoots roots)`：`bool IsEnabled { get; }`、`ToggleResult SetEnabled(bool enabled)`。开启 = 创建 `User\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` 并把默认值设为空字符串；关闭 = 删除整个 `{86ca1aa0-…}` 键（只删这一个键，不碰其它 CLSID）。`IsEnabled` = `InprocServer32` 存在且默认值为空字符串。
  - `interface IExplorerRestarter { Task RestartAsync(CancellationToken ct = default); }`；真实实现 `ExplorerRestarter`：结束所有 `explorer.exe` 进程，等 3 秒，若没有 `explorer.exe` 在运行就启动一个。

- [ ] **Step 1: 写失败的测试：**
  - `SetEnabled_True_CreatesInprocServer32WithEmptyDefault_AndIsEnabledReportsTrue`
  - `SetEnabled_False_RemovesOnlyTheSwitchKey`（沙箱里另放一个别的 CLSID 键，关闭后它还在）
  - `IsEnabled_KeyExistsButDefaultIsNotEmpty_IsFalse`
  - `SetEnabled_True_WhenAlreadyEnabled_IsANoOpSuccess`
- [ ] **Step 2: 运行，确认失败。**
- [ ] **Step 3: 实现。** `ExplorerRestarter` 不写自动化测试（会结束开发机上的资源管理器），在 Task 8 手动验证。
- [ ] **Step 4: 运行，确认通过，提交** `feat: add classic context menu switch`

---

## Task 7: `ContextMenuModule`

**Files:**
- Create: `src/TidyBox.Modules.ContextMenu/ContextMenuModule.cs`
- Test: `tests/TidyBox.Modules.ContextMenu.Tests/ContextMenuModuleTests.cs`

**Interfaces:**
- Consumes: `ContextMenuCatalog`、`MenuToggler`、`ClassicMenuSwitch`（Task 3、5、6），`MenuNameResolver`（Task 4），`IExplorerRestarter`。
- Produces: `sealed class ContextMenuModule : IToolModule`，构造 `ContextMenuModule(ContextMenuRegistryRoots? roots = null, IMenuNameResolver? names = null, IExplorerRestarter? restarter = null)`（全 `null` 时用真实实现）：
  - `ModuleId = "context-menu"`，`DisplayName = "右键菜单管理"`。
  - `Task InitializeAsync()`：确认能打开两个根的 `Software\Classes`，失败时抛 `InvalidOperationException` 带清晰信息（让外壳把模块显示成"加载失败"）。
  - `Task<IReadOnlyList<MenuEntry>> LoadEntriesAsync()`（在线程池上跑 `ContextMenuCatalog.LoadAll`）。
  - `ToggleResult SetEntryEnabled(MenuEntry entry, bool enabled)`。
  - `bool IsClassicMenuEnabled { get; }`、`ToggleResult SetClassicMenu(bool enabled)`、`Task RestartExplorerAsync()`。

- [ ] **Step 1: 写失败的测试：**
  - `ModuleId_And_DisplayName_MatchConvention`
  - `InitializeAsync_RootsReadable_Succeeds`；`..._RootCannotBeOpened_ThrowsInvalidOperationWithMessage`
  - `LoadEntriesAsync_ThenDisable_ThenReload_ShowsEntryAsDisabled`（端到端：沙箱里放一个处理程序和一个动词，各自禁用后重新加载，状态变为 `Disabled`，再启用后回到 `Enabled`）
  - `SetClassicMenu_TogglesTheSwitchAndIsClassicMenuEnabledFollows`
  - `RestartExplorerAsync_DelegatesToTheRestarter`（假的 `IExplorerRestarter`）
- [ ] **Step 2: 运行，确认失败。**
- [ ] **Step 3: 实现。**
- [ ] **Step 4: 运行全部测试，确认通过，提交** `feat: add ContextMenuModule`

---

## Task 8: WinUI3 视图并接入外壳

**Files:**
- Create: `src/TidyBox.App/Views/ContextMenuView.xaml`、`ContextMenuView.xaml.cs`
- Modify: `src/TidyBox.App/TidyBox.App.csproj`（引用新项目）、`src/TidyBox.App/App.xaml.cs`（注册 `ContextMenuModule`）、`src/TidyBox.App/MainWindow.xaml.cs`（`Pages["context-menu"] = typeof(ContextMenuView)`）

**Interfaces:**
- Consumes: `ContextMenuModule`（Task 7）。页面通过 `Frame.Navigate(typeof(ContextMenuView), module)` 拿到模块实例，约定同 `DiskAnalyzerView`。

界面要求（视图只做绑定和事件转发，逻辑都在模块里）：
- 顶部一张"恢复完整（经典）右键菜单"卡片：开关 + 说明文字"开启后右键直接显示完整菜单，需要重启资源管理器才生效" + "重启资源管理器"按钮（点击先弹确认框，说明会关闭打开的文件夹窗口）。
- 搜索框（按显示名称、键名、发布者过滤）。
- 列表按 `MenuLocation.DisplayName()` 分组，每行：开关、显示名称、种类标签（处理程序/动词）、发布者、路径或问题说明。`DisabledBySystem` 与 `Problem != null` 的行开关禁用并给出原因 tooltip。
- 切换开关后调用 `SetEntryEnabled`，失败时把开关还原并在状态栏显示 `Error`；成功后重新加载列表（因为同一 CLSID 可能出现在多个位置）。
- 沿用磁盘分析页的无障碍约定：列表项行对象重写 `ToString`。

- [ ] **Step 1: 引用项目、注册模块、加页面映射，写页面。**
- [ ] **Step 2: `dotnet build src/TidyBox.App -p:Platform=x64`，Expected: 成功，0 警告。**
- [ ] **Step 3: 自动化冒烟：** 用 UI Automation 启动打包后的 App（做法同磁盘分析页的验证：临时替换已安装的开发包并在 `finally` 里还原），确认导航里出现"右键菜单管理"、页面加载出条目、搜索框能过滤；截取应用窗口检查布局。
- [ ] **Step 4: 手动验证（需要人）：** 禁用一个条目后右键，确认菜单里没了，再启用确认回来；切换经典菜单并点"重启资源管理器"，确认右键直接出现完整菜单。结果与 Task 1 的结论对照，不一致就回到 Task 1 修正文档和实现。
- [ ] **Step 5: 提交** `feat: add context menu management view and wire it into the shell`

---

## 回退方案（仅当 Task 1 的结论是 `HKCU` 不够用）

在 Task 5 里为需要改 `HKLM` 的情形增加走提权通道的实现：处理程序改为"重命名键并备份原名到一个我们自己的值里"，动词改为在 `HKLM` 写 `LegacyDisable`。这是第一个真正使用 `ElevationClient`/`ElevationServer` 的模块，**开工前必须先补齐提权基础设施的遗留项**：App 里接入 `ElevationClient`（用 `NewSessionPipeName()`）、`ElevatedHelperProcessLauncher` 的真实路径、client 连接后校验 server 进程路径；helper 里的 `IElevatedOperationHandler` 必须对输入做严格白名单校验（只允许下列固定的相对路径和 CLSID 格式，拒绝任何其它键路径）。回退方案应作为单独的任务补进本计划，而不是现在预先实现。

## 非目标

自定义添加菜单项、批量清理（付费）；Windows 11 新式菜单里由打包应用（`IExplorerCommand`）提供的条目（它们不在上述注册表位置，需要另行研究）；按文件扩展名（`.txt` 等）分类的菜单；`SendTo` 文件夹和"新建"菜单模板管理；付费解锁机制。

## 完成后的状态

用户能在"右键菜单管理"里看到按位置分组的所有注册表登记的菜单条目（带中文名称、发布者、问题提示），一键禁用/恢复，不改任何系统文件、不需要管理员权限，并能一键切换经典右键菜单。
