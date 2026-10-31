# 文件整理：多文件夹 × 多强度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让文件整理页可以保存多个文件夹和多种整理强度，两者多对多，每个文件夹记住自己上次用的强度。

**Architecture:** 新增纯逻辑类 `TidyProfiles`（`TidyBox.Modules.Downloads.Profiles`），持有强度列表、文件夹列表、文件夹→强度名配对和当前文件夹，保存为 `profiles.json`。`DownloadsModule` 拥有一个实例，并在切换文件夹/强度时把结果应用到已有的 `SetFolder` / `SetOptions`。界面只做显示和调用。

**Tech Stack:** .NET 8、WinUI 3、xUnit 2.5.3、System.Text.Json。

**Spec:** `docs/superpowers/specs/2026-10-31-tidy-profiles-design.md`

## Global Constraints

- 强度范围沿用 `TidyOptions.Create`：天数 1–3650，分钟 0–1440。
- 强度名：去掉首尾空白后非空、最长 20 个字，不区分大小写不可重复。至少保留一个强度；首次使用的唯一强度叫“标准”，值为 `TidyOptions.Default`（90 天 / 5 分钟）。
- 下载文件夹（`DownloadsFolder.Resolve()`）始终在列表里，不能移除。
- 加入列表的文件夹必须通过 `FolderChoice.Validate`；路径存成 `Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))`，不区分大小写去重。
- 移除文件夹、删除强度都不触碰磁盘上的任何用户文件。
- `PathGuard`、`TidyExecutor`、`TidyUndo`、日志与“已保留”的行为一律不改。
- 注释与现有代码同风格：英文，写“为什么”，不写显而易见的事。界面文案用简体中文。

## Review Focus

- 删除被多个文件夹使用的强度：这些文件夹都要落到剩下的第一个强度，不能留下指向不存在名字的配对。
- 列表里的文件夹已被用户在磁盘上删掉：选它要给出理由而不是崩溃；启动时上次用的文件夹不存在要回到下载文件夹。
- 手改坏或损坏的 `profiles.json`（坏 JSON、未知强度名、重复文件夹、超范围数值、没有任何强度）：回到可用的默认，不抛异常。
- 同一个文件夹以不同大小写或带末尾斜杠再次添加：只出现一条。
- 下载文件夹和它的子文件夹同时在列表里：切换后引擎只对当前那一个动手。

---

### Task 1: 强度列表

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Profiles/TidyStrength.cs`
- Create: `src/TidyBox.Modules.Downloads/Profiles/TidyProfiles.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Profiles/TidyProfilesStrengthTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TidyStrength(string Name, TidyOptions Options);`
  - `public sealed class TidyProfiles` with `public const string DefaultStrengthName = "标准"; public const int MaxNameLength = 20;`
  - `public static TidyProfiles Load(string filePath, string defaultFolder)`（本任务先只做“文件不存在 → 只有“标准”强度”，持久化在 Task 3）
  - `public IReadOnlyList<TidyStrength> Strengths { get; }`
  - `public void AddStrength(string name, TidyOptions options)` — `ArgumentException`：名称空白、超长、重名
  - `public void UpdateStrength(string name, TidyOptions options)` — `ArgumentException`：不存在
  - `public void DeleteStrength(string name)` — `InvalidOperationException`：只剩一个；`ArgumentException`：不存在

- [ ] **Step 1: Write the failing tests**（用 `Path.GetTempPath()` 下的临时文件路径，`Load` 此时不落盘）

```csharp
[Fact] public void A_new_profile_set_has_one_strength_named_standard_with_the_default_thresholds()
// Strengths is [("标准", TidyOptions.Default)]
[Fact] public void AddStrength_appends_and_trims_the_name()            // AddStrength("  严格 ", Create(30,0)) -> Strengths[1].Name == "严格"
[Theory][InlineData("")][InlineData("   ")][InlineData("一二三四五六七八九十一二三四五六七八九十一")] // 21 chars
public void AddStrength_refuses_a_blank_or_too_long_name(string name)   // ArgumentException, Strengths.Count stays 1
[Fact] public void AddStrength_refuses_a_name_that_exists_ignoring_case()  // "abc" then "ABC" -> ArgumentException
[Fact] public void UpdateStrength_replaces_the_thresholds_of_that_name()   // Update("标准", Create(30,1)) -> Strengths[0].Options.StaleDays == 30
[Fact] public void UpdateStrength_of_an_unknown_name_is_refused()
[Fact] public void DeleteStrength_removes_it()                              // after adding "严格"
[Fact] public void DeleteStrength_refuses_to_remove_the_last_one()          // InvalidOperationException
```

- [ ] **Step 2: Run to verify they fail**
Run: `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter FullyQualifiedName~TidyProfilesStrengthTests`
Expected: 编译失败（类型不存在）。

- [ ] **Step 3: Implement** `TidyStrength` and the strength half of `TidyProfiles` per the signatures above. Name comparison `StringComparison.OrdinalIgnoreCase`; names stored trimmed.

- [ ] **Step 4: Run to verify they pass** — same command. Expected: PASS（8 项，Theory 计 3 项）。

- [ ] **Step 5: Commit** — `git add src/TidyBox.Modules.Downloads/Profiles tests/TidyBox.Modules.Downloads.Tests/Profiles; git commit -m "feat: keep several tidy strengths"`

### Task 2: 文件夹列表与配对

**Files:**
- Modify: `src/TidyBox.Modules.Downloads/Profiles/TidyProfiles.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Profiles/TidyProfilesFolderTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `TidyProfiles`、`TidyStrength`；`FolderChoice.Validate(string) : string?`
- Produces:
  - `public IReadOnlyList<string> Folders { get; }` — 第一项恒为 `defaultFolder`（规范化后），其后按添加顺序
  - `public string SelectedFolder { get; }` — 初始为 `defaultFolder`
  - `public TidyStrength StrengthFor(string folder)` — 配对的强度；没有配对时是第一个强度
  - `public void AddFolder(string path)` — `ArgumentException(message = FolderChoice.Validate 的理由)`；已存在则不重复添加
  - `public bool RemoveFolder(string path)` — 默认文件夹或不在列表中返回 false；移除的是当前选中的则回到默认文件夹；同时去掉它的配对
  - `public void Select(string folder)` — 不在列表中 → `ArgumentException`
  - `public void SetStrength(string folder, string strengthName)` — 文件夹或强度不存在 → `ArgumentException`
  - `DeleteStrength`（Task 1）扩展：配对到被删强度的文件夹改配第一个剩下的强度

- [ ] **Step 1: Write the failing tests**（真实临时目录；`defaultFolder` 也是一个临时目录）

```csharp
[Fact] public void The_default_folder_is_first_and_selected()
[Fact] public void AddFolder_adds_a_valid_folder_once_even_with_other_casing_or_a_trailing_slash()
[Fact] public void AddFolder_refuses_a_folder_FolderChoice_refuses()          // 用 Environment.SpecialFolder.Windows 路径，消息等于 FolderChoice.Validate 的返回值
[Fact] public void The_same_strength_can_serve_two_folders_and_one_folder_can_change_strength()
// 两个文件夹 SetStrength(…, "标准") 后 StrengthFor 都是标准；把其中一个改成 "严格" 后另一个不变
[Fact] public void A_folder_remembers_its_strength_while_another_is_selected()
[Fact] public void StrengthFor_without_a_pairing_is_the_first_strength()
[Fact] public void RemoveFolder_forgets_the_pairing_and_selects_the_default_when_it_was_selected()
[Fact] public void RemoveFolder_refuses_the_default_folder_and_unknown_folders()  // 返回 false
[Fact] public void Select_refuses_a_folder_that_is_not_in_the_list()
[Fact] public void DeleteStrength_moves_every_folder_that_used_it_to_the_first_remaining_strength()  // 两个文件夹都用“严格”，删除后都是“标准”
[Fact] public void A_nested_folder_and_its_parent_are_two_separate_entries()      // Downloads 与 Downloads\子目录
```

- [ ] **Step 2: Run to verify they fail**
Run: `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter FullyQualifiedName~TidyProfilesFolderTests`
Expected: FAIL（成员不存在）。

- [ ] **Step 3: Implement** the folder list, pairing map (`Dictionary<string,string>` with `StringComparer.OrdinalIgnoreCase`), and the `DeleteStrength` extension. The default folder is normalized but not validated.

- [ ] **Step 4: Run to verify they pass**，并跑 Task 1 的测试确认没被带坏。Expected: PASS。

- [ ] **Step 5: Commit** — `git commit -m "feat: keep several folders, each with its own strength"`

### Task 3: 保存、读取与迁移

**Files:**
- Modify: `src/TidyBox.Modules.Downloads/Profiles/TidyProfiles.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Profiles/TidyProfilesStoreTests.cs`

**Interfaces:**
- Consumes: Task 1–2。
- Produces:
  - `TidyProfiles.Load(filePath, defaultFolder)` 现在会读文件；每次改动（Add/Update/Delete Strength、Add/Remove Folder、Select、SetStrength）立即写盘，写之前 `Directory.CreateDirectory`。
  - 文件内容为 JSON：`{ "strengths":[{"name","staleDays","recentMinutes"}], "folders":[path], "selected":path, "pairs":{path:name} }`。
  - 读取时容错：坏 JSON、读取 IO 错误 → 当作不存在；没有任何有效强度 → 补上“标准”；强度数值越界或名字重复的条目丢掉；`folders` 里重复或不存在于磁盘不检查（磁盘检查在使用时）；配对指向未知强度/未知文件夹的丢掉；`selected` 不在列表里 → 默认文件夹。读到的文件夹列表里不含默认文件夹时仍保证它排第一。

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Everything_survives_a_reload()   // 加强度、加文件夹、配对、选中，再 Load 同一文件，四样都在
[Fact] public void A_damaged_file_gives_the_first_run_state()                 // 内容 "{not json"
[Fact] public void A_file_with_no_strengths_gets_the_standard_one()
[Fact] public void Out_of_range_and_duplicate_strengths_are_dropped()          // staleDays 0、99999、重名
[Fact] public void A_pairing_to_an_unknown_strength_or_folder_is_dropped()
[Fact] public void A_selected_folder_that_is_not_listed_falls_back_to_the_default()
[Fact] public void The_default_folder_stays_first_even_if_the_file_lists_it_elsewhere()
[Fact] public void Each_change_is_on_disk_at_once()   // 每次操作后读文件内容能看到
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter FullyQualifiedName~TidyProfilesStoreTests`；Expected: FAIL。

- [ ] **Step 3: Implement** persistence with `System.Text.Json` and the tolerant load, in the style of `KeptStore` (catch `JsonException` / `IOException`, non-atomic write is accepted as there).

- [ ] **Step 4: Run all Profiles tests** — `--filter FullyQualifiedName~Profiles`；Expected: PASS。

- [ ] **Step 5: Commit** — `git commit -m "feat: save the folders and strengths between runs"`

### Task 4: 接入 DownloadsModule

**Files:**
- Modify: `src/TidyBox.Modules.Downloads/DownloadsModule.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/DownloadsModuleProfilesTests.cs`

**Interfaces:**
- Consumes: `TidyProfiles`（Task 1–3）。
- Produces on `DownloadsModule`:
  - `public TidyProfiles Profiles { get; }` — 文件 `Path.Combine(StorageDirectory, "profiles.json")`，默认文件夹 `DownloadsFolder.Resolve()`
  - `public void UseFolder(string path)` — 通过 `Profiles.AddFolder` 校验（失败抛 `ArgumentException`，当前文件夹不变）、`Profiles.Select`、`SetFolder`、再 `SetOptions(Profiles.StrengthFor(folder).Options)`
  - `public void UseStrength(string strengthName)` — `Profiles.SetStrength(DownloadsFolderPath, name)` 后 `SetOptions`
  - `public void RefreshOptions()` — `SetOptions(Profiles.StrengthFor(DownloadsFolderPath).Options)`，给设置页改了强度之后调用
  - `public void RemoveFolder(string path)` — `Profiles.RemoveFolder`；移除的是当前文件夹则 `UseFolder(默认文件夹)`
  - 构造函数：`downloadsFolder` / `options` 参数保留，含义变为“启动时的显式覆盖，并记下来”：`downloadsFolder` 非空且有效 → `UseFolder`（无效则忽略，不抛）；`options` 非空 → 写入当前文件夹所配强度（`UpdateStrength`）。两者都为空时按 `Profiles.SelectedFolder` 启动；若该文件夹已不存在 → 回到默认文件夹。

- [ ] **Step 1: Write the failing tests**（临时 storage 目录与临时文件夹；用现有的假 recycler 约定，参照 `DownloadsModuleTests.cs` 的构造方式）

```csharp
[Fact] public void UseFolder_switches_the_folder_and_applies_that_folders_strength()
[Fact] public void UseFolder_with_a_refused_folder_throws_and_keeps_the_current_folder_and_thresholds()
[Fact] public void UseStrength_changes_the_thresholds_in_force_and_is_remembered_for_that_folder_only()
[Fact] public void RefreshOptions_picks_up_an_edited_strength()
[Fact] public void Removing_the_current_folder_goes_back_to_the_default_folder()
[Fact] public void A_new_module_on_the_same_storage_starts_with_the_last_folder_and_its_strength()
[Fact] public void A_last_folder_that_no_longer_exists_starts_on_the_default_folder()
[Fact] public void The_constructor_arguments_are_recorded_as_the_starting_folder_and_strength()   // 迁移路径
[Fact] public void After_switching_folder_the_history_shows_only_that_folders_batches()           // 沿用 GetHistory 过滤，回归测试
```

- [ ] **Step 2: Run to verify they fail** — `--filter FullyQualifiedName~DownloadsModuleProfilesTests`；Expected: FAIL。

- [ ] **Step 3: Implement** the members above inside the existing `_gate` lock where they touch `_executor`/`_undo` (reuse `SetFolder`/`SetOptions`; do not duplicate their body).

- [ ] **Step 4: Run the whole Downloads suite** — `dotnet test tests/TidyBox.Modules.Downloads.Tests`；Expected: 全部 PASS（原 166 + 新增），0 失败。

- [ ] **Step 5: Commit** — `git commit -m "feat: let the module switch between saved folders and strengths"`

### Task 5: 界面、迁移与文档

**Files:**
- Create: `src/TidyBox.App/LegacyTidySettings.cs`
- Delete: `src/TidyBox.App/TidyFolderSettings.cs`, `src/TidyBox.App/TidyOptionsSettings.cs`
- Modify: `src/TidyBox.App/App.xaml.cs:44`、`Views/DownloadsView.xaml(.cs)`、`Views/SettingsView.xaml(.cs)`、`docs/privacy-policy.md`
- Test: App 项目没有自动化测试；本任务用构建 + 手动核对（见 Step 5）。

**Interfaces:**
- Consumes: Task 4 的 `DownloadsModule.Profiles / UseFolder / UseStrength / RefreshOptions / RemoveFolder`。
- Produces: `internal static class LegacyTidySettings { public static (string? Folder, TidyOptions? Options) Take(); }` — 读出旧的三个 LocalSettings 键（越界值当作没有；文件夹不存在当作没有），读完立刻删掉这三个键。

- [ ] **Step 1: App.xaml.cs** — `var legacy = LegacyTidySettings.Take();` 然后 `new DownloadsModule(downloadsFolder: legacy.Folder, options: legacy.Options)`；删除两个旧设置类。

- [ ] **Step 2: DownloadsView** — 顶部加“文件夹”下拉框（项为 `Profiles.Folders`，下载文件夹显示为“下载文件夹”）和“整理强度”下拉框（项为强度名，旁边小字显示“N 天 · M 分钟”）；“更改文件夹…”改名“添加文件夹…”，选后调用 `UseFolder`；加“从列表移除”（下载文件夹时隐藏）；删除“恢复为下载文件夹”按钮及其处理。选择变化时 `_scanCts` 取消、调用 `UseFolder` / `UseStrength` 后 `ScanAsync`；失败（`ArgumentException`）把理由写进 `ResultText` 并把下拉框还原到当前文件夹。填充下拉框时用 `_rendering` 之类的现有标志避免触发 SelectionChanged。`ShowThresholds()` 沿用，选强度后刷新。页面说明文字里“可以点‘更改文件夹…’”改成新按钮名。

- [ ] **Step 3: SettingsView** — “文件整理”卡片改成管理强度：下拉框选择要编辑的强度（初始为当前文件夹所配）、两个现有的 NumberBox 编辑所选强度（改动即保存：`Profiles.UpdateStrength` 后 `RefreshOptions`）、“新建强度…”（ContentDialog 输入名称，初始值取当前数字框的值；重名/空/超长在对话框里提示）、“删除”（只剩一个时禁用；确认对话框）、“恢复默认值”（把所选强度改回 90 天 / 5 分钟）。

- [ ] **Step 4: privacy-policy.md** — “文件整理会在你的电脑上保存什么”里加一条：你添加的文件夹、整理强度，以及每个文件夹上次用的强度（`profiles.json`，同一个文件夹，不上传）。

- [ ] **Step 5: Verify**
Run: `dotnet build src/TidyBox.App -c Debug -p:Platform=x86` → 0 warning 0 error；`dotnet test` 全解决方案各测试项目全绿。
手动（需先结束 `TidyBox*` 进程，把构建出的变更文件同步进开发包 AppX 目录，跳过 `AppxManifest.xml`）：1) 首次启动沿用原来的文件夹和天数；2) 添加两个文件夹、新建“严格 30 天”，A 用严格、B 用标准，来回切换各自记住；3) 删掉“严格”，A 变回标准；4) 添加系统文件夹被拒绝并说明理由；5) 在资源管理器里删掉一个已列出的文件夹后选它，页面给出理由不崩溃；6) 重启后列表与选择都在。

- [ ] **Step 6: Commit** — `git add src docs/privacy-policy.md; git commit -m "feat: choose among saved folders and strengths on the tidy page"`（只加这些路径，`docs/` 下无关的未跟踪文件不要带上）
