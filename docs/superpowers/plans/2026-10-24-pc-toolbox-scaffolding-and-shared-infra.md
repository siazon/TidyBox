# PC 维护工具箱 —— 项目脚手架与共享基础设施 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭建PC维护工具箱的项目骨架：解决方案结构、共享模块契约（IToolModule）、提权帮手进程与IPC、WinUI3外壳App、MSIX打包——为后续磁盘分析/右键菜单管理/卸载工具三个模块的独立计划提供稳定的接口基础。

**Architecture:** .NET 8 多项目解决方案。`TidyBox.Core`是无UI依赖的类库，承载模块契约和提权客户端逻辑；`TidyBox.ElevatedHelper`是独立的、按需以管理员身份启动的控制台程序，通过命名管道接收操作请求；`TidyBox.App`是WinUI3外壳，负责导航和加载各模块UI；`TidyBox.Packaging`是MSIX打包项目。各模块（磁盘分析等）将在后续计划中作为实现`IToolModule`的独立组件接入，本计划只搭骨架、不实现具体业务模块。

**Tech Stack:** .NET 8, WinUI3 (Windows App SDK), xUnit, System.IO.Pipes (命名管道), System.Text.Json

**Spec:** `docs/superpowers/specs/2026-10-24-pc-maintenance-toolbox-design.md`

## Global Constraints

- 技术栈固定为 .NET 8 + WinUI3，MSIX打包并声明 `runFullTrust` 能力。
- 只支持 Windows 11，不做 Windows 10 兼容。
- App 本身以普通用户权限运行，不声明 `requireAdministrator`；只有真正需要系统级权限的操作才临时拉起提权帮手进程，用完即退出。
- 不搭建对外的第三方插件分发/沙箱基础设施；内部模块按"松耦合组件、清晰接口、引擎层与UI层分离、模块间走消息/事件通信"的方式设计。
- 依赖选型优先 MIT/Apache 2.0 许可证的参考实现，不把 GPL 许可证项目的代码直接编译进本项目。

## Review Focus

- 用户在UAC弹窗里点"取消"：`ElevationClient` 必须返回明确的失败结果，不能挂起或抛出未处理异常。
- 帮手进程空闲超时自动退出后又来新请求：`ElevationClient` 必须能重新拉起帮手进程，而不是对着已失效的管道反复失败。
- 帮手进程已经在运行时：`ElevationClient` 不应该重复拉起第二个提权进程实例。
- 某个`IToolModule`实现初始化失败：外壳App要能优雅降级显示错误，不能导致整个App无法启动。
- 标准用户账户（非管理员账户）下安装并首次运行：核心的"无需管理员权限即可使用"功能必须真正可用，不能默认退化成不可用。

---

## Task 1: 项目脚手架 + 共享模块契约

**Files:**
- Create: `TidyBox.sln`
- Create: `src/TidyBox.Core/TidyBox.Core.csproj`
- Create: `src/TidyBox.Core/Modules/IToolModule.cs`
- Create: `src/TidyBox.Core/Modules/ModuleRegistry.cs`
- Test: `tests/TidyBox.Core.Tests/TidyBox.Core.Tests.csproj`
- Test: `tests/TidyBox.Core.Tests/Modules/ModuleRegistryTests.cs`

**Interfaces:**
- Produces:
  - `interface IToolModule { string ModuleId { get; } string DisplayName { get; } Task InitializeAsync(); }`
  - `class ModuleRegistry { void Register(IToolModule module); IReadOnlyList<IToolModule> GetAll(); IToolModule? GetById(string moduleId); }`
  - `ModuleRegistry.Register` 对初始化失败的模块不抛异常中断其他模块注册——`InitializeAsync()`的异常由调用方（Task 5的外壳App）捕获，`ModuleRegistry`本身只负责存取，不调用`InitializeAsync()`。

- [ ] **Step 1: 创建解决方案和空的Core/Tests项目骨架**

```bash
dotnet new sln -n TidyBox
dotnet new classlib -n TidyBox.Core -o src/TidyBox.Core -f net8.0
dotnet new xunit -n TidyBox.Core.Tests -o tests/TidyBox.Core.Tests -f net8.0
dotnet sln add src/TidyBox.Core/TidyBox.Core.csproj tests/TidyBox.Core.Tests/TidyBox.Core.Tests.csproj
dotnet add tests/TidyBox.Core.Tests reference src/TidyBox.Core
```

- [ ] **Step 2: 运行 `dotnet build`，确认空骨架能编译通过**

Run: `dotnet build TidyBox.sln`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: 写失败的测试：注册两个模块后 `GetAll()` 返回两个，`GetById()` 能按id取到**

```csharp
public class ModuleRegistryTests
{
    private class FakeModule : IToolModule
    {
        public string ModuleId { get; }
        public string DisplayName { get; }
        public FakeModule(string id, string name) { ModuleId = id; DisplayName = name; }
        public Task InitializeAsync() => Task.CompletedTask;
    }

    [Fact]
    public void Register_TwoModules_GetAllReturnsBoth()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "磁盘分析"));
        registry.Register(new FakeModule("context-menu", "右键菜单管理"));

        Assert.Equal(2, registry.GetAll().Count);
    }

    [Fact]
    public void GetById_KnownId_ReturnsModule()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "磁盘分析"));

        var found = registry.GetById("disk");

        Assert.NotNull(found);
        Assert.Equal("磁盘分析", found!.DisplayName);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var registry = new ModuleRegistry();
        Assert.Null(registry.GetById("missing"));
    }
}
```

- [ ] **Step 4: 运行测试，确认失败（因为`IToolModule`/`ModuleRegistry`还不存在）**

Run: `dotnet test tests/TidyBox.Core.Tests`
Expected: FAIL — 编译错误，找不到 `IToolModule`/`ModuleRegistry`

- [ ] **Step 5: 在 `src/TidyBox.Core/Modules/IToolModule.cs` 和 `ModuleRegistry.cs` 实现接口**

`ModuleRegistry`内部用`Dictionary<string, IToolModule>`按`ModuleId`存储；`Register`遇到重复id时直接覆盖（不在本任务里处理冲突策略，YAGNI，后续模块计划如有需要再加校验）。

- [ ] **Step 6: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Core.Tests`
Expected: PASS，3个测试全部通过

- [ ] **Step 7: Commit**

```bash
git add TidyBox.sln src/TidyBox.Core tests/TidyBox.Core.Tests
git commit -m "feat: add solution scaffold, IToolModule contract and ModuleRegistry"
```

---

## Task 2: 提权IPC消息契约

**Files:**
- Create: `src/TidyBox.Core/Elevation/ElevationRequest.cs`
- Create: `src/TidyBox.Core/Elevation/ElevationResponse.cs`
- Test: `tests/TidyBox.Core.Tests/Elevation/ElevationMessageTests.cs`

**Interfaces:**
- Consumes: 无（独立的数据契约，不依赖Task 1的类型）
- Produces:
  - `record ElevationRequest(string OperationType, string PayloadJson)`
  - `record ElevationResponse(bool Success, string? ErrorMessage, string? ResultJson)`
  - 两者都用 `System.Text.Json` 做序列化，后续Task 3/4直接 `JsonSerializer.Serialize/Deserialize<T>` 使用，不额外包装序列化方法。

- [ ] **Step 1: 写失败的测试：`ElevationRequest`/`ElevationResponse` 序列化后再反序列化，字段值不变**

```csharp
public class ElevationMessageTests
{
    [Fact]
    public void ElevationRequest_RoundTripsThroughJson()
    {
        var original = new ElevationRequest("uninstall.removeResidual", "{\"path\":\"C:\\\\Foo\"}");
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ElevationRequest>(json);

        Assert.Equal(original.OperationType, restored!.OperationType);
        Assert.Equal(original.PayloadJson, restored.PayloadJson);
    }

    [Fact]
    public void ElevationResponse_FailureCase_RoundTripsThroughJson()
    {
        var original = new ElevationResponse(false, "access denied", null);
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ElevationResponse>(json);

        Assert.False(restored!.Success);
        Assert.Equal("access denied", restored.ErrorMessage);
        Assert.Null(restored.ResultJson);
    }
}
```

- [ ] **Step 2: 运行测试，确认失败（类型不存在）**

Run: `dotnet test tests/TidyBox.Core.Tests --filter ElevationMessageTests`
Expected: FAIL — 编译错误

- [ ] **Step 3: 实现 `ElevationRequest`/`ElevationResponse` 两个record类型**

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Core.Tests --filter ElevationMessageTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/TidyBox.Core/Elevation tests/TidyBox.Core.Tests/Elevation
git commit -m "feat: add elevation IPC message contracts"
```

---

## Task 3: 提权服务端（帮手进程）

**Files:**
- Create: `src/TidyBox.ElevatedHelper/TidyBox.ElevatedHelper.csproj`
- Create: `src/TidyBox.ElevatedHelper/ElevationServer.cs`
- Create: `src/TidyBox.ElevatedHelper/IElevatedOperationHandler.cs`
- Create: `src/TidyBox.ElevatedHelper/Program.cs`
- Test: `tests/TidyBox.ElevatedHelper.Tests/TidyBox.ElevatedHelper.Tests.csproj`
- Test: `tests/TidyBox.ElevatedHelper.Tests/ElevationServerTests.cs`

**Interfaces:**
- Consumes: `ElevationRequest`/`ElevationResponse`（Task 2）
- Produces:
  - `interface IElevatedOperationHandler { string OperationType { get; } Task<ElevationResponse> HandleAsync(string payloadJson); }`
  - `class ElevationServer { ElevationServer(string pipeName, IEnumerable<IElevatedOperationHandler> handlers, TimeSpan idleTimeout); Task RunAsync(CancellationToken ct); }`
  - `ElevationServer.RunAsync`：在命名管道`pipeName`上循环接受连接，按`ElevationRequest.OperationType`匹配`handlers`里对应的`IElevatedOperationHandler`并调用，把结果写回；连续`idleTimeout`时间没有新连接就让`RunAsync`自然返回（调用方在`Program.cs`里据此退出进程）。找不到匹配`OperationType`的handler时返回`ElevationResponse(false, "unknown operation: {type}", null)`。

- [ ] **Step 1: 搭建 `TidyBox.ElevatedHelper` 控制台项目骨架，加入解决方案**

```bash
dotnet new console -n TidyBox.ElevatedHelper -o src/TidyBox.ElevatedHelper -f net8.0
dotnet new xunit -n TidyBox.ElevatedHelper.Tests -o tests/TidyBox.ElevatedHelper.Tests -f net8.0
dotnet sln TidyBox.sln add src/TidyBox.ElevatedHelper/TidyBox.ElevatedHelper.csproj tests/TidyBox.ElevatedHelper.Tests/TidyBox.ElevatedHelper.Tests.csproj
dotnet add src/TidyBox.ElevatedHelper reference src/TidyBox.Core
dotnet add tests/TidyBox.ElevatedHelper.Tests reference src/TidyBox.ElevatedHelper
dotnet add tests/TidyBox.ElevatedHelper.Tests reference src/TidyBox.Core
```

- [ ] **Step 2: 运行 `dotnet build`，确认骨架编译通过**

Run: `dotnet build TidyBox.sln`
Expected: Build succeeded

- [ ] **Step 3: 写失败的测试：用一个假的 `IElevatedOperationHandler`，验证服务端能通过命名管道收发消息；并验证"未知OperationType"和"空闲超时后RunAsync返回"两种情况**

```csharp
public class ElevationServerTests
{
    private class EchoHandler : IElevatedOperationHandler
    {
        public string OperationType => "echo";
        public Task<ElevationResponse> HandleAsync(string payloadJson)
            => Task.FromResult(new ElevationResponse(true, null, payloadJson));
    }

    [Fact]
    public async Task RunAsync_KnownOperation_EchoesPayloadBack()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        var request = new ElevationRequest("echo", "{\"x\":1}");
        await JsonSerializer.SerializeAsync(client, request);
        client.WaitForPipeDrain();
        // (sender then half-closes; server reads one request per connection then responds)
        var response = await JsonSerializer.DeserializeAsync<ElevationResponse>(client);

        Assert.True(response!.Success);
        Assert.Equal("{\"x\":1}", response.ResultJson);

        cts.Cancel();
    }

    [Fact]
    public async Task RunAsync_UnknownOperation_ReturnsFailureResponse()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        await JsonSerializer.SerializeAsync(client, new ElevationRequest("nope", "{}"));
        client.WaitForPipeDrain();
        var response = await JsonSerializer.DeserializeAsync<ElevationResponse>(client);

        Assert.False(response!.Success);
        Assert.Contains("nope", response.ErrorMessage);

        cts.Cancel();
    }

    [Fact]
    public async Task RunAsync_NoConnectionsWithinIdleTimeout_ReturnsOnItsOwn()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, Array.Empty<IElevatedOperationHandler>(), TimeSpan.FromMilliseconds(200));

        var completed = await Task.WhenAny(server.RunAsync(CancellationToken.None), Task.Delay(2000));

        Assert.True(completed.IsCompleted);
    }
}
```

- [ ] **Step 4: 运行测试，确认失败（`ElevationServer`/`IElevatedOperationHandler`不存在）**

Run: `dotnet test tests/TidyBox.ElevatedHelper.Tests`
Expected: FAIL

- [ ] **Step 5: 实现 `IElevatedOperationHandler` 和 `ElevationServer`**

`ElevationServer.RunAsync`思路：循环 `NamedPipeServerStream.WaitForConnectionAsync`，每次等待时用`Task.WhenAny`配合一个`idleTimeout`的`Task.Delay`竞速——超时先完成就让`RunAsync`返回；连接先建立就处理这一次请求/响应，然后关闭这次连接，继续下一轮循环等待。

- [ ] **Step 6: 实现 `Program.cs`：从命令行参数读取pipeName，构造`ElevationServer`（初始handler列表为空，留给后续模块计划往里加），调用`RunAsync`直到自然返回后进程退出**

- [ ] **Step 7: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.ElevatedHelper.Tests`
Expected: PASS，3个测试全部通过

- [ ] **Step 8: Commit**

```bash
git add src/TidyBox.ElevatedHelper tests/TidyBox.ElevatedHelper.Tests TidyBox.sln
git commit -m "feat: add elevated helper process with named-pipe IPC server"
```

---

## Task 4: 提权客户端

**Files:**
- Create: `src/TidyBox.Core/Elevation/IElevationClient.cs`
- Create: `src/TidyBox.Core/Elevation/ElevationClient.cs`
- Create: `src/TidyBox.Core/Elevation/IHelperProcessLauncher.cs`
- Test: `tests/TidyBox.Core.Tests/Elevation/ElevationClientTests.cs`
- Modify: `tests/TidyBox.Core.Tests/TidyBox.Core.Tests.csproj`（加一条对`src/TidyBox.ElevatedHelper`的项目引用——Task 3的`ElevationServer`定义在那个项目里，本任务的测试要启动一个真实`ElevationServer`来验证`ElevationClient`，这个引用在之前任何任务里都没加过，是本任务必须补的前置步骤）

**Interfaces:**
- Consumes: `ElevationRequest`/`ElevationResponse`（Task 2）
- Produces:
  - `interface IHelperProcessLauncher { bool IsHelperRunning(string pipeName); void LaunchElevated(string pipeName); }`
    （真实实现在本任务里用`Process.Start`配合`UseShellExecute=true, Verb="runas"`，会触发UAC；测试用假实现替身，不实际拉起UAC，对应Review Focus里"UAC取消"和"不重复拉起"两条就在这层假实现上验证客户端的处理逻辑）
  - `interface IElevationClient { Task<ElevationResponse> ExecuteAsync(ElevationRequest request, CancellationToken ct = default); }`
  - `class ElevationClient : IElevationClient`，构造函数接收`string pipeName`和`IHelperProcessLauncher launcher`

- [ ] **Step 1: 补项目引用，让 `TidyBox.Core.Tests` 能用到 Task 3 的 `ElevationServer`**

```bash
dotnet add tests/TidyBox.Core.Tests reference src/TidyBox.ElevatedHelper
```

- [ ] **Step 2: 写失败的测试，覆盖Review Focus里三条：已运行直接连接发送；未运行则调用Launch再连接；UAC取消（launcher抛异常）时返回失败而不抛出；空闲超时后管道已失效时能重新拉起**

测试里需要一个简单的echo处理器验证双方能收发——Task 3 的 `ElevationServerTests` 里的`EchoHandler`是那个测试文件私有的类，不能跨项目直接引用，本任务在自己的测试文件里新定义一个同样逻辑的`EchoHandler`测试替身（几行代码，不算重复劳动，是两个独立测试项目各自的测试替身）；但`ElevationServer`本身是`TidyBox.ElevatedHelper`项目里的生产类，直接`using`该项目的命名空间复用即可，不要重新实现一遍服务端逻辑。

```csharp
public class ElevationClientTests
{
    private class FakeLauncher : IHelperProcessLauncher
    {
        public bool Running;
        public int LaunchCount;
        public bool ThrowOnLaunch; // 模拟用户在UAC上点"取消"

        public bool IsHelperRunning(string pipeName) => Running;
        public void LaunchElevated(string pipeName)
        {
            LaunchCount++;
            if (ThrowOnLaunch) throw new InvalidOperationException("user declined UAC");
            Running = true; // 模拟拉起后进程变为"在运行"
        }
    }

    [Fact]
    public async Task ExecuteAsync_HelperAlreadyRunning_DoesNotLaunchAgain()
    {
        var launcher = new FakeLauncher { Running = true };
        // ...启动一个真实ElevationServer（同TidyBox.Core.Tests里引用ElevatedHelper项目或复刻一个最小echo server）
        // 断言 launcher.LaunchCount == 0，且 ExecuteAsync 正常拿到响应
    }

    [Fact]
    public async Task ExecuteAsync_HelperNotRunning_LaunchesThenConnects()
    {
        var launcher = new FakeLauncher { Running = false };
        // 断言 launcher.LaunchCount == 1
    }

    [Fact]
    public async Task ExecuteAsync_UserDeclinesUac_ReturnsFailureResponseNotThrow()
    {
        var launcher = new FakeLauncher { Running = false, ThrowOnLaunch = true };
        var client = new ElevationClient("pt-test-pipe", launcher);

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{}"));

        Assert.False(response.Success);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task ExecuteAsync_StalePipeAfterHelperExited_RelaunchesHelper()
    {
        var launcher = new FakeLauncher { Running = true }; // IsHelperRunning说在跑，但管道其实已经没人听
        var client = new ElevationClient("pt-test-pipe-" + Guid.NewGuid(), launcher);

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{}"), CancellationToken.None);

        // 连接失败后，客户端应该把 launcher 当作"没在跑"重试一次 LaunchElevated
        Assert.True(launcher.LaunchCount >= 1);
    }
}
```

- [ ] **Step 3: 运行测试，确认失败**

Run: `dotnet test tests/TidyBox.Core.Tests --filter ElevationClientTests`
Expected: FAIL

- [ ] **Step 4: 实现 `IHelperProcessLauncher`（真实版用`Process.Start(new ProcessStartInfo { UseShellExecute = true, Verb = "runas", FileName = ..., Arguments = pipeName })`）、`ElevationClient`**

`ElevationClient.ExecuteAsync`逻辑：
1. 若`launcher.IsHelperRunning(pipeName)`为false，调用`launcher.LaunchElevated(pipeName)`；若抛异常，直接返回`ElevationResponse(false, ex.Message, null)`。
2. 尝试连接管道（设超时，比如5秒）。连接失败（比如管道其实已经不在、Review Focus里"空闲超时后又来新请求"的情况）：把这次当成"其实没在运行"，调用一次`launcher.LaunchElevated(pipeName)`重试连接一次；仍失败才真正返回失败。
3. 连接成功后用`PipeMessageFraming.WriteAsync`/`ReadAsync`（Task 3 已加在`TidyBox.Core.Elevation`，4字节长度前缀）发送`ElevationRequest`、读取`ElevationResponse`并返回。**不要**直接对pipe用`JsonSerializer.SerializeAsync/DeserializeAsync`——Byte模式管道没有EOF，反序列化会永久挂起。

- [ ] **Step 5: 运行测试，确认通过**

Run: `dotnet test tests/TidyBox.Core.Tests --filter ElevationClientTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/TidyBox.Core/Elevation tests/TidyBox.Core.Tests/Elevation tests/TidyBox.Core.Tests/TidyBox.Core.Tests.csproj
git commit -m "feat: add elevation client with UAC-decline and stale-pipe retry handling"
```

---

## Task 5: WinUI3 外壳App

**Files:**
- Create: `src/TidyBox.App/TidyBox.App.csproj`
- Create: `src/TidyBox.App/App.xaml` / `App.xaml.cs`
- Create: `src/TidyBox.App/MainWindow.xaml` / `MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `ModuleRegistry`/`IToolModule`（Task 1）
- Produces: 无（外壳App是叶子节点，后续模块计划会往`ModuleRegistry`里注册真正的模块，不需要外壳App再对外产出接口）

- [ ] **Step 1: 手写WinUI3项目骨架（不要用 `dotnet new winui3`——预检查确认当前环境的 `dotnet new list` 不包含这个模板，WinUI3项目模板只在Visual Studio的"新建项目"向导里注册，CLI拿不到；手动创建 `.csproj` 和空的XAML文件即可，WinUI3项目本质就是一个带特定属性/包引用的SDK风格csproj，不依赖那个模板）**

手动创建 `src/TidyBox.App/TidyBox.App.csproj`，关键属性：
- `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>`
- `<TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>`（对应约束"只支持Windows 11"，实际运行时门槛在后续Task 6的manifest里用`TargetDeviceFamily`进一步收紧到Win11）
- `<OutputType>WinExe</OutputType>`，`<UseWinUI>true</UseWinUI>`
- PackageReference: `Microsoft.WindowsAppSDK`（当前稳定版）、`Microsoft.Windows.SDK.BuildTools`
- 项目引用 `TidyBox.Core`

```bash
dotnet sln TidyBox.sln add src/TidyBox.App/TidyBox.App.csproj
```

之后创建最小的 `App.xaml`/`App.xaml.cs`（标准WinUI3应用入口样板）和 `MainWindow.xaml`/`MainWindow.xaml.cs`（下一步再填内容）。

- [ ] **Step 2: 在 `MainWindow.xaml` 搭建一个 `NavigationView`，数据源绑定到一个空的`ModuleRegistry`实例（本任务不注册任何真实模块，留给后续模块计划）**

- [ ] **Step 3: 在 `MainWindow.xaml.cs` 里，遍历`ModuleRegistry.GetAll()`时，对调用`module.InitializeAsync()`失败的模块捕获异常，改为在导航列表里显示"该模块加载失败"而不是让异常冒泡导致App崩溃（对应Review Focus第4条）**

一行实现思路：在`try { await module.InitializeAsync(); AddToNav(module); } catch (Exception ex) { AddFailedEntry(module.DisplayName, ex.Message); }`外面套循环。

- [ ] **Step 4: 手动验证：运行 `dotnet build` + 本机启动App，确认窗口能打开、导航为空列表时不崩溃**

Run: `dotnet build src/TidyBox.App` 然后在Visual Studio或`dotnet run`启动
Expected: 窗口正常打开，没有异常对话框

- [ ] **Step 5: 手动验证Review Focus第4条：临时注册一个`InitializeAsync()`里直接`throw`的假模块，确认App仍能正常打开而不是崩溃，之后把这个临时假模块代码删掉**

- [ ] **Step 6: Commit**

```bash
git add src/TidyBox.App TidyBox.sln
git commit -m "feat: add WinUI3 shell app with graceful module-load-failure handling"
```

---

## Task 6: MSIX打包项目

**Files:**
- Create: `src/TidyBox.Packaging/TidyBox.Packaging.wapproj`
- Create: `src/TidyBox.Packaging/Package.appxmanifest`

**Interfaces:**
- Consumes: `TidyBox.App`（Task 5）作为打包目标
- Produces: 无（这是解决方案的终端打包产物）

- [ ] **Step 1: 创建Windows Application Packaging Project，加入解决方案，设置`TidyBox.App`为打包目标**

（这一步在Visual Studio里用"添加新项目 > Windows应用程序打包项目"模板操作，命令行`dotnet`工具链对`.wapproj`支持有限，手动在IDE里完成并记录到本步骤）

- [ ] **Step 2: 在 `Package.appxmanifest` 里配置：`<rescap:Capability Name="runFullTrust" />`，`TargetDeviceFamily` 最低版本设为 Windows 11（Build 22000起），不声明`requireAdministrator`**

- [ ] **Step 3: 本机生成MSIX包并安装测试**

Run: 在Visual Studio里右键打包项目 → "部署" 或 "创建应用包"
Expected: 生成`.msix`文件，本机双击能正常安装，开始菜单能看到App图标并正常启动（对应Task 5验证过的空外壳窗口）

- [ ] **Step 4: 手动验证Review Focus第5条：在一个标准用户账户（非管理员）下安装并启动这个MSIX包，确认App能正常打开（因为我们没有声明`requireAdministrator`，这一步应该直接成功，不弹UAC）**

Expected: 标准账户下双击打开App，窗口正常显示，不触发任何UAC提示

- [ ] **Step 5: Commit**

```bash
git add src/TidyBox.Packaging TidyBox.sln
git commit -m "feat: add MSIX packaging project with runFullTrust capability, Windows 11 minimum version"
```

---

## 完成后的状态

六个任务完成后，仓库里有一个能在标准用户账户下安装运行的空壳MSIX应用，具备：模块注册机制（`IToolModule`/`ModuleRegistry`）、按需提权的IPC机制（`ElevationClient`/`ElevationServer`）、WinUI3外壳窗口。三个业务模块（磁盘分析、右键菜单管理、卸载工具）将在各自独立的后续计划里，分别实现`IToolModule`、注册进`ModuleRegistry`，并在需要系统级操作时通过`IElevationClient.ExecuteAsync`调用本计划搭好的提权通道——不需要重新设计这层基础设施。
