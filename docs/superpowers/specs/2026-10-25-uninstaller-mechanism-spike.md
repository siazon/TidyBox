# 卸载工具机制验证（Task 1 结论）

日期：2026-10-25　环境：Windows 11 build 26300，普通用户权限，已部署的 MSIX 包（x86，松散注册，身份 `PowerTools.Dev`，能力只有 `runFullTrust` + `unvirtualizedResources`）。
验证方式：临时探针代码放在 App 里（已删除），结果由我在**包外**用 PowerShell 读取真实注册表确认。

## 结果

| | 做了什么 | 观察到的 |
|---|---|---|
| **A Store 卸载** | 包内调用 `new PackageManager().RemovePackageAsync("21090PaddyXu.QuickLook_4.5.0.0_neutral__egxr34yet59cg")`（QuickLook，用户同意卸载） | **成功**：没有 UAC，没有异常，`Get-AppxPackage` 确认已卸载。manifest 里没有声明 `packageManagement`。 |
| **B1 HKCU 删除** | 包外用 PowerShell 建 `HKCU\Software\PowerToolsSpike\Leftover`；包内 `Registry.CurrentUser.DeleteSubKeyTree` | 包内无异常、重新打开显示"已消失"，但**包外真实注册表里仍在** → **被虚拟化，没有真正删除**。再次运行时包内报"子键不存在"（墓碑留在包私有 hive）。 |
| **B2 HKLM 删除** | 包外以管理员建 `HKLM\SOFTWARE\PowerToolsSpike\Leftover`；包内（`Registry64` 视图）直接删除；另由包拉起一个管理员 PowerShell 子进程执行 `Remove-Item` | 包内直接删除和管理员子进程**都认为键不存在**（子进程报 `PathNotFound`），真实注册表里**键仍在** → HKLM 同样被包身份遮蔽/虚拟化，提权子进程也逃不出去。 |
| **C Steam 登记** | 读三个 `Uninstall` 根 | `Steam App 413150`（Stardew Valley）在 **64 位 `HKLM\...\Uninstall`**，`UninstallString` = `"C:\Program Files (x86)\Steam\steam.exe" steam://uninstall/413150`，可靠识别。`Steam`（客户端本身）在 WOW6432Node，`UninstallString` 是 `uninstall.exe`，不匹配 `steam://uninstall/<digits>`，按普通 Win32 处理。机器上另一个 `appmanifest_228980`（Steamworks 运行库）没有 Uninstall 键——它不是游戏，不影响。 |

验证后已清理包外真实注册表里的测试键；QuickLook 已按用户同意被卸载（可在 Store 重装）。

## 结论（按 plan 的决策表）

- **A(1) 成功** → Store 路由用 `PackageManager.RemovePackageAsync`。**风险：**这是开发期的松散注册包，Store 正式包是否需要受限能力 `packageManagement` 无法在这里验证；提审时留意，必要时按规则声明并在审核说明里解释。
- **B 的 HKCU 和 HKLM 删除都被虚拟化** → **注册表残留整体降级为"只展示、不提供删除"**，提示用户自行用注册表编辑器处理。不为此扩大 `unvirtualizedResources`（要排除整个 `HKCU\Software`/`HKLM\SOFTWARE`，Store 审核风险过高，不值得）。文件夹残留不受影响。
- **C 可靠** → Steam 靠 `UninstallString` 识别，不需要 `appmanifest` 回退，也不需要引用磁盘分析模块。

## 对 plan 的影响（已写进 plan）

- Task 4：`ResidueKind.RegistryKey` 保留（用于展示），但 `ResidueItem` 增加 `bool Removable`，注册表项恒为 `false`。
- Task 5：`ResidueRemover` 只处理文件夹，**不做 `.reg` 备份和注册表删除**；`IElevationClient` 只用于 `Program Files`/`ProgramData` 下的文件夹。
- Task 6：帮手只处理 `folders`，白名单只含文件夹根。
- Task 7/8：界面里注册表残留灰显、不可勾选，旁注"无法在此删除，请手动处理"。
