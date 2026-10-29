# PC 维护工具箱 —— 磁盘分析模块 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现磁盘分析模块作为一个 `IToolModule`：标准递归扫描生成共享的扫描快照数据模型、手动触发的历史对比（本次vs上次扫描，找出增长最多的文件夹）、Steam游戏库解析（占用空间+多久没动过+一键跳转卸载）。非Steam大型应用的兜底识别不在本计划范围内（设计文档里标注为V1可不做）。

**Architecture:** 新增 `TidyBox.Modules.DiskAnalyzer` 类库，依赖脚手架计划里的 `TidyBox.Core`（用其中的 `IToolModule`）。内部按引擎层/UI层分离：`DirectoryScanner`（扫描）、`SnapshotStore`（快照持久化+对比）、`SteamLibraryParser`（Steam库解析）都是纯逻辑、可单测的类，不依赖WinUI3；最后一个任务才接WinUI3视图，且视图层只做手动验证（XAML渲染不是TDD友好的）。三个引擎组件共用同一套 `DiskScanEntry`/`DiskScanSnapshot` 数据模型，这是贯穿整个模块的核心契约。

**Tech Stack:** .NET 8, xUnit, System.IO（标准递归遍历，不做NTFS MFT直读——设计文档明确这是V1之后才考虑的优化）, System.Text.Json（快照持久化，用JSON文件而不是SQLite——这是本计划的决定：V1只存"每个顶层文件夹的聚合大小"而不是每个文件的记录，数据量很小，不需要数据库引擎，YAGNI）

**Spec:** `docs/superpowers/specs/2026-10-24-pc-maintenance-toolbox-design.md`

**Depends on:** `docs/superpowers/plans/2026-10-24-pc-toolbox-scaffolding-and-shared-infra.md`（Task 1的 `IToolModule` 接口；本计划不依赖该计划的Task 2-6，提权客户端对本模块没有任何用处）

## Global Constraints

- 技术栈固定为 .NET 8，本模块不含UI框架相关构建except最后一个任务的WinUI3视图。
- 只支持 Windows 11，不做 Windows 10 兼容。
- 本模块的所有操作都不需要管理员权限——不依赖脚手架计划的 `ElevationClient`。
- 不搭建对外插件分发/沙箱基础设施；内部按"松耦合组件、清晰接口、引擎层与UI层分离"设计，三个引擎组件共享同一套扫描快照数据模型。
- 目标市场为简体中文/中国区，用户可见文案用中文；内部类型/字段命名用英文（跟脚手架计划的既有代码风格一致）。

## Review Focus

- 扫描过程中遇到无权限访问的子文件夹（比如系统保护目录）：扫描器必须跳过并继续，不能让整个扫描因为一个文件夹访问被拒而中断失败。
- 两次快照之间，某个文件夹被整个删除：对比逻辑要把它正确地标记为"消失/减少"，不能因为找不到对应项而抛异常。
- Steam库解析遇到格式异常或字段缺失的 `.acf` 文件（比如游戏正在更新、文件不完整）：解析器要跳过这一条记录，不能让整个库解析因为一个损坏文件而失败。
- `ModuleId`/`DisplayName` 等 `IToolModule` 实现：必须在 `InitializeAsync()` 里完成所有可能失败的初始化工作（比如检测Steam是否安装），失败时要抛出清晰的异常而不是让模块处于"看起来初始化成功但内部状态是坏的"状态——这样脚手架计划Task 5里"优雅降级"的逻辑才能正确捕获。
- Steam未安装的电脑：Steam游戏视图要能优雅处理"没有找到Steam"的情况（显示空列表或提示，不是抛异常崩溃整个模块）。

---

## Task 1: 共享扫描数据模型 + 基础递归扫描器

**Files:**
- Create: `src/TidyBox.Modules.DiskAnalyzer/TidyBox.Modules.DiskAnalyzer.csproj`
- Create: `src/TidyBox.Modules.DiskAnalyzer/Scanning/DiskScanEntry.cs`
- Create: `src/TidyBox.Modules.DiskAnalyzer/Scanning/DiskScanSnapshot.cs`
- Create: `src/TidyBox.Modules.DiskAnalyzer/Scanning/DirectoryScanner.cs`
- Test: `tests/TidyBox.Modules.DiskAnalyzer.Tests/TidyBox.Modules.DiskAnalyzer.Tests.csproj`
- Test: `tests/TidyBox.Modules.DiskAnalyzer.Tests/Scanning/DirectoryScannerTests.cs`

**Interfaces:**
- Produces:
  - `record DiskScanEntry(string Path, long SizeBytes, DateTime LastModifiedUtc, int ItemCount)` —— 代表扫描结果里的一个顶层文件夹聚合条目（本计划V1只聚合到"扫描根目录下的直接子文件夹"这一层，不逐文件保存，保持数据量小）
  - `record DiskScanSnapshot(string RootPath, DateTime ScannedAtUtc, IReadOnlyList<DiskScanEntry> Entries)`
  - `class DirectoryScanner { Task<DiskScanSnapshot> ScanAsync(string rootPath, CancellationToken ct = default); }` —— 并行遍历`rootPath`下的每个直接子文件夹，递归求和得到`SizeBytes`/`ItemCount`/`LastModifiedUtc`（取该子树里最新的文件修改时间），单个子文件夹访问被拒不影响其它子文件夹的扫描结果（对应Review Focus第1条）

- [ ] **Step 1: 项目骨架**

```bash
dotnet new classlib -n TidyBox.Modules.DiskAnalyzer -o src/TidyBox.Modules.DiskAnalyzer -f net8.0
dotnet new xunit -n TidyBox.Modules.DiskAnalyzer.Tests -o tests/TidyBox.Modules.DiskAnalyzer.Tests -f net8.0
dotnet sln TidyBox.sln add src/TidyBox.Modules.DiskAnalyzer/TidyBox.Modules.DiskAnalyzer.csproj tests/TidyBox.Modules.DiskAnalyzer.Tests/TidyBox.Modules.DiskAnalyzer.Tests.csproj
dotnet add src/TidyBox.Modules.DiskAnalyzer reference src/TidyBox.Core
dotnet add tests/TidyBox.Modules.DiskAnalyzer.Tests reference src/TidyBox.Modules.DiskAnalyzer
```

- [ ] **Step 2: 运行 `dotnet build TidyBox.sln`，确认骨架编译通过**

- [ ] **Step 3: 写失败的测试：对一个临时目录（含2个子文件夹，其中一个有受保护/不可访问的孙子文件夹）扫描，验证聚合大小正确、受保护文件夹不中断整体扫描**

```csharp
public class DirectoryScannerTests
{
    [Fact]
    public async Task ScanAsync_TwoSubfolders_ReturnsAggregatedSizePerFolder()
    {
        var root = CreateTempDir();
        WriteFile(Path.Combine(root, "A", "a.txt"), bytes: 100);
        WriteFile(Path.Combine(root, "A", "a2.txt"), bytes: 200);
        WriteFile(Path.Combine(root, "B", "b.txt"), bytes: 50);

        var scanner = new DirectoryScanner();
        var snapshot = await scanner.ScanAsync(root);

        var a = snapshot.Entries.Single(e => e.Path.EndsWith("A"));
        var b = snapshot.Entries.Single(e => e.Path.EndsWith("B"));
        Assert.Equal(300, a.SizeBytes);
        Assert.Equal(50, b.SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_SubfolderAccessDenied_SkipsItAndContinuesOthers()
    {
        var root = CreateTempDir();
        WriteFile(Path.Combine(root, "Readable", "x.txt"), bytes: 10);
        var locked = Path.Combine(root, "Locked");
        Directory.CreateDirectory(locked);
        DenyAccess(locked); // 测试辅助方法：用ACL或模拟UnauthorizedAccessException让该目录不可读

        var scanner = new DirectoryScanner();
        var snapshot = await scanner.ScanAsync(root);

        Assert.Contains(snapshot.Entries, e => e.Path.EndsWith("Readable"));
        // Locked文件夹要么被跳过、要么以SizeBytes=0出现，不能让ScanAsync抛异常
    }
}
```

（`DenyAccess`这个测试辅助方法的具体实现留给实现者决定——可以用`DirectorySecurity`设置ACL拒绝当前用户读权限，或者如果环境限制无法方便设置ACL，改用一个能注入"模拟访问被拒"的可测试抽象，比实际改ACL更稳定可靠，这个选择是实现者的工程判断，不是本计划要锁死的细节）

- [ ] **Step 4: 运行测试，确认失败**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter DirectoryScannerTests`
Expected: FAIL

- [ ] **Step 5: 实现 `DiskScanEntry`/`DiskScanSnapshot`/`DirectoryScanner`**

`DirectoryScanner.ScanAsync`思路：枚举`rootPath`的直接子目录，对每个子目录用`Parallel.ForEachAsync`并行递归求和（`Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)`配合try/catch包住单个子目录的求和过程，捕获`UnauthorizedAccessException`/`IOException`后把该子目录当成`SizeBytes=0`处理并继续，不向上抛）。

- [ ] **Step 6: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter DirectoryScannerTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/TidyBox.Modules.DiskAnalyzer tests/TidyBox.Modules.DiskAnalyzer.Tests TidyBox.sln
git commit -m "feat: add shared disk-scan data model and directory scanner"
```

---

## Task 2: 快照持久化 + 历史对比

**Files:**
- Create: `src/TidyBox.Modules.DiskAnalyzer/History/SnapshotStore.cs`
- Create: `src/TidyBox.Modules.DiskAnalyzer/History/SnapshotDiff.cs`
- Test: `tests/TidyBox.Modules.DiskAnalyzer.Tests/History/SnapshotStoreTests.cs`
- Test: `tests/TidyBox.Modules.DiskAnalyzer.Tests/History/SnapshotDiffTests.cs`

**Interfaces:**
- Consumes: `DiskScanSnapshot`/`DiskScanEntry`（Task 1）
- Produces:
  - `class SnapshotStore { SnapshotStore(string storageDirectory); Task SaveAsync(DiskScanSnapshot snapshot); Task<DiskScanSnapshot?> LoadLatestAsync(string rootPath); }` —— 按`RootPath`存最新一份快照成JSON文件（文件名用`rootPath`的哈希，避免路径里的特殊字符问题），`LoadLatestAsync`找不到时返回null，不抛异常
  - `record DiskScanEntryDelta(string Path, long SizeDeltaBytes, long PreviousSizeBytes, long CurrentSizeBytes)`
  - `static class SnapshotDiff { IReadOnlyList<DiskScanEntryDelta> Compare(DiskScanSnapshot previous, DiskScanSnapshot current); }` —— 按`Path`匹配两份快照的条目，算`SizeDeltaBytes`，按绝对值降序排列；只在`previous`里出现、`current`里消失的文件夹，`CurrentSizeBytes`记为0（对应Review Focus第2条，不抛异常）

- [ ] **Step 1: 写失败的测试：`SnapshotStore`存了再读，拿到同样内容；读一个不存在的`rootPath`返回null**

```csharp
public class SnapshotStoreTests
{
    [Fact]
    public async Task SaveAsync_ThenLoadLatest_ReturnsSameSnapshot()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var snapshot = new DiskScanSnapshot("C:\\Games", DateTime.UtcNow,
            new[] { new DiskScanEntry("C:\\Games\\A", 100, DateTime.UtcNow, 3) });

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadLatestAsync("C:\\Games");

        Assert.NotNull(loaded);
        Assert.Equal(100, loaded!.Entries.Single().SizeBytes);
    }

    [Fact]
    public async Task LoadLatestAsync_UnknownRootPath_ReturnsNull()
    {
        var store = new SnapshotStore(CreateTempDir());
        Assert.Null(await store.LoadLatestAsync("C:\\Nothing"));
    }
}

public class SnapshotDiffTests
{
    [Fact]
    public void Compare_FolderGrew_ReportsPositiveDelta()
    {
        var previous = Snapshot(("A", 100));
        var current = Snapshot(("A", 300));

        var deltas = SnapshotDiff.Compare(previous, current);

        Assert.Equal(200, deltas.Single(d => d.Path == "A").SizeDeltaBytes);
    }

    [Fact]
    public void Compare_FolderDisappeared_ReportsFullNegativeDeltaWithoutThrowing()
    {
        var previous = Snapshot(("A", 100), ("B", 50));
        var current = Snapshot(("A", 100)); // B消失了

        var deltas = SnapshotDiff.Compare(previous, current);

        var b = deltas.Single(d => d.Path == "B");
        Assert.Equal(0, b.CurrentSizeBytes);
        Assert.Equal(-50, b.SizeDeltaBytes);
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter "SnapshotStoreTests|SnapshotDiffTests"`
Expected: FAIL

- [ ] **Step 3: 实现 `SnapshotStore`（JSON序列化读写，文件名用`rootPath`的稳定哈希，比如SHA256取前16位十六进制）和 `SnapshotDiff`**

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter "SnapshotStoreTests|SnapshotDiffTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/TidyBox.Modules.DiskAnalyzer/History tests/TidyBox.Modules.DiskAnalyzer.Tests/History
git commit -m "feat: add snapshot persistence and historical diff comparison"
```

---

## Task 3: Steam库解析器

**Files:**
- Create: `src/TidyBox.Modules.DiskAnalyzer/Steam/SteamGameInfo.cs`
- Create: `src/TidyBox.Modules.DiskAnalyzer/Steam/SteamLibraryParser.cs`
- Test: `tests/TidyBox.Modules.DiskAnalyzer.Tests/Steam/SteamLibraryParserTests.cs`

**Interfaces:**
- Consumes: 无（独立组件，不依赖Task 1/2的类型）
- Produces:
  - `record SteamGameInfo(string AppId, string Name, long SizeBytes, DateTime LastUpdatedUtc, string InstallPath)`
  - `class SteamLibraryParser { IReadOnlyList<string> FindLibraryFolders(string steamInstallPath); IReadOnlyList<SteamGameInfo> ParseLibrary(string libraryFolderPath); }`
    - `FindLibraryFolders`读取`{steamInstallPath}/steamapps/libraryfolders.vdf`，解析出所有库路径
    - `ParseLibrary`扫描某个库路径下`steamapps/appmanifest_*.acf`文件，每个文件解析出一个`SteamGameInfo`；单个`.acf`文件损坏/字段缺失时跳过该文件、不中断其它文件的解析（对应Review Focus第3条）

- [ ] **Step 1: 写失败的测试：用测试目录里手写的示例`.vdf`/`.acf`文本（真实Steam文件的简化版）验证解析结果；另外验证一个字段残缺的`.acf`被跳过而不抛异常**

```csharp
public class SteamLibraryParserTests
{
    [Fact]
    public void ParseLibrary_ValidAcfFile_ReturnsGameInfo()
    {
        var libDir = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(libDir, "steamapps"));
        File.WriteAllText(Path.Combine(libDir, "steamapps", "appmanifest_123.acf"), """
            "AppState"
            {
                "appid"		"123"
                "name"		"Example Game"
                "SizeOnDisk"		"5368709120"
                "LastUpdated"		"1700000000"
            }
            """);

        var parser = new SteamLibraryParser();
        var games = parser.ParseLibrary(libDir);

        var game = games.Single();
        Assert.Equal("123", game.AppId);
        Assert.Equal("Example Game", game.Name);
        Assert.Equal(5368709120, game.SizeBytes);
    }

    [Fact]
    public void ParseLibrary_MalformedAcfFile_SkipsItWithoutThrowing()
    {
        var libDir = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(libDir, "steamapps"));
        File.WriteAllText(Path.Combine(libDir, "steamapps", "appmanifest_999.acf"), "not even close to valid vdf {{{");
        File.WriteAllText(Path.Combine(libDir, "steamapps", "appmanifest_123.acf"), """
            "AppState"
            {
                "appid"		"123"
                "name"		"Example Game"
                "SizeOnDisk"		"100"
                "LastUpdated"		"1700000000"
            }
            """);

        var parser = new SteamLibraryParser();
        var games = parser.ParseLibrary(libDir);

        Assert.Single(games); // 只有123解析成功，999被跳过，没有抛异常
    }

    [Fact]
    public void FindLibraryFolders_ParsesLibraryFoldersVdf()
    {
        var steamDir = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(steamDir, "steamapps"));
        File.WriteAllText(Path.Combine(steamDir, "steamapps", "libraryfolders.vdf"), """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                }
            }
            """);

        var parser = new SteamLibraryParser();
        var folders = parser.FindLibraryFolders(steamDir);

        Assert.Equal(2, folders.Count);
        Assert.Contains(folders, f => f.Contains("SteamLibrary"));
    }
}
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter SteamLibraryParserTests`
Expected: FAIL

- [ ] **Step 3: 实现一个最小的VDF文本解析（不需要通用VDF库——VDF格式对本任务的需求很窄，只要能抓key-value对和嵌套的`{}`块即可，手写一个简单的递归下降解析器或基于正则的行解析都可以，由实现者选择，只要测试通过）**

实现 `SteamGameInfo`、`SteamLibraryParser`。`ParseLibrary`对每个`.acf`文件单独try/catch，解析失败（缺字段、格式错误）就跳过，继续下一个文件。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter SteamLibraryParserTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/TidyBox.Modules.DiskAnalyzer/Steam tests/TidyBox.Modules.DiskAnalyzer.Tests/Steam
git commit -m "feat: add Steam library VDF/ACF parser"
```

---

## Task 4: DiskAnalyzerModule（IToolModule实现）

**Files:**
- Create: `src/TidyBox.Modules.DiskAnalyzer/DiskAnalyzerModule.cs`
- Create: `src/TidyBox.Modules.DiskAnalyzer/Steam/SteamUninstallLauncher.cs`
- Test: `tests/TidyBox.Modules.DiskAnalyzer.Tests/DiskAnalyzerModuleTests.cs`

**Interfaces:**
- Consumes: `IToolModule`（脚手架计划Task 1）, `DirectoryScanner`/`SnapshotStore`/`SnapshotDiff`（本计划Task 1-2）, `SteamLibraryParser`（本计划Task 3）
- Produces:
  - `class DiskAnalyzerModule : IToolModule` —— `ModuleId = "disk-analyzer"`, `DisplayName = "磁盘空间分析"`；`InitializeAsync()`里检测本机Steam安装路径（常见路径探测，找不到就记录"无Steam"状态，不抛异常——对应Review Focus第5条）
  - `interface ISteamUninstallLauncher { void LaunchUninstall(string appId); }`，真实实现用 `Process.Start(new ProcessStartInfo("steam://uninstall/" + appId) { UseShellExecute = true })`（不需要提权，Steam自己处理卸载流程和可能的UAC）

- [ ] **Step 1: 写失败的测试：`DiskAnalyzerModule.ModuleId`/`DisplayName`符合约定；`InitializeAsync()`在没有Steam的环境下不抛异常，且之后能查到"没有Steam"的状态**

```csharp
public class DiskAnalyzerModuleTests
{
    [Fact]
    public void ModuleId_And_DisplayName_MatchConvention()
    {
        var module = new DiskAnalyzerModule(steamInstallPathOverride: null);
        Assert.Equal("disk-analyzer", module.ModuleId);
        Assert.Equal("磁盘空间分析", module.DisplayName);
    }

    [Fact]
    public async Task InitializeAsync_NoSteamInstalled_DoesNotThrowAndReportsNoSteam()
    {
        var module = new DiskAnalyzerModule(steamInstallPathOverride: "C:\\DoesNotExist");

        await module.InitializeAsync(); // 不应该抛异常

        Assert.False(module.IsSteamAvailable);
    }
}
```

（`steamInstallPathOverride`这个构造参数是为了让测试可控地指定/排除Steam路径，不依赖测试机器上是否真的装了Steam——这是本任务必须提供的可测试性钩子，不是可选项）

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter DiskAnalyzerModuleTests`
Expected: FAIL

- [ ] **Step 3: 实现 `DiskAnalyzerModule`、`ISteamUninstallLauncher`/`SteamUninstallLauncher`**

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Modules.DiskAnalyzer.Tests --filter DiskAnalyzerModuleTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/TidyBox.Modules.DiskAnalyzer tests/TidyBox.Modules.DiskAnalyzer.Tests
git commit -m "feat: add DiskAnalyzerModule IToolModule implementation with Steam uninstall launcher"
```

---

## Task 5: WinUI3 视图（手动验证，非TDD）

**Files:**
- Create: `src/TidyBox.Modules.DiskAnalyzer/Views/DiskAnalyzerView.xaml` / `.xaml.cs`

**Interfaces:**
- Consumes: `DiskAnalyzerModule`（Task 4），脚手架计划Task 5的`TidyBox.App`外壳（本任务把`DiskAnalyzerModule`注册进外壳App的`ModuleRegistry`）
- Produces: 无（叶子视图）

- [ ] **Step 1: 在 `TidyBox.App` 项目里引用 `TidyBox.Modules.DiskAnalyzer`，在外壳App启动时把 `new DiskAnalyzerModule()` 注册进 `ModuleRegistry`**

- [ ] **Step 2: 实现 `DiskAnalyzerView`：顶部"扫描"按钮触发`DirectoryScanner.ScanAsync`，结果用简单的列表（文件夹名+大小，按大小降序）展示——V1不要求做真正的Treemap可视化控件，先用列表验证数据链路通了（Treemap视觉渲染的具体控件选型留给实现者判断，不在本计划锁死）**

- [ ] **Step 3: 加一个"与上次对比"按钮，调用`SnapshotStore`+`SnapshotDiff`，列表旁显示增长最多的几个文件夹**

- [ ] **Step 4: 加一个"游戏库"分区，显示`SteamLibraryParser`解析出的游戏列表，每行一个"卸载"按钮调用`ISteamUninstallLauncher`**

- [ ] **Step 5: 手动验证：运行外壳App，点击"扫描"选择一个真实文件夹，确认列表显示正确；如果本机有Steam，确认游戏库分区显示真实已安装游戏**

Run: 在Visual Studio里直接运行 `TidyBox.App` 项目
Expected: 窗口打开后能看到磁盘分析模块的导航项，点进去功能可用

- [ ] **Step 6: Commit**

```bash
git add src/TidyBox.Modules.DiskAnalyzer/Views src/TidyBox.App
git commit -m "feat: add disk analyzer WinUI3 view and wire it into the shell app"
```

---

## 完成后的状态

磁盘分析模块作为第一个真正可用的业务模块接入外壳App：用户可以扫描任意文件夹看到占用情况、对比两次扫描找出空间增长来源、看到Steam游戏库占用并一键跳转卸载。右键菜单管理模块和卸载工具模块是下一步的独立计划。
