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

## 补充验证（Task 7 之后）：文件夹删除不受虚拟化影响

包外建 `%LOCALAPPDATA%\PowerToolsSpikeLocal`、`%APPDATA%\PowerToolsSpikeRoaming`、`C:\ProgramData\PowerToolsSpikeData`（各含一个子目录和文件），从已部署的包里用 `ShellRecycleBin.Delete` 移到回收站：三个都**真的从真实文件系统消失**（包外 `Test-Path` 为 False）。所以文件夹残留的回收站删除在包身份下有效，只有注册表被虚拟化。
测试产生的三个目录现在在用户的回收站里。

## 补充验证二：包里启动的子进程可以删真实注册表（推翻"注册表只能展示"）

背景：Geek / Revo 的核心是清注册表。前面发现"进程内 `RegistryKey` 删除会被虚拟化"，这里验证"让别的进程来删"行不行。方法同前：包外用 PowerShell 建真实测试键，从已部署的包里用不同方式启动删除，再在包外看真实注册表。

| 方式 | 目标 | 结果 |
|---|---|---|
| 包内直接启动子进程 `reg.exe delete "HKCU\...\LeftC" /f` | HKCU | **真的删掉了** |
| 让资源管理器打开一个 `.cmd`（`explorer.exe x.cmd`，进程不是我们的子进程）里面 `reg delete` | HKCU | **真的删掉了** |
| 计划任务（`schtasks /create` + `/run`）执行 `reg delete` | HKCU | **真的删掉了** |
| 包内 `Process.Start("reg.exe", "delete \"HKLM\SOFTWARE\...\LeftE\" /f /reg:64")`，`Verb=runas`（一次 UAC） | HKLM | **真的删掉了**，退出码 0 |
| WMI `Win32_Process.Create` | HKCU | 我的命令引号写错，没得出结论（不需要，已有可行方式） |

结论：虚拟化只作用于**包进程自己**的注册表 API（`RegistryKey.DeleteSubKeyTree` 等）；它启动的 `reg.exe` 不受影响，HKLM 经 UAC 提权后也可以。所以：

- 注册表残留**可以删**：用 `reg.exe export` 先导出 `.reg` 备份（reg.exe 自己的格式，可双击还原），再 `reg.exe delete <key> /f /reg:64`；HKLM 的项通过提权帮手（帮手是已提权的进程，由它启动 reg.exe）。不需要扩大 `unvirtualizedResources`，也不需要计划任务这类绕行方式。
- 之前的"注册表只展示、不可删除"的结论作废；帮手端需要新增注册表白名单校验，这部分尚未实现。
- 审核风险：做法只是启动系统自带的 `reg.exe`，没有借助计划任务或资源管理器绕出沙箱，应当比较稳妥，但提审说明里要写明用途。
- 实现时注意：`reg.exe` 是 32 位进程的 WOW 重定向会改变 HKLM\SOFTWARE 的视图，必须带 `/reg:64`；路径要用参数数组/严格引号，不能把用户可控字符串拼进命令行。

## 补充验证三：应用图标（logo）怎么取

方法：在已部署的包里放一个临时探针，用不同方式取图标、转成 PNG 存盘，我再逐个看图。验证后探针已删除。

| 来源 | 做法 | 结果 |
|---|---|---|
| 桌面程序的 exe | `SHCreateItemFromParsingName(路径)` → `IShellItemImageFactory.GetImage(64×64, SIIGBF_ICONONLY)` → `GetDIBits` 取像素 → `BitmapEncoder` 写 PNG | 正确（PotPlayer 的黄色播放图标），带透明通道；第一次约 150 ms，之后 5–65 ms |
| `DisplayIcon` 为 `文件,序号` | `PrivateExtractIcons(文件, 序号, 64, 64)` → `GetIconInfo` → 取 `hbmColor` | 可行（`imageres.dll,5`，约 160 ms），透明通道正常 |
| 商店应用 | `package.GetAppListEntriesAsync()` 拿到 AUMID（全部 6 个测试包都有），再 `shell:AppsFolder\<AUMID>` 取图 | **flags=0（默认）正确**（计算器、画图的真实图标），23–160 ms；**`SIIGBF_ICONONLY` 会返回通用的白纸图标，是错的，商店应用不能用它** |
| 商店应用（读清单自己挑） | 读 `AppxManifest.xml` 的 `Square44x44Logo`，在 Assets 里挑变体 | 清单和图片都能读（`WindowsApps` 可读），但变体有几十种（scale、targetsize、altform-unplated、对比度），挑出来的 Codex 图标是白色线稿，在白底上看不见。不如走 shell。 |

结论：

- 桌面程序/Steam：`DisplayIcon`（含序号）→ 没有就取 `AppFolder` 里的主程序 → 通用图标。取图用 `IShellItemImageFactory`（ICONONLY）。
- 商店应用：用 AUMID + `shell:AppsFolder`，**不要加 ICONONLY**。
- 白色线稿的图标（比如 Codex）在浅色背景上看不见，界面里要给图标垫一块浅灰/半透明的小底板。
- 开销：150 多项在后台限流取，约几秒，不阻塞列表；缓存不是第一版必需。
- 没有新的权限或清单改动。
