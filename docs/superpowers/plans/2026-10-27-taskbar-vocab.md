# 任务栏背单词 + PowerTools 单例 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 PowerTools 改成单例，并新增"背单词"模块：读 qwerty-learner 格式的 json 词表，每 5 秒在 Windows 11 任务栏托盘左侧轮换显示单词 + 释义。

**Architecture:** 单例用"命名互斥体判定谁是第一个 + 复用现有命名管道传 `--activate`"实现（Core 层，可单测）。背单词分两层：`PowerTools.Modules.Vocab`（词表加载、轮换、模块状态、位置计算，纯逻辑，xUnit 覆盖）和 App 层（设置页、透明 WinUI 3 窗口经 `SetParent` 嵌入 `Shell_TrayWnd`，Windows 行为只能手动验证）。

**Tech Stack:** .NET 8、WinUI 3（Windows App SDK 2.5.1）、xUnit 2.5.3、`System.Text.Json`、Win32 P/Invoke（`user32.dll`、`CoreMessaging.dll`）。

**Spec:** `docs/superpowers/specs/2026-10-27-taskbar-vocab-design.md`（执行者须先读它，尤其 5.4 生命周期与 5.5 单例）。

## Global Constraints

- Windows 11 only；MSIX 全信任（`runFullTrust`）；普通用户权限，不申请管理员权限。
- 不引入 GPL 代码。TaskbarLyrics（MIT）只借鉴做法，本实现不复制其代码。
- 新项目 `PowerTools.Modules.Vocab` 目标框架 `net8.0-windows10.0.19041.0`，`ImplicitUsings`/`Nullable` 开启，引用 `PowerTools.Core`；测试项目用 xUnit 2.5.3，csproj 照抄 `tests/PowerTools.Modules.Uninstaller.Tests`。
- 模块：`ModuleId = "vocab"`，`DisplayName = "背单词"`。设置键：`Vocab.Path`、`Vocab.Enabled`（`ApplicationData.Current.LocalSettings`，与 `Theme` 同处存取）。
- 单例：互斥体名 `Local\PowerTools.SingleInstance`；激活令牌 `--activate`；secondary 向管道发送最长等待 3 秒；secondary 绝不启动单词条。
- 词表格式：`[{ "name", "trans": [...], "usphone" }]`（大小写不敏感）。第一行文字 `"{name}  /{usphone}/"`（两个空格；无音标时只有 name），第二行为 `trans` 用 `；` 连接；每行单行省略号截断。
- 位置：窗口右边缘 = 托盘左边缘 − 8 DIP；宽度 = `min(560 DIP, 可用宽度)`；可用宽度 < 100 DIP 时窗口收缩为 0 宽。换词间隔固定 5 秒；维护检查（重新嵌入/重新定位）每 2 秒。文字颜色：`SystemUsesLightTheme == 1` 用黑字，否则白字。
- 工作区里 `CLAUDE.md` 有用户有意保留的本地改动，**绝不暂存它**；每次提交只 `git add` 本计划列出的路径。提交信息前缀 `feat:`/`fix:`/`docs:`/`chore:`。
- Core 测试命名沿用现有的"整句下划线"风格（如 `A_listener_receives_the_path_that_was_sent`）。
- 普通 `dotnet build` 不会刷新已注册的 dev 包布局（`src\PowerTools.App\bin\x86\Debug\net8.0-windows10.0.26100.0\AppX`）。凡需要运行 App 的手动验证，都要请用户先在 Visual Studio 里 Deploy（x86 Debug）。

## Review Focus

按最可能影响使用者的顺序，每条后面是持有其测试/验证的任务：

1. 词表是合法 json 但不是数组、或含 `trans` 缺失/为空/全空白、`name` 为空的条目——应跳过坏条目或给出清晰错误，不能崩溃（Task 3）。
2. 已保存的词表文件之后被删除/改名——下次启动不崩溃、不显示单词条，设置页显示"找不到文件"（Task 3 单测 + Task 8 手动）。
3. 重复 `Start()`、未运行时 `Stop()`、单词条运行中换到一个坏词表——状态保持一致，运行中遇到坏词表则停止（Task 4）。
4. 极长的单词或释义——单行省略号截断，不换行、不撑破窗口（Task 6/7 手动）。
5. 几乎同时启动两次、关主窗口后的进程残留、Explorer 重启——只剩一个实例；关主窗口后无残留进程；单词条自动恢复（Task 1 单测 + Task 2/8 手动）。

---

### Task 1: 单例的 Core 部分（激活令牌 + SingleInstanceGuard）

**Files:**
- Modify: `src/PowerTools.Core/Launch/LaunchRequest.cs`
- Create: `src/PowerTools.Core/Launch/SingleInstanceGuard.cs`
- Test: `tests/PowerTools.Core.Tests/LaunchRequestTests.cs`（追加）、`tests/PowerTools.Core.Tests/SingleInstanceGuardTests.cs`（新建）、`tests/PowerTools.Core.Tests/AnalyzeRequestChannelTests.cs`（追加一个）

**Interfaces:**
- Produces: `LaunchRequest.ActivateMessage` (`const string`, 值 `"--activate"`)；`LaunchRequest.IsActivate(string? message) -> bool`（trim 后与令牌相等，忽略大小写）。
- Produces: `SingleInstanceGuard(string name) : IDisposable`；`bool IsPrimary { get; }`；`static string DefaultName`（`@"Local\PowerTools.SingleInstance"`）。

- [ ] **Step 1: 写失败的测试**
  - `LaunchRequestTests`：`IsActivate_is_true_for_the_token_ignoring_case_and_whitespace`（`"--activate"`、`" --ACTIVATE \n"` 为 true）；`IsActivate_is_false_for_paths_and_empty_input`（`null`、`""`、`@"C:\Users\x"`、`"--analyze"` 为 false）。
  - `SingleInstanceGuardTests`（每个测试用 `"PowerTools.Tests." + Guid.NewGuid()` 作名字，不碰真实互斥体）：`The_first_guard_is_primary`；`A_second_guard_with_the_same_name_is_not_primary`；`Once_all_guards_are_disposed_a_new_guard_is_primary_again`；`Guards_with_different_names_are_independent`。
  - `AnalyzeRequestChannelTests`：`An_activate_message_is_delivered_like_any_other_text`（监听器收到的字符串恰为 `"--activate"`）。
- [ ] **Step 2: 运行并确认失败**
  Run: `dotnet test tests/PowerTools.Core.Tests --filter "FullyQualifiedName~LaunchRequestTests|FullyQualifiedName~SingleInstanceGuardTests|FullyQualifiedName~AnalyzeRequestChannelTests"`
  Expected: 编译失败（`ActivateMessage`/`IsActivate`/`SingleInstanceGuard` 未定义）。
- [ ] **Step 3: 实现**
  `SingleInstanceGuard` 用 `new Mutex(initiallyOwned: false, name, out createdNew)`，`IsPrimary = createdNew`；**不要** `WaitOne`/`ReleaseMutex`（互斥体有线程归属，App 的启动线程与退出线程可能不同）；`Dispose` 只关闭句柄（最后一个句柄关闭时互斥体被系统销毁）。`IsActivate` 为纯字符串比较。
- [ ] **Step 4: 运行并确认通过**
  Run: 同 Step 2。 Expected: 全部通过（`已通过!`）。
- [ ] **Step 5: 提交**
  ```bash
  git add src/PowerTools.Core/Launch tests/PowerTools.Core.Tests
  git commit -m "feat: add the activate message and a named-mutex single-instance guard"
  ```

### Task 2: 单例接入 App

**Files:**
- Modify: `src/PowerTools.App/App.xaml.cs`（`OnLaunched` 与字段）、`src/PowerTools.App/MainWindow.xaml.cs`（`BringToFront` 由 `private` 改为 `public`）

**Interfaces:**
- Consumes: Task 1 的 `SingleInstanceGuard`、`LaunchRequest.IsActivate`、`LaunchRequest.ActivateMessage`；现有 `AnalyzeRequestSender.TrySend(string pipeName, string path, TimeSpan timeout) -> bool`（连接会一直等到管道出现或超时，所以 3 秒超时即覆盖"primary 还没建好监听"）。
- Produces: 无新公共接口。

- [ ] **Step 1: 改 `OnLaunched`**
  1. 在方法最前面创建 `SingleInstanceGuard`（保存为 `App` 的字段，进程存活期间必须持有）。
  2. `!IsPrimary`：调用现有的 `AllowSetForegroundWindow(AsfwAny)`，向 `AnalyzeRequestListener.DefaultPipeName` 发送（有 `--analyze` 路径就发路径，否则发 `LaunchRequest.ActivateMessage`），超时 3 秒；发送结果忽略；释放 guard；`Exit()`；`return`。
  3. 删除被取代的旧分支（仅当带 `--analyze` 时才尝试发送、超时 250 ms 的那段）。
  4. primary 的监听回调：`LaunchRequest.IsActivate(message)` → `main.DispatcherQueue.TryEnqueue(main.BringToFront)`；否则保持现有的"完整路径 → `OpenInDiskAnalyzer`"。
- [ ] **Step 2: 构建**
  Run: `dotnet build src/PowerTools.App/PowerTools.App.csproj -p:Platform=x86`
  Expected: 0 个警告，0 个错误。
- [ ] **Step 3: 全量测试不回归**
  Run: `dotnet test PowerTools.sln --no-build`（先对 sln 做一次 `dotnet build PowerTools.sln`）
  Expected: 全部通过（含现有 `AnalyzeRequestChannelTests`）。
- [ ] **Step 4: 手动验证（请用户先 Deploy）**
  用 App 的 execution alias `PowerTools.exe`：
  1. 连续启动两次 → `(Get-Process PowerTools.App).Count` 为 `1`，窗口只有一个。
  2. 最小化主窗口后再启动一次 → 窗口还原并到前台，进程数仍为 1。
  3. 主窗口打开时执行 `PowerTools.exe --analyze C:\Windows` → 磁盘分析页开始分析该目录，进程数仍为 1。
  4. 同时启动两次（`1..2 | % { Start-Process PowerTools.exe }`）→ 最终只有一个实例。
- [ ] **Step 5: 提交**
  ```bash
  git add src/PowerTools.App/App.xaml.cs src/PowerTools.App/MainWindow.xaml.cs
  git commit -m "feat: make PowerTools single-instance; a second launch brings the running window to the front"
  ```

### Task 3: Vocab 项目骨架 + WordCard + WordListLoader

**Files:**
- Create: `src/PowerTools.Modules.Vocab/PowerTools.Modules.Vocab.csproj`、`WordCard.cs`、`WordListLoader.cs`
- Create: `tests/PowerTools.Modules.Vocab.Tests/PowerTools.Modules.Vocab.Tests.csproj`、`WordCardTests.cs`、`WordListLoaderTests.cs`
- Modify: `PowerTools.sln`

**Interfaces:**
- Produces（namespace `PowerTools.Modules.Vocab`）：
  - `sealed record WordCard(string Name, List<string> Trans, string? Usphone)`，另有 `string Title { get; }` 与 `string Meaning { get; }`（规则见 Global Constraints）。
  - `sealed record WordListLoadResult(IReadOnlyList<WordCard> Cards, string? Error)`，`bool Succeeded => Error is null`。
  - `static class WordListLoader { static WordListLoadResult Load(string path); }`（不向调用方抛异常）。

- [ ] **Step 1: 建两个项目并登记进 sln**
  csproj 按 Global Constraints 照抄；测试项目引用被测项目。
  Run: `dotnet sln PowerTools.sln add src/PowerTools.Modules.Vocab/PowerTools.Modules.Vocab.csproj --solution-folder src` 与 `dotnet sln PowerTools.sln add tests/PowerTools.Modules.Vocab.Tests/PowerTools.Modules.Vocab.Tests.csproj --solution-folder tests`
  Expected: 两个项目被添加；`git diff PowerTools.sln` 只有新增项目的条目（若出现 Visual Studio 版本号改动，`git checkout` 掉那几行）。
- [ ] **Step 2: 写失败的测试**
  - `WordCardTests`：`Title_has_the_phonetic_in_slashes_after_two_spaces`（`remain` + `rɪˈmen` → `"remain  /rɪˈmen/"`）；`Title_is_just_the_word_without_a_phonetic`（`null` 与 `""`）；`Meaning_joins_the_entries_with_a_full_width_semicolon`（`["a","b"]` → `"a；b"`）。
  - `WordListLoaderTests`（临时文件，测试类 `IDisposable` 清理）：
    - `Qwerty_format_loads_in_file_order`（两个词 `remain`/`among`，断言顺序、`Usphone`、`Trans[0]`）。
    - `A_missing_file_reports_not_found`（`Error` 含 `找不到文件`，`Cards` 为空）。
    - `A_directory_instead_of_a_file_reports_unreadable`（`Error` 含 `无法读取文件`）。
    - `Malformed_json_reports_a_format_error`（`Error` 含 `词表格式不正确`）。
    - `A_json_object_instead_of_an_array_reports_a_format_error`。
    - `Entries_without_a_usable_translation_or_name_are_skipped`（`trans` 为 `[]`、缺失、`["", " "]`，以及 `name` 为 `""` 的条目被跳过，其余保留，`Succeeded`）。
    - `A_list_with_nothing_usable_reports_empty`（`[]` 与全被跳过都得 `Error == "词表里没有可用的单词"`）。
    - `A_utf8_file_with_a_byte_order_mark_loads`。
- [ ] **Step 3: 运行并确认失败**
  Run: `dotnet test tests/PowerTools.Modules.Vocab.Tests`
  Expected: 编译失败（类型未定义）。
- [ ] **Step 4: 实现**
  `WordListLoader.Load`：`File.ReadAllText`（捕获 `FileNotFoundException`/`DirectoryNotFoundException` → `找不到文件：{path}`；`IOException`/`UnauthorizedAccessException` → `无法读取文件：{原因}`）；`JsonSerializer.Deserialize<List<WordCard?>>`，`PropertyNameCaseInsensitive = true`；`JsonException` → `词表格式不正确：{原因}`。记录绑定后 `Trans` 在运行时可能为 `null`，过滤条件要据此写：`Name` 非空且 `Trans` 中至少有一个非空白项。过滤后为空 → `词表里没有可用的单词`。
- [ ] **Step 5: 运行并确认通过**
  Run: `dotnet test tests/PowerTools.Modules.Vocab.Tests` Expected: 全部通过。
- [ ] **Step 6: 提交**
  ```bash
  git add src/PowerTools.Modules.Vocab tests/PowerTools.Modules.Vocab.Tests PowerTools.sln
  git commit -m "feat: add the Vocab module project with the qwerty word-list loader"
  ```

### Task 4: WordRotator + VocabModule

**Files:**
- Create: `src/PowerTools.Modules.Vocab/WordRotator.cs`、`VocabModule.cs`
- Test: `tests/PowerTools.Modules.Vocab.Tests/WordRotatorTests.cs`、`VocabModuleTests.cs`

**Interfaces:**
- Consumes: Task 3 的 `WordCard`、`WordListLoader.Load`。
- Produces：
  - `sealed class WordRotator { WordRotator(IReadOnlyList<WordCard> cards); WordCard? Next(); }`（按顺序循环，到末尾回到第一个；空表返回 `null`）。
  - `sealed class VocabModule : IToolModule`：`ModuleId`/`DisplayName` 见 Global Constraints；`Task InitializeAsync()`（立即完成）；`string? Path`（最近一次传给 `Load` 的路径，失败也保留）；`int WordCount`；`string? LoadError`；`bool IsRunning`；`bool Load(string path)`；`bool Start()`；`void Stop()`；`WordCard? Next()`。

- [ ] **Step 1: 写失败的测试**
  - `WordRotatorTests`：`Next_returns_the_cards_in_order_and_wraps_around`（3 张卡调 7 次）；`Next_on_an_empty_list_returns_null`。
  - `VocabModuleTests`（用临时 json）：`Id_and_display_name_are_fixed`；`Load_of_a_valid_file_sets_path_count_and_clears_the_error`；`Next_after_load_starts_at_the_first_word`；`Load_of_a_missing_file_returns_false_and_leaves_no_words`（`WordCount == 0`、`LoadError` 非空、`Path` 为尝试的路径）；`Start_without_words_returns_false_and_does_not_run`；`Start_twice_is_harmless_and_does_not_restart_the_rotation`；`Stop_when_not_running_does_nothing`；`A_failed_load_while_running_stops_it`；`A_successful_load_while_running_keeps_running_and_restarts_from_the_first_word`。
- [ ] **Step 2: 运行并确认失败** — Run: `dotnet test tests/PowerTools.Modules.Vocab.Tests` Expected: 编译失败。
- [ ] **Step 3: 实现** — `Load` 成功时重建 `WordRotator`；失败时清空词表、`IsRunning = false`。`Next()` 在无词表时返回 `null`。
- [ ] **Step 4: 运行并确认通过** — Expected: 全部通过。
- [ ] **Step 5: 提交**
  ```bash
  git add src/PowerTools.Modules.Vocab tests/PowerTools.Modules.Vocab.Tests
  git commit -m "feat: add the Vocab module state and word rotation"
  ```

### Task 5: StripPlacement（位置计算）

**Files:**
- Create: `src/PowerTools.Modules.Vocab/StripPlacement.cs`
- Test: `tests/PowerTools.Modules.Vocab.Tests/StripPlacementTests.cs`

**Interfaces:**
- Produces: `static class StripPlacement { static (int Left, int Width)? Calculate(double trayLeftPx, double scale); }`。`trayLeftPx` 是托盘左边缘相对任务栏客户区原点的物理像素，`scale` 是 DPI 缩放（1.75 即 175%）。规则见 Global Constraints；`Left`/`Width` 四舍五入到整数像素。

- [ ] **Step 1: 写失败的测试**
  - `Roomy_taskbar_places_the_full_width_window_just_left_of_the_tray`：`Calculate(3336, 1.75)` 等于 `(2342, 980)`（这是 spike 在这台 175% 缩放的 4K 屏上实测出的值）。
  - `A_narrow_gap_shrinks_the_window`：`Calculate(500, 1.0)` 等于 `(0, 492)`。
  - `Exactly_the_minimum_width_still_fits`：`Calculate(108, 1.0)` 等于 `(0, 100)`。
  - `Less_than_the_minimum_returns_null`：`Calculate(107, 1.0)`、`Calculate(0, 1.0)` 为 `null`。
  - `Invalid_input_returns_null`：`scale` 为 `0`、负数、`NaN`；`trayLeftPx` 为负数或 `NaN`。
- [ ] **Step 2: 运行并确认失败** — Expected: 编译失败。
- [ ] **Step 3: 实现** — `right = trayLeftPx − 8×scale`；`width = min(560×scale, right)`；`width < 100×scale` 返回 `null`；否则 `Left = right − width`。
- [ ] **Step 4: 运行并确认通过** — Expected: 全部通过。
- [ ] **Step 5: 提交**
  ```bash
  git add src/PowerTools.Modules.Vocab tests/PowerTools.Modules.Vocab.Tests
  git commit -m "feat: compute where the vocab strip sits left of the notification area"
  ```

### Task 6: 任务栏单词条窗口与宿主

**Files:**
- Create（目录 `src/PowerTools.App/TaskbarStrip/`）：`TransparentBackdrop.cs`、`TaskbarStripWindow.xaml`、`TaskbarStripWindow.xaml.cs`、`TaskbarStripHost.cs`、`TaskbarNative.cs`
- Modify: `src/PowerTools.App/PowerTools.App.csproj`（加 `ProjectReference` 到 Vocab）、`THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: `VocabModule.Next()`、`WordCard.Title/Meaning`、`StripPlacement.Calculate`。
- Produces（namespace `PowerTools.App.TaskbarStrip`，均 `internal`）：
  - `TaskbarStripHost(VocabModule module)`；`bool IsShowing { get; }`；`string? Error { get; }`（最近一次 `Show()` 失败的原因，成功为 `null`）；`bool Show()`；`void Hide()`。`Show`/`Hide` 只能在 UI 线程调用；`Hide` 在未显示时无操作。

- [ ] **Step 1: 实现** — 方向与已验证的 spike 一致（`D:\SourceCode\SideProject\TaskbarVocabWinUISpike`，可直接参照其中的 `TransparentBackdrop.cs` 与 `MainWindow.xaml.cs`；仓库外的文件，只读参考）。必须满足：
  - `TransparentBackdrop : SystemBackdrop`：`OnTargetConnected` 里先确保线程上有 `Windows.System.DispatcherQueue`（没有则 P/Invoke `CoreMessaging.dll` 的 `CreateDispatcherQueueController`，`ThreadType = 2`、`ApartmentType = 0`），再 `new Windows.UI.Composition.Compositor()` 并把 alpha=0 的 `CreateColorBrush` 赋给 `connectedTarget.SystemBackdrop`；`OnTargetDisconnected` 清理。缺少这个 DispatcherQueue 会以 `0xC000027B` 崩溃。
  - 窗口：`SetBorderAndTitleBar(false, false)`、`IsResizable = false`、`AppWindow.IsShownInSwitchers = false`；两个 `TextBlock`（第一行 16 pt `SemiBold` 显示 `Title`，第二行 12 pt、`Opacity 0.8` 显示 `Meaning`），均 `TextTrimming=CharacterEllipsis`、`MaxLines=1`。
  - 嵌入（`TaskbarNative.cs`）：`FindWindow("Shell_TrayWnd")`，`FindWindowEx(taskbar, 0, "TrayNotifyWnd")`；`SetParent`；样式去掉 `WS_POPUP|WS_CAPTION|WS_THICKFRAME|WS_SYSMENU|WS_MINIMIZEBOX|WS_MAXIMIZEBOX`，加上 `WS_CHILD|WS_VISIBLE`；扩展样式去掉 `WS_EX_TOOLWINDOW`，加上 `WS_EX_NOACTIVATE`；**之后**用 `GetAncestor(hwnd, GA_PARENT) == taskbar` 判断成功（不要用 `GetParent`：窗口还是弹出样式时它返回的是 owner，会误判失败）；位置只用 `SetWindowPos`（`SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED`，top = 0，height = 任务栏高度，left/width 来自 `StripPlacement`，`scale = GetDpiForWindow(taskbar) / 96.0`），不写 `Window` 的位置属性。`Calculate` 返回 `null` 时 `SetWindowPos(... 0, 0, 0, 0, SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE)` 收缩到 0 宽。
  - 定时：5 秒 `DispatcherQueueTimer` 调 `module.Next()` 换词；2 秒维护计时器：若父窗口不再是当前 `Shell_TrayWnd`（Explorer 重启）则重新嵌入，并重新计算位置、重读文字颜色。
  - 失败处理：嵌入失败 → `Error = "无法嵌入任务栏"`，关闭窗口，`Show()` 返回 `false`；`module.Next()` 返回 `null` → `Error = "没有可显示的单词"`；找不到 `Shell_TrayWnd` 时保持隐藏、下个周期重试，不抛异常。
  - 文字颜色：读 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` 的 `SystemUsesLightTheme`。
- [ ] **Step 2: 第三方声明** — 在 `THIRD-PARTY-NOTICES.md` 末尾追加一节，说明任务栏嵌入做法参考 ANYNC/TaskbarLyrics（https://github.com/ANYNC/TaskbarLyrics），MIT，`Copyright (c) 2026 anync`；写明未复制其代码、仅参考做法。
- [ ] **Step 3: 构建**
  Run: `dotnet build src/PowerTools.App/PowerTools.App.csproj -p:Platform=x86`
  Expected: 0 个警告，0 个错误。（窗口行为需要入口才能验证，放在 Task 7 的手动验证里。）
- [ ] **Step 4: 提交**
  ```bash
  git add src/PowerTools.App/TaskbarStrip src/PowerTools.App/PowerTools.App.csproj THIRD-PARTY-NOTICES.md
  git commit -m "feat: add the transparent taskbar strip window and its host"
  ```

### Task 7: "背单词"页面、模块注册与设置持久化

**Files:**
- Create: `src/PowerTools.App/Views/VocabView.xaml`、`VocabView.xaml.cs`、`src/PowerTools.App/TaskbarStrip/VocabSettings.cs`
- Modify: `src/PowerTools.App/App.xaml.cs`、`src/PowerTools.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `VocabModule`（Task 4）、`TaskbarStripHost`（Task 6）。
- Produces：`App.Vocab`（`static VocabModule`，在 `App` 构造函数里创建并 `Modules.Register`）；`App.VocabStrip`（`static TaskbarStripHost`）；`VocabSettings.Path`（`string?`）、`VocabSettings.Enabled`（`bool`），均读写 `LocalSettings.Values`（键 `Vocab.Path`、`Vocab.Enabled`）。

- [ ] **Step 1: 实现**
  - `MainWindow` 的 `Pages` 加 `["vocab"] = typeof(VocabView)`，`ModuleIcons` 加 `["vocab"]`（任选一个 Segoe Fluent Icons 字形）。
  - 页面是 `Page`，`OnNavigatedTo` 里 `e.Parameter` 是 `VocabModule`（与其他页面一致）；**页面不拥有单词条**（导航离开会销毁页面），状态来自 `module.IsRunning` 与 `App.VocabStrip.IsShowing`。
  - 控件与文案：标题 `背单词`；按钮 `选择词表…`（`FileOpenPicker`，过滤 `.json`，`InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow))`，与 `DiskAnalyzerView` 同写法）；已选路径；状态文字 `已加载 {n} 个单词`，失败时显示 `module.LoadError` 原文；`ToggleSwitch`，Header `显示在任务栏`。
  - 选文件：`module.Load(path)`，成功才 `VocabSettings.Path = path`；失败则 `App.VocabStrip.Hide()`、`VocabSettings.Enabled = false`、显示错误。
  - 开关打开：`module.Start() && App.VocabStrip.Show()`，成功则 `VocabSettings.Enabled = true`；失败则把开关扳回关闭、`module.Stop()`，显示 `App.VocabStrip.Error`（或 `module.LoadError`）。关闭：`App.VocabStrip.Hide()`、`module.Stop()`、`VocabSettings.Enabled = false`。
  - 运行中换词表：成功则单词条继续（`Load` 已重置轮换）；失败则按上面"失败"处理。
- [ ] **Step 2: 构建** — Run: `dotnet build src/PowerTools.App/PowerTools.App.csproj -p:Platform=x86` Expected: 0 个警告，0 个错误。
- [ ] **Step 3: 手动验证（请用户先 Deploy）**
  1. 左侧导航出现"背单词"；选择 `D:\Software\qwerty\qwerty-learner\public\dicts\NGSL_1.2.json` → 显示 `已加载 1755 个单词`。
  2. 打开开关 → 托盘左侧出现透明背景的单词条，第一个词是该文件的第一项（`remain  /rɪˈmen/`），背景与任务栏一致、不遮挡图标；每 5 秒换一个词（用 DPI-aware 的 `CopyFromScreen` 截两张间隔 5 秒的图对比）。
  3. 切到别的模块再切回：单词条不消失，开关仍是开。
  4. 选一个损坏的 json（例如内容 `{ bad`）→ 页面显示 `词表格式不正确…`，单词条消失，开关为关。
  5. 极长释义（把某词的 `trans` 改成 200 字）→ 单行省略号截断，不换行。
  6. 把任务栏文字主题切到浅色/深色，下个 2 秒周期文字颜色跟着变。
- [ ] **Step 4: 提交**
  ```bash
  git add src/PowerTools.App
  git commit -m "feat: add the Vocab page and register the module"
  ```

### Task 8: 启动时自动恢复 + 关闭主窗口时不残留进程

**Files:**
- Modify: `src/PowerTools.App/App.xaml.cs`、`src/PowerTools.App/MainWindow.xaml.cs`（如需要）

**Interfaces:**
- Consumes: `VocabSettings`、`App.Vocab`、`App.VocabStrip`（Task 7）。

- [ ] **Step 1: 实现**
  - primary 启动、`main.Activate()` 之后：若 `VocabSettings.Enabled` 且 `VocabSettings.Path` 非空 → `App.Vocab.Load(path)`；成功则 `Start()` 并 `App.VocabStrip.Show()`；**失败时不清除已保存的 `Enabled`**（只是这次不显示，页面打开时会显示 `LoadError`）。secondary 永远走不到这里（Task 2 已提前 `return`）。
  - 主窗口 `Closed` 时：**先** `App.VocabStrip.Hide()`（单词条自己也是 `Window`，不先关它进程会残留），再释放 `AnalyzeRequestListener` 与 `SingleInstanceGuard`，最后 `Exit()`。
- [ ] **Step 2: 全量构建与测试**
  Run: `dotnet build PowerTools.sln` 然后 `dotnet test PowerTools.sln --no-build`
  Expected: 0 个警告，0 个错误；全部测试通过（原有 595 个 + 本计划新增的）。
- [ ] **Step 3: 手动验证（请用户先 Deploy）**
  1. 单词条显示中，点主窗口右上角 × → 3 秒内 `Get-Process PowerTools.App` 无结果，单词条消失。
  2. 重新启动 → 单词条自动出现，不需要打开设置页。
  3. 把词表文件改名后重启 → 不崩溃、不显示单词条；打开"背单词"页显示 `找不到文件：…`。
  4. 单词条显示中再启动一次 → 仍只有一个进程、一个单词条，主窗口被带到前台。
  5. （可选，会让任务栏短暂消失几秒）`Stop-Process -Name explorer` → 几秒内单词条自动恢复。
- [ ] **Step 4: 提交**
  ```bash
  git add src/PowerTools.App
  git commit -m "feat: restore the vocab strip at launch and leave no process behind when the main window closes"
  ```
