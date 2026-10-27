# 任务栏背单词模块——设计文档

日期：2026-10-27
状态：已批准（2026-10-27）。批准时用户追加一条需求：PowerTools 改为单例（见 5.5）。

## 1. 意图与背景

用户（开发者本人）想在日常使用电脑时被动背单词：把一个 json 词表里的单词和释义，每 5 秒轮换一次，直接显示在 Windows 11 任务栏上，不占用任何桌面窗口。目前**只自己用**，不考虑分发，也不进 Store 的对外功能宣传。

此前已做了两个可运行的验证（在 PowerTools 仓库之外，`D:\SourceCode\SideProject\TaskbarVocab` 的 WPF 版，和 `D:\SourceCode\SideProject\TaskbarVocabWinUISpike` 的 WinUI 3 版），结论：

- 把窗口 `SetParent` 成 `Shell_TrayWnd` 的子窗口，文字可以直接浮在任务栏上，WinUI 3 下同样成立（渲染、每 2 秒刷新均正常）。
- WinUI 3 窗口默认背景不透明（纯白），需要自定义 `SystemBackdrop` 才能透明；该 backdrop 需要先在线程上建一个 `Windows.System.DispatcherQueue`（用 `CoreMessaging.dll` 的 `CreateDispatcherQueueController`），否则崩溃（0xC000027B）。
- 进程必须是 `PerMonitorV2` DPI 感知，否则所有窗口坐标被虚拟化而错位。PowerTools 的 `app.manifest` 已经声明，不需要改。
- 本机（Windows 11 build 26300）的 UI Automation 不暴露任务栏应用图标，无法"测量空隙"，所以位置改为**锚定到托盘窗口 `TrayNotifyWnd` 的左边缘**。

做法借鉴自开源项目 ANYNC/TaskbarLyrics（MIT 许可）：以 `SetParent` 嵌入 `Shell_TrayWnd`。本实现是重新写的，没有复制其代码。

## 2. 目标、非目标、反目标

**目标**
- 作为 PowerTools 的第四个模块（`IToolModule`，`ModuleId = "vocab"`，`DisplayName = "背单词"`），在左侧导航里有一项。
- 读 qwerty-learner 的词典格式（`[{ "name", "trans": [...], "usphone" }]`），已用 `NGSL_1.2.json`（1755 词）验证过格式。
- 任务栏上显示两行：第一行单词 + 美式音标，第二行释义（`trans` 用 `；` 连接，过长用省略号截断）。
- 设置页：选择词表文件、"开始/停止"开关、显示词数和错误信息。
- **PowerTools 改为单例**（用户追加）：再次启动不再开第二个窗口，而是把已运行的窗口带到前台。这也避免了两个实例各自在任务栏上嵌一个单词条。

**非目标（本版不做）**
- 关闭主窗口后继续显示（常驻后台）。需要托盘图标、关窗口改为隐藏、并改动右键菜单"分析此文件夹"的命名管道逻辑，成本评估见对话记录，延后。
- 开机自启、`StartupTask`。
- 随机顺序、记录学习进度、间隔可配置、点击/快捷键切词。
- 自动避开任务栏图标（UI Automation 在目标系统上不可用）。
- 副显示器任务栏、垂直任务栏。

**反目标（即使技术上能跑也算失败）**
- 单词条遮挡任务栏上的图标或托盘按钮，或拦截它们的点击。
- 关掉 PowerTools 主窗口后进程仍然留在后台、且没有任何退出入口。
- 设置页在词表损坏时崩溃，或让整个 PowerTools 无法启动。
- 单例检测在连续快速启动（双击、连点）时放过了两个实例；或者反过来，第一个实例卡住时，再次启动没有任何反应也没有任何提示地"吞掉"。

## 3. 约束

- Windows 11 only，MSIX 全信任（`runFullTrust`），普通用户权限，不申请管理员权限。
- 不引入 GPL 代码；TaskbarLyrics 是 MIT，本实现只借鉴做法。
- 按项目既有约定：引擎层与 UI 层分离，每个业务模块实现 `IToolModule`；模块在 `App.xaml.cs` 构造函数里 `Modules.Register(...)`，页面在 `MainWindow` 的 `Pages` 字典里按 `ModuleId` 映射。
- 现有 spike 是 unpackaged 运行的；**MSIX 包身份下嵌入任务栏是否同样可行尚未验证**，见第 7 节。

## 4. 做法（已选定）

考虑过两个方案：

1. **带设置页（选定）**：导航里多一项，选词表、开始/停止，状态持久化。
2. 无页面，启动即显示固定路径的词表：代码最少，但无法换词表、无法暂停，只能退出整个 PowerTools。

选择方案 1，原因：用户的使用方式是"先看看运行情况"，必然会遇到想暂停或换词表的时候。

## 5. 设计

### 5.1 新项目 `PowerTools.Modules.Vocab`（引擎层，无 UI 依赖）

目标框架与同类模块一致（`net8.0-windows10.0.19041.0`），引用 `PowerTools.Core`，配套 `PowerTools.Modules.Vocab.Tests`（xUnit，沿用现有测试项目的写法）。

- `WordCard(string Name, List<string> Trans, string? Usphone)`：属性名与 json 的 `name/trans/usphone` 大小写不敏感绑定。
- `WordListLoader.Load(path)`：返回词表或错误信息，不抛异常给调用方。失败情形：文件不存在、读取被拒、json 格式错误、词表为空。`trans` 为空或缺失的词条**跳过**而不是整体失败。
- `WordRotator`：按文件顺序循环，`Next()` 到末尾回到第一个；词表为空时不可用。
- `VocabModule : IToolModule`：持有当前词表路径、已加载词表和"运行中"状态，对外暴露 `Load(path)`、`Start()`、`Stop()` 和 `Next()` 之类的最小接口。不直接依赖 WinRT 的 `LocalSettings`。

### 5.2 App 项目（UI 层，WinUI 3）

- **`Views/VocabView`**：导航页。控件：选择文件按钮（复用 `App.CurrentWindow` 做 file picker，与现有页面一致）、词数/错误提示、"开始/停止"开关。页面本身**不拥有**单词条窗口，因为页面在导航切换时会被销毁和重建。
- **`TaskbarStripHost`**（App 层的单例，类似 `App.Modules` 的静态持有方式）：负责创建/销毁单词条窗口，生命周期独立于页面，所以切到别的模块页面时单词条不会消失。
- **`TaskbarStripWindow`**：WinUI 3 `Window`，透明背景，两个 `TextBlock`，`DispatcherQueueTimer` 每 5 秒调用 `Next()` 换词。
- **`TransparentBackdrop`**：自定义 `SystemBackdrop`，含上述 DispatcherQueue 初始化（直接沿用 spike 已验证的写法）。
- **嵌入逻辑**（`TaskbarEmbedder` 之类，Win32 P/Invoke）：找 `Shell_TrayWnd` 和其子窗口 `TrayNotifyWnd`；`SetParent`；去掉 `WS_POPUP/CAPTION/THICKFRAME/SYSMENU`，加上 `WS_CHILD|WS_VISIBLE` 和 `WS_EX_NOACTIVATE`；只用 `SetWindowPos`（任务栏客户区的物理像素）定位，不写 `Window` 的位置属性。
- **位置规则**：窗口右边缘 = 托盘左边缘 − 8 DIP；宽度取 `min(560 DIP, 可用宽度)`；可用宽度小于 100 DIP 时窗口收缩到 0 宽（不遮挡）。
- **每 2 秒**重新检查一次：父窗口是否还是当前的 `Shell_TrayWnd`（Explorer 重启后会变）、DPI 是否变化、托盘位置是否变化，必要时重新嵌入和重新定位。
- **文字颜色**：读 `HKCU\...\Themes\Personalize\SystemUsesLightTheme`，浅色任务栏用黑字，深色用白字。

### 5.3 设置与持久化

- `LocalSettings` 新增 `Vocab.Path`（词表路径，只引用不复制）和 `Vocab.Enabled`（上次是否处于运行状态）。读写放在 App 层（与 `Theme` 的存取位置一致）。
- 启动时，若 `Vocab.Enabled` 为真且路径可读，则自动开始；否则不显示。

### 5.4 生命周期（本版最容易出错的点）

WinUI 3 的进程在存在其他窗口时可能不会随主窗口关闭而退出。单词条本身就是一个 `Window`，若不处理，**用户关闭 PowerTools 主窗口后，进程会因单词条窗口而继续存在**，且没有退出入口，违反反目标。因此：

- `MainWindow` 关闭时必须先关闭单词条窗口，再让应用退出。
- 这一行为需要手动验证（关主窗口 → 任务管理器里没有残留的 `PowerTools.App` 进程）。

### 5.5 单例（用户追加的需求）

**现状**：`App.OnLaunched` 只有在命令行带 `--analyze` 时才会通过命名管道 `AnalyzeRequestListener.DefaultPipeName`（每个 Windows 用户一个名字）尝试把请求交给已运行的实例；**普通启动不检查**，所以现在双击两次 PowerTools 会开出两个窗口。

**考虑过的两个做法**
1. Windows App SDK 的 `AppInstance.FindOrRegisterForKey` + `RedirectActivationToAsync`：官方做法，但需要自定义 `Main`（关闭 XAML 生成的入口），并把参数处理改到激活参数上，会改动现有且有测试覆盖的"分析此文件夹"流程。
2. **命名互斥体判定谁是第一个 + 复用现有管道传"激活"请求（选定）**：改动小，复用已有测试覆盖的通道。

**设计**
- Core 新增 `SingleInstanceGuard`：用每个用户会话一个的命名互斥体（`Local\PowerTools.SingleInstance`）；能拿到就是第一个实例（primary），拿不到就是后续实例（secondary）。互斥体是进程退出时由系统释放的，primary 退出时显式释放。
- 判定放在 `App.OnLaunched` 的**最前面**：在创建 `MainWindow`、启动管道监听、自动开始单词条**之前**。secondary 绝不能启动单词条。
- secondary 通过现有管道发送一条消息后退出：带 `--analyze <路径>` 时发送路径（与现在一致）；普通启动发送固定的激活令牌 `--activate`。
- 因为 primary 可能刚拿到互斥体、管道监听还没建好，secondary 发送要**重试最长约 3 秒**；仍无人响应时**也退出**（拿着互斥体的进程是活的，再开第二个实例比"没反应"更糟）。这是我的决定，用户可改。
- primary 收到消息后在 UI 线程处理：路径 → `OpenInDiskAnalyzer`（现状不变）；`--activate` → 复用现有的 `BringToFront()`（最小化时先还原再激活）。secondary 在发送前已调用 `AllowSetForegroundWindow`（现有代码），让 primary 能真正取得前台。
- 协议兼容：路径消息不变。`--activate` 不是完整路径，不会与路径冲突，`App` 里原有的 `Path.IsPathFullyQualified` 检查仍然成立。
- 现有类名 `AnalyzeRequestListener/Sender` 不强制改名；是否重命名由实现者决定。
- `LaunchRequest` 增加"消息是激活请求还是路径"的判定，便于单测。

## 6. 错误处理

- 词表无法加载：页面显示具体原因，不启动单词条，不影响其他模块。
- 找不到 `Shell_TrayWnd`（Explorer 正在重启）：保持窗口隐藏，下一个 2 秒周期重试，不抛异常。
- `SetParent` 失败：记录原因，页面状态显示为"无法嵌入任务栏"，并停止，不留下一个悬空的顶层窗口。
- 单个词条 `trans` 为空：跳过。

## 7. 测试与验证

- **自动化（xUnit）**：`WordListLoader`（有效文件、缺失、格式错误、空列表、含空 `trans` 的词条被跳过）；`WordRotator`（顺序、到末尾回绕、空表）；`VocabModule`（加载、开始/停止状态）。
- **自动化（单例，Core.Tests）**：`SingleInstanceGuard`（同名第二个不是 primary；primary 释放后可再次成为 primary；每个测试用唯一的互斥体名，避免互相干扰，也避免碰到真实的 PowerTools）；`LaunchRequest` 的消息判定（激活令牌 vs 路径 vs 空/非法）。现有 `AnalyzeRequestChannelTests` 保持不变。
- **手动（单例）**：连续双击启动只出现一个窗口；主窗口最小化时再次启动会还原并到前台；资源管理器右键"分析此文件夹"仍然有效；几乎同时启动两次只留下一个实例。
- **手动（Windows 行为无法可靠自动化）**：
  1. 在**已注册的 MSIX dev 包**下运行（不是 unpackaged），确认能嵌入、透明、每 5 秒换词。这是本版**最大的未验证风险**；若失败需要回头调整方案。
  2. 关闭主窗口后进程确实退出。
  3. 深色/浅色任务栏下文字可读；任务栏图标很多、托盘附近放不下时窗口是否正确隐藏。
  4. Explorer 重启后单词条是否恢复。
- 注意：普通 `dotnet build` 不会刷新已注册的 AppX layout（`bin\...\AppX`），验证 MSIX 行为需要在 Visual Studio 里 Deploy/F5。

## 8. 第三方声明

在仓库根的 `THIRD-PARTY-NOTICES.md` 增加一条：做法参考 ANYNC/TaskbarLyrics（MIT）。（本条是我的主张，不是用户明确要求。）

## 9. 留给实现者决定的

- 新类的具体命名与文件划分。
- `TaskbarStripHost` 是静态单例还是挂在 `App` 上的属性，二者择一，与 `App.Modules` 风格一致即可。
- 导航图标的 glyph 选择。
