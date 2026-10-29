# 下载文件夹整理模块 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增第四个模块"下载文件夹"：扫描下载文件夹，给出可预览、可撤销的整理提案，并列出久未动的文件供用户移到回收站或标记"保留，不再提醒"。

**Architecture:** 新项目 `TidyBox.Modules.Downloads`（引擎，无 UI 依赖，xUnit 覆盖）+ `TidyBox.App` 里的一个页面（手动验证）。整理与回顾共用同一个文件级扫描器和同一份 JSON 日志；`DownloadsModule` 是 UI 唯一接触的门面。`IFileRecycler`（文件版回收站）加进 `TidyBox.Core`，不改动现有的 `IRecycleBin`。

**Tech Stack:** .NET 8、WinUI 3、xUnit 2.5.3、`System.Text.Json`、`Microsoft.VisualBasic.FileIO`（回收站）。

**Spec:** `docs/superpowers/specs/2026-10-29-downloads-tidy-design.md`（执行者须先读，尤其"已决定的实现方式"）。

## Global Constraints

- 分支：从 `master` 新建 `feat/downloads-tidy`，不在 `master` 上开发。每个任务结束提交一次，信息前缀 `feat:`/`test:`/`chore:`，只 `git add` 本任务列出的路径。
- Windows 11 only；普通权限；不使用提权帮手；不联网。
- 新项目与现有模块一致：`net8.0-windows`、`ImplicitUsings`/`Nullable` 开启、引用 `TidyBox.Core`、`InternalsVisibleTo` 给对应测试项目；测试项目用 xunit 2.5.3、Microsoft.NET.Test.Sdk 17.8.0（照抄 `tests/TidyBox.Modules.DiskAnalyzer.Tests` 的 csproj）。临时目录前缀用 `tb-downloads-`，测试里自己清理。
- 模块不引用其他模块。`ByteSizeFormatter` 留在 DiskAnalyzer 模块，**只在 `TidyBox.App` 的行类里使用**（App 已引用它）；引擎只返回 `long` 字节数。（偏离 spec 里"移到 Core"的建议，原因：引擎本来就不格式化，少改一处。）
- 只处理下载文件夹**顶层文件**，以及 TidyBox 自己整理进去的分类文件夹里的文件（**只看一层**）。不进入用户自建或别的工具建的文件夹，不移动任何文件夹。
- 分类（按扩展名，不区分大小写，一个扩展名只属一个分类，**没有兜底分类**）：`images`：jpg jpeg png gif webp bmp svg ico heic heif；`documents`：pdf doc docx xls xlsx ppt pptx txt rtf odt；`archives`：zip rar 7z tar gz bz2 xz；`installers`：exe msi msix appx；`music`：mp3 wav flac aac ogg wma m4a；`videos`：mp4 avi mkv mov wmv flv webm。默认文件夹名：图片、文档、压缩包、安装包、音乐、视频。
- 安全过滤（不进提案，也不进回顾）：`.crdownload` `.part` `.tmp` `.partial` `.opdownload` 结尾；被占用（以 `FileShare.None` 打不开）；最近写入距今 **不足 5 分钟**；大小为 0；`desktop.ini`、`Thumbs.db`；重解析点（符号链接/联接点）。
- "多久没动" = `max(创建时间, 修改时间)`；**超过 90 天**算久未动，固定不可配置，不使用最后访问时间。
- 可疑 = 只检查双扩展名（倒数第二段是文档或图片扩展名，最后一段是 exe/msi/bat/cmd/scr/js/vbs/ps1/com），不做内容（魔数）检测。
- **默认勾选**：没有风险的移动提案；**默认不勾选**：可疑项、"不会动"的文件、回收类建议（内容相同的重复文件）、回顾里的全部文件。
- 同名冲突：大小相同才比 SHA-256；内容相同 → 回收类建议；内容不同 → 加后缀，**只加一次**（`name (n).ext`，n 取最小的未占用整数；源文件名已是 `name (k).ext` 时先去掉 ` (k)` 再找，绝不出现 `(1) (1)`）。**永不覆盖**任何已存在的文件。
- 日志与撤销：移动批次可撤销；回收批次只记录，由用户从回收站还原。一次操作既有移动又有回收时拆成两个批次。撤销逐项检查，不满足就跳过并说明，不覆盖。
- 回收站只用系统回收站，**没有永久删除**；"释放空间"的说法必须提示"清空回收站后才真正释放"（现有清空入口在「磁盘空间分析」页）。
- 存储：`%LOCALAPPDATA%\TidyBox\Downloads\`，其中 `journal\<batchId>.json` 与 `kept.json`；JSON 损坏按"没有数据"处理，不崩溃。
- 来源只保存/显示域名；`about:internet` 忽略；优先 `ReferrerUrl`，没有则 `HostUrl`。
- 首次打开只扫描，不移动任何文件。任何会动文件的方法，先校验每个路径都在下载文件夹之内，否则抛 `ArgumentException` 且**一个文件都不动**。
- App 是 32 位进程：新增 P/Invoke 结构体时照顾位数；`SHGetKnownFolderPath` 只用 `Guid` 与 `IntPtr`，没有结构体问题。
- 运行 App 的手动验证前，要把新 dll/exe/`resources.pri` 同步进已注册的 dev 包布局（`src\TidyBox.App\bin\x86\Debug\net8.0-windows10.0.26100.0\AppX`），普通 `dotnet build` 不会刷新它。

## Review Focus

1. 目标位置已有同名文件：内容相同、内容不同、源文件名已带 ` (1)`、同批两个文件抢同一个后缀名——都不覆盖，也不出现 `(1) (1)`（Task 4）。
2. 正在下载的文件（`.crdownload`）、被占用、刚写入、0 字节、符号链接——既不出现在提案里，也不出现在回顾里（Task 2）。
3. 撤销时目标文件已被移走、已被修改、或原位置已被新文件占用——跳过该项并说明，其余项照常恢复，永不覆盖（Task 7）。
4. 提案或日志里的路径指向下载文件夹之外（过期或被篡改）：整批拒绝、一个文件都不动（Task 6、Task 7）。
5. `kept.json`、日志文件损坏，`Zone.Identifier` 缺失、格式错误或为 `about:internet`——不崩溃，按"没有数据"处理（Task 5、Task 6、Task 8）。

---

### Task 1: 项目骨架、模块外壳与导航接入

**Files:**
- Create: `src/TidyBox.Modules.Downloads/TidyBox.Modules.Downloads.csproj`、`DownloadsModule.cs`、`DownloadsFolder.cs`
- Create: `tests/TidyBox.Modules.Downloads.Tests/TidyBox.Modules.Downloads.Tests.csproj`、`DownloadsModuleTests.cs`、`DownloadsFolderTests.cs`
- Create: `src/TidyBox.App/Views/DownloadsView.xaml`、`DownloadsView.xaml.cs`（占位页）
- Modify: `TidyBox.sln`、`src/TidyBox.App/TidyBox.App.csproj`、`src/TidyBox.App/App.xaml.cs`（`Modules.Register(new DownloadsModule())`）、`src/TidyBox.App/MainWindow.xaml.cs`（`Pages["downloads"] = typeof(DownloadsView)`、`ModuleIcons["downloads"] = ""`）

**Interfaces:**
- Produces：
  - `public sealed class DownloadsModule : IToolModule`，`ModuleId == "downloads"`，`DisplayName == "下载文件夹"`；构造 `DownloadsModule(string? downloadsFolder = null, string? storageDirectory = null)`（`recycler` 与 `clock` 参数在 Task 9 才加，因为 `IFileRecycler` 到 Task 6 才存在）；属性 `public string DownloadsFolderPath { get; }`、`public string StorageDirectory { get; }`。
  - `public static class DownloadsFolder { public static string Resolve(); }`

- [ ] **Step 1: 写失败的测试**
```csharp
[Fact] void ModuleId_And_DisplayName() => Assert.Equal(("downloads", "下载文件夹"), (m.ModuleId, m.DisplayName));
[Fact] async Task InitializeAsync_CreatesStorageDirectory_WithJournalSubfolder()   // 目录和 journal\ 都存在
[Fact] async Task InitializeAsync_StorageDirectoryIsAFile_ThrowsInvalidOperationException()
[Fact] void Resolve_ReturnsFullyQualifiedPath() => Assert.True(Path.IsPathFullyQualified(DownloadsFolder.Resolve()));
[Fact] void Module_UsesOverrideFolder() => Assert.Equal(overridePath, new DownloadsModule(downloadsFolder: overridePath).DownloadsFolderPath);
```
- [ ] **Step 2: 运行确认失败**
  Run: `dotnet test tests/TidyBox.Modules.Downloads.Tests` Expected: 编译失败（类型不存在）。
- [ ] **Step 3: 建项目并实现**
  两个 csproj 照抄 DiskAnalyzer 的；`dotnet sln TidyBox.sln add <两个 csproj> --solution-folder src`/`tests`；App csproj 加 `ProjectReference`。`DownloadsFolder.Resolve()` 用 `SHGetKnownFolderPath`（FOLDERID_Downloads `374DE290-123F-4565-9164-39C4925E467B`），失败时回退 `%USERPROFILE%\Downloads`。`InitializeAsync` 创建 `StorageDirectory` 与 `journal\`，失败抛带原因的 `InvalidOperationException`（写法照 `DiskAnalyzerModule.InitializeAsync`）。占位页只显示标题"下载文件夹"。
- [ ] **Step 4: 运行测试与构建**
  Run: `dotnet test tests/TidyBox.Modules.Downloads.Tests` Expected: 全部 PASS。
  Run: `dotnet build src/TidyBox.App/TidyBox.App.csproj -c Debug -p:Platform=x86` Expected: Build succeeded。
- [ ] **Step 5: 提交**
  `git add TidyBox.sln src/TidyBox.Modules.Downloads tests/TidyBox.Modules.Downloads.Tests src/TidyBox.App/TidyBox.App.csproj src/TidyBox.App/App.xaml.cs src/TidyBox.App/MainWindow.xaml.cs src/TidyBox.App/Views/DownloadsView.xaml src/TidyBox.App/Views/DownloadsView.xaml.cs`；`git commit -m "feat: add the downloads module shell and nav entry"`

### Task 2: 文件级扫描器与安全过滤

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Scanning/DownloadFile.cs`、`SafetyFilter.cs`、`DownloadScanner.cs`、`FileAge.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Scanning/SafetyFilterTests.cs`、`DownloadScannerTests.cs`、`FileAgeTests.cs`

**Interfaces:**
- Produces：
  - `public sealed record DownloadFile(string FullPath, long SizeBytes, DateTime CreatedUtc, DateTime ModifiedUtc, string? CategoryFolder) { public string Name => Path.GetFileName(FullPath); }`（`CategoryFolder` 为 `null` 表示在顶层）
  - `public static class SafetyFilter { public static readonly TimeSpan RecentWindow; public static bool IsEligible(FileInfo file, DateTime nowUtc); }`
  - `public sealed class DownloadScanner { public IReadOnlyList<DownloadFile> Scan(string downloadsRoot, IReadOnlyCollection<string> categoryFolderNames, DateTime nowUtc); }`（根目录不存在返回空列表，不抛）
  - `public static class FileAge { public static readonly TimeSpan StaleAfter; public static DateTime Basis(DownloadFile f); public static bool IsStale(DownloadFile f, DateTime nowUtc); public static int AgeDays(DownloadFile f, DateTime nowUtc); }`

- [ ] **Step 1: 写失败的测试**
```csharp
// SafetyFilterTests：逐项 → IsEligible == false；对照组 → true
[Theory] "a.crdownload","a.part","a.tmp","a.partial","a.opdownload","desktop.ini","Thumbs.db"  // 内容非空、写入于 1 小时前
[Fact] ZeroByteFile_NotEligible;  [Fact] WrittenFourMinutesAgo_NotEligible;  [Fact] WrittenSixMinutesAgo_Eligible
[Fact] FileOpenedWithShareNone_NotEligible;  [Fact] SymbolicLinkFile_NotEligible   // 无法建链接时用 Skip 说明
// DownloadScannerTests
[Fact] ReturnsTopLevelFiles_WithNullCategoryFolder
[Fact] ReturnsFilesOneLevelInsideCategoryFolders_WithThatFolderName   // "文档\a.pdf" → CategoryFolder == "文档"
[Fact] DoesNotDescendTwoLevels_NorIntoOtherFolders                    // "我的项目\x.pdf"、"文档\sub\y.pdf" 都不出现
[Fact] SkipsCategoryFolderThatIsAReparsePoint;  [Fact] MissingRoot_ReturnsEmpty
// FileAgeTests
[Fact] Basis_IsLaterOfCreatedAndModified   // 创建 7/22、修改 2/26 → 7/22（解压带来的旧修改时间）
[Fact] IsStale_ExactlyNinetyDays_False;  [Fact] IsStale_NinetyDaysAndOneSecond_True
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Scanning"` Expected: 编译失败。
- [ ] **Step 3: 实现上述签名** — `IsEligible` 里按"扩展名/文件名 → 大小 → 重解析点 → 最近写入 → 占用"的顺序短路，占用检测用 `File.Open(..., FileShare.None)` 并捕获 `IOException`/`UnauthorizedAccessException`；时间一律用 UTC。
- [ ] **Step 4: 运行测试** — 同上，Expected: 全部 PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads/Scanning tests/TidyBox.Modules.Downloads.Tests/Scanning`；`git commit -m "feat: scan the downloads folder with safety filters"`

### Task 3: 分类、文件夹名与可疑判断

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Rules/Categories.cs`、`CategoryNames.cs`、`Suspicion.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Rules/CategoriesTests.cs`、`CategoryNamesTests.cs`、`SuspicionTests.cs`

**Interfaces:**
- Produces：
  - `public sealed record Category(string Id, IReadOnlyList<string> Extensions);`
  - `public static class Categories { public static IReadOnlyList<Category> All { get; } public static Category? ForFile(string fileName); }`（Id 为 `images`/`documents`/`archives`/`installers`/`music`/`videos`）
  - `public sealed class CategoryNames { public CategoryNames(IReadOnlyDictionary<string, string>? overrides = null); public string FolderFor(string categoryId); public IReadOnlyCollection<string> AllFolderNames { get; } }`（默认中文名见 Global Constraints；这是文件夹名的**唯一**出处）
  - `public static class Suspicion { public static bool HasDeceptiveDoubleExtension(string fileName); }`

- [ ] **Step 1: 写失败的测试**
```csharp
[Theory] ("a.JPG","images"), ("r.pdf","documents"), ("x.7z","archives"), ("s.msi","installers"), ("s.flac","music"), ("v.webm","videos")
void ForFile_MapsExtensionCaseInsensitively
[Theory] "notes.md","a.xpi","b.apk","noextension"  void ForFile_UnknownExtension_ReturnsNull
[Fact] EveryExtension_BelongsToExactlyOneCategory
[Fact] FolderFor_DefaultsAreChinese   // images→"图片" … videos→"视频"
[Fact] FolderFor_UsesOverride;  [Fact] FolderFor_UnknownId_Throws ArgumentException
[Theory] ("invoice.pdf.exe",true), ("photo.PNG.scr",true), ("setup.exe",false), ("a.png.zip",false), ("report.pdf",false), ("x.tar.gz",false)
void HasDeceptiveDoubleExtension
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Rules"` Expected: 编译失败。
- [ ] **Step 3: 实现** — 扩展名表直接用 Global Constraints 的清单；比较用 `StringComparer.OrdinalIgnoreCase`。
- [ ] **Step 4: 运行测试** — Expected: PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads/Rules tests/TidyBox.Modules.Downloads.Tests/Rules`；`git commit -m "feat: add default categories, folder names and double-extension check"`

### Task 4: 整理提案与同名冲突处理

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Tidy/Proposal.cs`、`ProposalBuilder.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Tidy/ProposalBuilderTests.cs`

**Interfaces:**
- Consumes：`DownloadFile`、`Categories.ForFile`、`CategoryNames.FolderFor`、`Suspicion.HasDeceptiveDoubleExtension`。
- Produces：
  - `public enum ProposalKind { Move, RecycleDuplicate, Untouched }`
  - `public sealed record Proposal(DownloadFile File, ProposalKind Kind, string? CategoryId, string? DestinationPath, string Reason, bool DefaultChecked, bool IsSuspicious);`
  - `public sealed class ProposalBuilder { public ProposalBuilder(CategoryNames names); public IReadOnlyList<Proposal> Build(string downloadsRoot, IReadOnlyList<DownloadFile> files); }`（只处理 `CategoryFolder == null` 的文件；其余忽略）

- [ ] **Step 1: 写失败的测试**（用真实临时目录与真实文件）
```csharp
[Fact] Pdf_NoConflict_IsMoveToDocumentsFolder_DefaultChecked   // Destination == root\文档\a.pdf
[Fact] UnknownExtension_IsUntouched_NotChecked_NoDestination
[Fact] DoubleExtension_IsMove_ButNotChecked_AndFlaggedSuspicious   // invoice.pdf.exe → 安装包
[Fact] SameNameDifferentContent_GetsSuffix_OnlyOnce              // 目标已有 a.pdf → a (1).pdf
[Fact] SourceAlreadyNumbered_DoesNotDoubleSuffix                  // 源 "Bookings (1).xlsx"，目标已有同名且内容不同 → "Bookings (2).xlsx"，不是 "(1) (1)"
[Fact] SameNameSameContent_IsRecycleDuplicate_NotChecked         // Kind == RecycleDuplicate
[Fact] SameNameSameSizeButDifferentBytes_IsNotDuplicate           // 只比大小会误判
[Fact] TwoSourcesResolvingToSameSuffixedName_GetDistinctDestinations   // 同批占位，互不冲突
[Fact] NeverProposesOverwritingAnExistingFile                     // 所有 Move 的 Destination 都不存在
[Fact] FilesAlreadyInCategoryFolders_AreIgnored
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Tidy"` Expected: 编译失败。
- [ ] **Step 3: 实现** — 目标已存在时：先比大小，大小相同才流式计算 SHA-256；用一个"本批已占用的目标名"集合防止两个提案撞名；后缀序号取最小的未占用整数。`Reason` 为面向用户的中文短句（未匹配："没有匹配的规则，留在原处"；可疑："文件名有双扩展名，请确认"；重复："目标已有内容相同的文件"）。
- [ ] **Step 4: 运行测试** — Expected: PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads/Tidy tests/TidyBox.Modules.Downloads.Tests/Tidy`；`git commit -m "feat: build tidy proposals with safe conflict handling"`

### Task 5: "保留，不再提醒"与回顾列表

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Review/KeptStore.cs`、`ReviewList.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Review/KeptStoreTests.cs`、`ReviewListTests.cs`

**Interfaces:**
- Produces：
  - `public sealed record KeptEntry(string FullPath, long SizeBytes, DateTime ModifiedUtc);`
  - `public sealed class KeptStore { public KeptStore(string filePath); public bool IsKept(DownloadFile file); public void Keep(DownloadFile file); public bool Unkeep(string fullPath); public void Rename(string oldPath, string newPath); public IReadOnlyList<KeptEntry> All(); }`（每次修改立即写盘；路径比较不区分大小写）
  - `public sealed record ReviewResult(IReadOnlyList<DownloadFile> Stale, IReadOnlyList<DownloadFile> Kept);`
  - `public static class ReviewList { public static ReviewResult Build(IEnumerable<DownloadFile> files, KeptStore kept, DateTime nowUtc); }`（`Stale` 按大小降序；`Kept` 是当前仍存在且被保留的文件）

- [ ] **Step 1: 写失败的测试**
```csharp
[Fact] Keep_ThenIsKept_True_AndSurvivesReloadFromDisk
[Fact] FileModifiedAfterKeep_IsNoLongerKept                       // 修改时间变了 → 重新参与回顾
[Fact] FileWithDifferentSize_IsNoLongerKept
[Fact] Unkeep_RemovesEntry_AndReturnsFalseWhenMissing
[Fact] Rename_KeepsFileKeptAtNewPath                              // 整理移动后"保留"不失效
[Fact] CorruptJsonFile_BehavesAsEmpty_AndNextKeepOverwritesIt     // Review Focus 5
[Fact] Build_ExcludesKeptFromStale_AndListsThemInKept
[Fact] Build_ExcludesFilesYoungerThanNinetyDays
[Fact] Build_SortsStaleBySizeDescending
[Fact] Build_IncludesStaleFilesInsideCategoryFolders              // 整理过的旧安装包仍会被回顾
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Review"` Expected: 编译失败。
- [ ] **Step 3: 实现** — 键为"完整路径 + 大小 + 修改时间(UTC Ticks)"；序列化用 `JsonSerializer`，读取失败（`JsonException`/`IOException`）按空处理（照 `SnapshotStore` 的写法）。
- [ ] **Step 4: 运行测试** — Expected: PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads/Review tests/TidyBox.Modules.Downloads.Tests/Review`；`git commit -m "feat: add the kept store and review list"`

### Task 6: 日志与执行（移动、回收）

**Files:**
- Create: `src/TidyBox.Core/Recycling/IFileRecycler.cs`（含 `ShellFileRecycler`）
- Create: `src/TidyBox.Modules.Downloads/Journal/Batch.cs`、`JournalStore.cs`、`src/TidyBox.Modules.Downloads/Tidy/TidyExecutor.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Journal/JournalStoreTests.cs`、`Tidy/TidyExecutorTests.cs`

**Interfaces:**
- Consumes：`Proposal`、`DownloadFile`、`KeptStore.Rename`。
- Produces：
  - `public interface IFileRecycler { void Delete(string filePath); }`；`public sealed class ShellFileRecycler : IFileRecycler`（`FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)`；与 `ShellRecycleBin` 一样不对真实回收站写单元测试，调用方用假实现）
  - `public enum BatchKind { Move, Recycle }`；`public sealed record BatchEntry(string From, string? To, long SizeBytes, DateTime ModifiedUtc, string Reason);`；`public sealed record Batch(string BatchId, BatchKind Kind, DateTime CreatedUtc, IReadOnlyList<BatchEntry> Entries);`
  - `public sealed class JournalStore { public JournalStore(string directory); public void Save(Batch batch); public IReadOnlyList<Batch> LoadAll(); public void Delete(string batchId); }`（`LoadAll` 按 `CreatedUtc` 降序；损坏的文件跳过）
  - `public sealed record Failure(string Path, string Message);`；`public sealed record ExecutionResult(Batch? MoveBatch, Batch? RecycleBatch, IReadOnlyList<Failure> Failures);`
  - `public sealed class TidyExecutor { public TidyExecutor(string downloadsRoot, JournalStore journal, KeptStore kept, IFileRecycler recycler, Func<DateTime> clock); public ExecutionResult Execute(IReadOnlyList<Proposal> selected); public ExecutionResult Recycle(IReadOnlyList<DownloadFile> files); }`
  - `Execute`：`Move` 提案 → `File.Move`（不覆盖），成功的写入移动批次；`RecycleDuplicate` 提案 → 回收，写入回收批次；`Untouched` 忽略。`Recycle`：回顾里的文件 → 回收批次。

- [ ] **Step 1: 写失败的测试**
```csharp
// JournalStoreTests
[Fact] SaveThenLoadAll_RoundTripsBatch;  [Fact] LoadAll_IsNewestFirst
[Fact] LoadAll_SkipsCorruptFile_AndKeepsTheRest;  [Fact] Delete_RemovesTheBatchFile
// TidyExecutorTests（假 IFileRecycler 记录被删路径）
[Fact] Execute_MovesFile_CreatesCategoryFolder_AndRecordsMoveBatch    // Entries[0].From/To/SizeBytes/ModifiedUtc 正确
[Fact] Execute_MovePreservesTimestampsAndZoneIdentifier               // 创建/修改时间不变；用 File.WriteAllText(path+":Zone.Identifier",…) 建 ADS，移动后仍可读
[Fact] Execute_MoveAndRecycleDuplicate_ProduceTwoSeparateBatches      // MoveBatch.Kind==Move，RecycleBatch.Kind==Recycle
[Fact] Execute_NeverOverwrites_WhenDestinationAppearedAfterPlanning   // 目标被占 → Failures 有一项，源文件原封不动
[Fact] Execute_IgnoresUntouchedProposals
[Fact] Execute_RenamesKeptEntryToNewPath
[Fact] Execute_AnyPathOutsideDownloadsRoot_ThrowsArgumentException_AndMovesNothing   // 同批里有一个合法 + 一个越界
[Fact] Recycle_RecordsRecycleBatch_AndCallsRecyclerOnlyForGivenFiles
[Fact] Recycle_PathOutsideRoot_Throws_AndRecyclesNothing
[Fact] Recycle_RecyclerThrows_ReportsFailure_AndContinuesWithOthers
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Journal|FullyQualifiedName~TidyExecutor"` Expected: 编译失败。
- [ ] **Step 3: 实现** — 路径校验在任何文件操作之前，对**整批**先校验（`Path.GetFullPath` 后以 `root + 分隔符` 前缀、`OrdinalIgnoreCase`）；每个批次一个文件 `<BatchId>.json`，`BatchId` 用 `Guid.NewGuid().ToString("N")`；成功的条目才进批次；没有成功条目时对应批次为 `null`、不写文件。
- [ ] **Step 4: 运行测试** — Expected: PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Core/Recycling src/TidyBox.Modules.Downloads/Journal src/TidyBox.Modules.Downloads/Tidy/TidyExecutor.cs tests/TidyBox.Modules.Downloads.Tests/Journal tests/TidyBox.Modules.Downloads.Tests/Tidy/TidyExecutorTests.cs`；`git commit -m "feat: execute tidy moves and recycles with a journal"`

### Task 7: 按批次撤销

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Tidy/TidyUndo.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Tidy/TidyUndoTests.cs`

**Interfaces:**
- Consumes：`JournalStore`、`KeptStore.Rename`、`Batch`/`BatchEntry`。
- Produces：
  - `public enum UndoSkipReason { TargetMissing, TargetChanged, OriginalOccupied }`
  - `public sealed record UndoSkip(BatchEntry Entry, UndoSkipReason Reason);`
  - `public sealed record UndoResult(int Restored, IReadOnlyList<UndoSkip> Skipped);`
  - `public sealed class TidyUndo { public TidyUndo(string downloadsRoot, JournalStore journal, KeptStore kept); public UndoResult Undo(string batchId); }`
  - 恢复成功的条目从批次里删除；批次空了就 `journal.Delete`；`Kind == Recycle` 或批次不存在 → `InvalidOperationException`；条目里任一路径越界 → `ArgumentException` 且什么都不恢复。

- [ ] **Step 1: 写失败的测试**（先用 `TidyExecutor` 真实移动出一个批次再撤销）
```csharp
[Fact] Undo_RestoresFilesToOriginalPaths_AndDeletesEmptyBatch
[Fact] Undo_RestoresTimestamps_Unchanged                          // 创建/修改时间与整理前一致
[Fact] Undo_TargetDeletedByUser_SkipsWithTargetMissing_OthersStillRestored
[Fact] Undo_TargetModifiedSinceMove_SkipsWithTargetChanged        // 大小或修改时间变了
[Fact] Undo_OriginalPathNowOccupied_SkipsWithOriginalOccupied_AndDoesNotOverwrite   // 原位置的新文件内容不变
[Fact] Undo_PartialRestore_KeepsOnlySkippedEntriesInBatch
[Fact] Undo_RenamesKeptEntryBackToOriginalPath
[Fact] Undo_RecycleBatch_Throws;  [Fact] Undo_UnknownBatchId_Throws
[Fact] Undo_JournalEntryOutsideRoot_Throws_AndRestoresNothing     // 手工改坏日志 JSON 模拟
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~TidyUndo"` Expected: 编译失败。
- [ ] **Step 3: 实现** — 先校验全部路径，再逐项检查"目标存在 → 大小与修改时间等于记录 → 原位置空闲"，满足才 `File.Move(to, from)`；修改过的批次用同一个 `BatchId` 重写。
- [ ] **Step 4: 运行测试** — Expected: PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads/Tidy/TidyUndo.cs tests/TidyBox.Modules.Downloads.Tests/Tidy/TidyUndoTests.cs`；`git commit -m "feat: undo tidy batches without overwriting anything"`

### Task 8: 来源域名

**Files:**
- Create: `src/TidyBox.Modules.Downloads/Source/ZoneIdentifierParser.cs`、`SourceReader.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/Source/ZoneIdentifierParserTests.cs`、`SourceReaderTests.cs`

**Interfaces:**
- Produces：
  - `public static class ZoneIdentifierParser { public static string? ParseDomain(string content); }`
  - `public static class SourceReader { public static string? TryGetDomain(string filePath); }`（读 `filePath + ":Zone.Identifier"`；没有该数据流或任何读取异常 → `null`）

- [ ] **Step 1: 写失败的测试**
```csharp
[Fact] ReferrerUrl_IsPreferredOverHostUrl           // Referrer github.com，Host release-assets.githubusercontent.com → "github.com"
[Fact] OnlyHostUrl_FallsBackToIt
[Fact] AboutInternet_IsIgnored_ReturnsNull           // HostUrl=about:internet
[Fact] ReferrerAboutInternet_FallsBackToHostUrl
[Theory] "", "garbage", "[ZoneTransfer]\r\nZoneId=3", "HostUrl=not a url", "HostUrl=ftp://x.com/f"  → null   // Review Focus 5
[Fact] UrlWithQueryAndToken_ReturnsOnlyTheHost       // 不泄露路径、查询串；结果为小写域名，去掉端口
[Fact] TryGetDomain_ReadsRealAlternateStream         // File.WriteAllText(p+":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/a")
[Fact] TryGetDomain_FileWithoutStream_ReturnsNull;  [Fact] TryGetDomain_MissingFile_ReturnsNull
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Source"` Expected: 编译失败。
- [ ] **Step 3: 实现** — 逐行找 `ReferrerUrl=`、`HostUrl=`；只接受 `http`/`https` 的绝对 URL，返回 `Uri.Host`（小写）。
- [ ] **Step 4: 运行测试** — Expected: PASS。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads/Source tests/TidyBox.Modules.Downloads.Tests/Source`；`git commit -m "feat: read the source domain from Zone.Identifier"`

### Task 9: 模块门面与端到端测试

**Files:**
- Modify: `src/TidyBox.Modules.Downloads/DownloadsModule.cs`
- Create: `src/TidyBox.Modules.Downloads/DownloadsReport.cs`
- Test: `tests/TidyBox.Modules.Downloads.Tests/DownloadsModuleWorkflowTests.cs`

**Interfaces:**
- Consumes：Task 2–8 的全部类型。构造函数在 Task 1 的基础上扩展为 `DownloadsModule(string? downloadsFolder = null, string? storageDirectory = null, IFileRecycler? recycler = null, Func<DateTime>? clock = null)`（Task 1 的既有测试保持通过）。
- Produces（UI 只用这些）：
  - `public sealed record DownloadsReport(string Folder, long TotalBytes, long StaleBytes, IReadOnlyList<Proposal> Proposals, IReadOnlyList<DownloadFile> Stale, IReadOnlyList<DownloadFile> Kept, IReadOnlyDictionary<string, string> SourceDomains);`（`TotalBytes` 是本次扫描到的合格文件的大小之和，不含文件夹；`SourceDomains` 键为 `FullPath`，只含有域名的文件）
  - `Task<DownloadsReport> ScanAsync(CancellationToken ct = default)`；`Task<ExecutionResult> ApplyAsync(IReadOnlyList<Proposal> selected, CancellationToken ct = default)`；`Task<ExecutionResult> RecycleAsync(IReadOnlyList<DownloadFile> files, CancellationToken ct = default)`；`void Keep(DownloadFile file)`；`bool Unkeep(string fullPath)`；`IReadOnlyList<Batch> GetHistory()`；`UndoResult Undo(string batchId)`
  - `recycler` 为空时用 `ShellFileRecycler`；`clock` 为空时用 `DateTime.UtcNow`。

- [ ] **Step 1: 写失败的测试**
```csharp
[Fact] ScanAsync_ModifiesNothing                                    // 扫描前后目录的文件清单与每个文件的内容哈希完全相同；日志目录为空
[Fact] ScanAsync_ReportsProposals_Stale_AndSourceDomains
[Fact] ApplyAsync_MovesOnlyTheSelectedProposals
[Fact] ApplyThenUndo_RestoresTheFolderToItsOriginalState            // 内容、时间戳都一致
[Fact] TidiedOldInstaller_StillShowsUpInReview_WithItsOriginalAge   // 整理后移入"安装包"，再次扫描仍在 Stale 里，年龄不变
[Fact] KeptFile_DisappearsFromStale_AppearsInKept_AndSurvivesBeingTidied   // 先 Keep 再 Apply 移动
[Fact] RecycleAsync_UsesRecycler_AndAppearsInHistoryAsRecycleBatch
[Fact] Apply_WithProposalOutsideFolder_Throws_AndChangesNothing
```
- [ ] **Step 2: 运行确认失败** — `dotnet test tests/TidyBox.Modules.Downloads.Tests --filter "FullyQualifiedName~Workflow"` Expected: FAIL。
- [ ] **Step 3: 实现** — `ScanAsync` 在 `Task.Run` 里：`DownloadScanner` → `ProposalBuilder` + `ReviewList.Build` + `SourceReader`；扫描方法不写任何文件。`Apply/Recycle/Undo` 转给 Task 6、7 的类；路径校验已在它们内部。
- [ ] **Step 4: 运行全部测试** — `dotnet test tests/TidyBox.Modules.Downloads.Tests` Expected: 全部 PASS；再跑 `dotnet test` 确认既有测试都未受影响。
- [ ] **Step 5: 提交** — `git add src/TidyBox.Modules.Downloads tests/TidyBox.Modules.Downloads.Tests`；`git commit -m "feat: expose the downloads engine through DownloadsModule"`

### Task 10: 页面（布局 B）与手动验证

**Files:**
- Modify: `src/TidyBox.App/Views/DownloadsView.xaml`、`DownloadsView.xaml.cs`
- Create: `src/TidyBox.App/Views/ProposalRow.cs`、`StaleRow.cs`、`BatchRow.cs`

**Interfaces:**
- Consumes：`DownloadsModule`（Task 9）；`ByteSizeFormatter.Format`（DiskAnalyzer，仅在行类里用）；`CategoryNames`。行类模式照 `OrphanFolderRow`（`IsChecked`、`Name`、`Detail`）。

- [ ] **Step 1: 搭页面**，从上到下（`ScrollViewer` 里的 `StackPanel`，控件风格照 `VocabView`）：
  1. 标题 + 路径 + 汇总行："文件合计 {TotalBytes}，超过 90 天没动的 {StaleBytes}"。
  2. **① 可以整理的（N）**：`ListView`（虚拟化）按目标文件夹分组；每行 CheckBox + 名称 + `Detail`（大小 · 来源域名 · `Reason`）；"不会动"的单独一组、不可勾选；底部按钮"检查所选操作（n）"。点击后弹确认 `ContentDialog`（将移动 n 项、共 X；有回收类建议时分开写"将移到回收站 m 项"），确认才执行 `ApplyAsync`。
  3. **② 久未动的（N）· 可释放 X**：默认按大小降序、**一项都不勾选**；类型筛选 `ComboBox` + "全选当前筛选结果"；两个按钮"移到回收站（n）"（确认框写明"移到回收站不会立刻释放空间，清空回收站后才真正释放；可在「磁盘空间分析」页清空"）和"保留，不再提醒"。
  4. 折叠区"已保留（N）"，每行"放回"（`Unkeep`）；折叠区"历史"，移动批次有"撤销"，回收批次显示"请从回收站还原"；撤销后弹出结果摘要，列出被跳过的项和原因。
  5. 扫描中显示进度并可取消；`ScanAsync` 失败时显示错误，不崩溃。首次进入页面只扫描，不改任何文件。
- [ ] **Step 2: 构建** — `dotnet build src/TidyBox.App/TidyBox.App.csproj -c Debug -p:Platform=x86` Expected: Build succeeded。
- [ ] **Step 3: 手动验证**（先同步 dev 包布局，见 Global Constraints；用**测试用的下载文件夹副本**，不要直接拿真实下载文件夹试）
  - 打开页面后目录**没有任何变化**；"不会动"组里的文件不可勾选；可疑文件和重复文件默认未勾选。
  - 整理一批 → 目标文件夹出现；"历史"出现一条移动批次；点"撤销"后文件回到原位，时间戳不变。
  - 把一个已整理的旧安装包（把修改和创建时间设为 100 天前）放进"安装包"文件夹 → 回顾里仍出现。
  - 对一个回顾文件点"保留，不再提醒" → 消失并出现在"已保留"；点"放回"后重新出现。
  - 移到回收站 → 文件出现在系统回收站；历史里是回收批次，没有"撤销"按钮。
  - 一个下载中（`.crdownload`）的文件和一个被另一个程序占用的文件都不出现。
  - 关闭再打开应用：已保留与历史仍在。
- [ ] **Step 4: 提交** — `git add src/TidyBox.App/Views/DownloadsView.xaml src/TidyBox.App/Views/DownloadsView.xaml.cs src/TidyBox.App/Views/ProposalRow.cs src/TidyBox.App/Views/StaleRow.cs src/TidyBox.App/Views/BatchRow.cs`；`git commit -m "feat: add the downloads page"`
