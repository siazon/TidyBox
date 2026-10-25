# 右键菜单禁用机制验证（Task 1 结论）

日期：2026-10-25　环境：Windows 11 build 26300（26H2），普通用户权限，资源管理器看的是"显示更多选项"里的传统菜单。
验证方式：只写 `HKCU`，由人在真实资源管理器里右键查看；注册表合并视图由我直接读取确认。验证完所有改动已撤销。

## 结果

| | 做了什么 | 观察到的 |
|---|---|---|
| **A 处理程序** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` 下写值 `{B41DB860-64E4-11D2-9906-E49FADC173CA}`（WinRAR，登记在 `HKLM`） | **不重启时 WinRAR 菜单还在；重启资源管理器后消失。** 删除该值并重启后恢复。 |
| **B 静态动词** | 创建 `HKCU\Software\Classes\Directory\shell\git_shell`，只写一个值 `LegacyDisable`（空字符串）；`git_shell` 本体在 `HKLM` | **立即生效**，`Open Git Bash here` 消失；相邻的、没动过的 `Open Git GUI here` 仍在（作用精确）。删除这个键后**立即恢复**（未重启就回来了）。 |
| **D 经典菜单** | `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`，默认值为空字符串 | 重启资源管理器后，直接右键就是完整经典菜单。 |
| 合并视图 | 读取 `HKEY_CLASSES_ROOT\Directory\shell\git_shell` | 写入 `HKCU` 后值为 `[默认, Icon, LegacyDisable]`、子键为 `[command]`：**按值合并，`HKLM` 的命令定义没有被顶掉**。 |

## 结论（按 plan 的决策表）

- **A、B 都生效 → 按计划往下做**：只写 `HKCU`，不需要管理员权限，不需要提权帮手。回退方案（重命名键 + 提权）这一期**不需要**。
- **附加条件：处理程序（A）和经典菜单（D）需要重启资源管理器才生效；动词（B）立即生效。** 实现要求：
  - `ToggleResult` 带 `NeedsExplorerRestart`：禁用/启用**处理程序**为 `true`，**动词**为 `false`，经典菜单开关为 `true`。
  - 界面在任何一次 `NeedsExplorerRestart == true` 的切换后显示"需要重启资源管理器才生效"并给出重启按钮（Task 6 的 `IExplorerRestarter`）。
- `Blocked` 是**按 CLSID** 起作用的，所以键名本身就是 CLSID 的条目（如 `{90AA3A4E-…}` Taskband Pin）同样能禁用，不受"默认值不是 CLSID"的影响；同一个 CLSID 在多个位置登记时，一次禁用对所有位置生效。

## 未验证（留给 Task 8 手动验证）

- 对**同一个处理程序**，撤销 `Blocked` 后"不重启是否立即恢复"没有单独观察（验证时是重启后才看的），实现上按"需要重启"处理。
- 动词 `LegacyDisable` 覆盖 `HKCU`-only 动词（用户自己建的）的行为只在 `HKLM` 登记的动词上验证过。
- Windows 11 新式菜单里由打包应用提供的条目不在这些注册表位置，本期不覆盖（见 plan 非目标）。

## 实现注意（踩到的坑）

在 PowerShell 5.1 里用 `reg add ... /d ""` 写空字符串时，空参数会被吞掉，下一个参数（`/f`）被当成数据写入，经典菜单开关因此被写成了 `/f`。**所有注册表写入必须用 .NET 的 `RegistryKey` API**（`SetValue("", "")`），不要调用 `reg.exe`。
