# P0 骨架与契约 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付一个能启动的 Avalonia 桌面壳：19 个页面路由全部可导航、暗/亮主题令牌生效、数据契约与 `ITspApiClient` 冻结并由夹具实现驱动，构建 0 警告 0 错误。

**Architecture:** 四层解决方案 `Tsp.Desktop → Tsp.Desktop.ViewModels → Tsp.Contracts`（服务端两层在 P2 才加入）。本阶段只实现客户端侧：`Tsp.Contracts` 放纯 DTO，`Tsp.Desktop.ViewModels` 放路由注册表、导航服务与夹具 API 客户端（全部可单测，不引用任何 View），`Tsp.Desktop` 放 Avalonia 壳、主题令牌与页面占位。

**Tech Stack:** .NET 10 / C# / Avalonia 12.1.6 / CommunityToolkit.Mvvm 8.4.2 / Microsoft.Extensions.DependencyInjection 10.0.14 / xunit 2.9.3

**Spec:** `docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md`

## Global Constraints

- TFM 一律 `net10.0`（本机 SDK 10.0.403 已具备）。
- Avalonia **12.1.6**（已验证其 TFM 含 `net10.0`）。客户端**不得**使用 WPF，**不得**引用任何 `StockSharp.*` 包（该包族为 WPF，TFM 仅 `net10.0-windows7.0`）。
- MVVM 用 **CommunityToolkit.Mvvm 8.4.2**：`[ObservableProperty]` / `[RelayCommand]`；命令的 `CanExecute` 依赖属性必须挂 `[NotifyCanExecuteChangedFor]`。
- DI 用 **Microsoft.Extensions.DependencyInjection 10.0.14**，注册集中在 `App.axaml.cs`。
- **CPM 集中包管理**：`ManagePackageVersionsCentrally=true`、`CentralPackageVersionOverrideEnabled=false` → **csproj 内禁止写 `Version`**，所有版本只在 `Directory.Packages.props` 登记。
- 测试栈固定 **xunit 2.9.3 + xunit.runner.visualstudio 3.1.5 + Microsoft.NET.Test.Sdk 18.11.0**。本阶段**不引入** `Avalonia.Headless.XUnit`（其 12.1.6 依赖 `xunit.v3`，会迫使测试栈分裂）；UI 靠真实运行验证。
- 命名：私有/内部字段（**含 `static`**）一律 `_camelCase`，**不用 `s_` 前缀**；常量 `PascalCase`；接口 `I` 前缀；类型/方法/属性 `PascalCase`。
- 格式：Allman 大括号；文件作用域命名空间且与文件夹一致；`using` 置于命名空间之外、`System.*` 在前；4 空格缩进（csproj/props/axaml 为 2 空格）。
- **构建基线：0 警告 0 错误。**
- 颜色语义：**红涨绿跌**（`bull` = `#F04438` / `bear` = `#12B76A`），且**仅用于价格与 K 线**，不用于 UI 状态；数字用等宽字形。
- 主题：**暗色为默认**，亮色可切；层次靠 1px 边框，暗色下不用阴影。
- 依赖方向（强制，禁止反向）：`Tsp.Desktop → Tsp.Desktop.ViewModels → Tsp.Contracts`。**客户端不得引用服务端程序集。**
- 价格字段用 `decimal`（A 股 2 位 / ETF 3 位小数）；成交量用 `long`；计算层（P3 起）内部用 `double`，只在契约边界用 `decimal`。
- 提交信息：中文 Conventional Commits，形如 `feat(scaffold): …`；**代码提交**在描述末尾带版本尾注 `（0.1.0）`，与 `Directory.Build.props` 的 `<Version>` 一致；文档与对应代码同批提交。
- 远端 `https://github.com/jackhuclan/wpf-stock-panel.git` 当前为**空仓库**。`git init` 与本地提交可自行进行；**推送须用户明确确认**，不得自行 push。

## Review Focus

以下五项是本阶段代码最可能被真实使用方式击穿的地方，各自的测试挂在下表所列任务里：

1. **导航到未注册的路由键**（拼错、深链、将来页面被裁剪）→ 应停在明确的空状态，不崩、不白屏。（Task 4）
2. **夹具目录缺失**（首次运行、路径写错）→ 应给出可读错误信息，不抛未捕获异常穿透到 UI。（Task 5、Task 8）
3. **夹具 JSON 损坏或缺字段**（契约加了字段而夹具没跟上）→ 缺字段取默认值继续；JSON 语法错误报可读异常。（Task 5）
4. **主题切换时已打开的页面**（令牌未走 ThemeDictionaries）→ 颜色应实时跟随，不残留旧值。（Task 7）
5. **长中文页名与宽数字溢出**（侧栏、指数条）→ 应正确省略或换行，不撑破布局。（Task 6）

---

### Task 1: 仓库与构建基线

建立解决方案、规范文件与一个可运行的测试基线。本任务结束时 `dotnet build` 与 `dotnet test` 都必须通过——这是后续所有任务的前提。

**Files:**
- Create: `Tsp.slnx`
- Create: `.editorconfig`（自 `E:\wpf-float-agent\.editorconfig` 复制 1:1，不改内容）
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.gitignore`
- Create: `AGENTS.md`
- Create: `src/Tsp.Contracts/Tsp.Contracts.csproj`
- Create: `tests/Tsp.Contracts.Tests/Tsp.Contracts.Tests.csproj`
- Create: `tests/Tsp.Contracts.Tests/BaselineTest.cs`

**Interfaces:**
- Consumes: 无
- Produces: 可编译的 `Tsp.Contracts` + `Tsp.Contracts.Tests` 两个项目，以及后续任务要用的**全部**包版本登记（Avalonia 12.1.6 / Avalonia.Desktop 12.1.6 / Avalonia.Themes.Fluent 12.1.6 / Avalonia.Fonts.Inter 12.1.6 / CommunityToolkit.Mvvm 8.4.2 / Microsoft.Extensions.DependencyInjection 10.0.14 / xunit 2.9.3 / xunit.runner.visualstudio 3.1.5 / Microsoft.NET.Test.Sdk 18.11.0）。`Tsp.Desktop.ViewModels` 由 Task 3 创建，`Tsp.Desktop` 由 Task 6 创建；**服务端的 `Tsp.Server` / `Tsp.Server.Core` 属 P2，本阶段不建**。

- [ ] **Step 1: 初始化仓库**

```bash
cd /e/wpf-stock-panel
git init
git remote add origin https://github.com/jackhuclan/wpf-stock-panel.git
```

不要 push。

- [ ] **Step 2: 复制规范文件**

从 `E:\wpf-float-agent\` 复制 `.editorconfig`（**保持内容不变**，含其"静态字段用 `_camelCase` 不用 `s_`"的仓库特有约定）。

- [ ] **Step 3: 写 `Directory.Build.props`**

发布/升级版本时只改 `<Version>` 一处。`Product` 用 `Tsp`，`RepositoryUrl` / `PackageProjectUrl` 指向 `https://github.com/jackhuclan/wpf-stock-panel`，`Version` 起始 `0.1.0`。保留 `Deterministic=true`、`IncludeSourceRevisionInInformationalVersion=true`、`GenerateDocumentationFile=false`。改 `<Description>` 为本项目描述。

- [ ] **Step 4: 写 `Directory.Packages.props`**

`ManagePackageVersionsCentrally=true`、`CentralPackageTransitivePinningEnabled=false`、`CentralPackageVersionOverrideEnabled=false`。按用途分组登记版本：MVVM 与 DI / Avalonia / 测试。版本值取 Global Constraints 所列，逐字一致。

- [ ] **Step 5: 写 `.gitignore`**

忽略 `bin/`、`obj/`、`.vs/`、`release/`、`artifacts/`、`data/`、`*.user`，以及将来的本地配置（含密钥者绝不入库）。

- [ ] **Step 6: 写 `AGENTS.md`**

提炼本项目开发规范：依赖方向、命名约定、CPM 规则、构建基线、验证习惯、提交与发版流程、指向 spec 与 plan 的路径。参照 `E:\wpf-float-agent\AGENTS.md` 的结构，但内容必须是本项目的（**不得**照抄其 Agent 框架、工具注册等无关章节）。

- [ ] **Step 7: 建 `Tsp.Contracts` 项目**

`dotnet new classlib`，删掉模板生成的 `Class1.cs`。csproj：`<TargetFramework>net10.0</TargetFramework>`、`<Nullable>enable</Nullable>`、`<ImplicitUsings>enable</ImplicitUsings>`。不引用任何包。

- [ ] **Step 8: 建 `Tsp.Contracts.Tests` 项目并写基线测试**

基线测试同时验证**测试栈可用**与 **`Directory.Build.props` 的版本已生效**——后者是 Task 1 的真实交付物，否则无人验证：

```csharp
namespace Tsp.Contracts.Tests;

public sealed class BaselineTest
{
    [Fact]
    public void Assembly_version_comes_from_Directory_Build_props()
    {
        var version = typeof(BaselineTest).Assembly.GetName().Version;

        Assert.Equal(new Version(0, 1, 0, 0), version);
    }
}
```

csproj 引用 `Tsp.Contracts`、`xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`（**只写 Include，不写 Version**），并设 `<IsPackable>false</IsPackable>` 与 `<Using Include="Xunit" />`（否则每个测试文件都要手写 `using Xunit;`）。

- [ ] **Step 9: 建 `Tsp.slnx` 并加入两个项目**

用 `.slnx`（与 `E:\wpf-float-agent\Floaticon.slnx` 同格式）。

- [ ] **Step 10: 验证构建与测试**

```bash
dotnet build Tsp.slnx
dotnet test tests/Tsp.Contracts.Tests
```

Expected: `Build succeeded` 且**警告数 0**；测试 1 passed。

- [ ] **Step 11: 提交**

```bash
git add -A
git commit -m "feat(scaffold): 初始化解决方案与构建基线（0.1.0）"
```

---

### Task 2: `Tsp.Contracts` 首批 DTO 与序列化契约

**Files:**
- Create: `src/Tsp.Contracts/Instruments/AssetType.cs`
- Create: `src/Tsp.Contracts/Instruments/Exchange.cs`
- Create: `src/Tsp.Contracts/Instruments/Instrument.cs`
- Create: `src/Tsp.Contracts/Market/DailyBar.cs`
- Create: `src/Tsp.Contracts/Market/IndexQuote.cs`
- Create: `src/Tsp.Contracts/Watchlist/WatchlistItem.cs`
- Test: `tests/Tsp.Contracts.Tests/SerializationRoundTripTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Tsp.Contracts` 项目
- Produces: `AssetType { Stock, Etf, Index }`；`Exchange { Sh, Sz, Bj }`；`Instrument(string Symbol, string Name, Exchange Exchange, AssetType AssetType, DateOnly ListedOn)`；`DailyBar(string Symbol, DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume, decimal Amount)`；`IndexQuote(string Symbol, string Name, decimal Close, decimal ChangePct)`；`WatchlistItem(Instrument Instrument, string? Note, decimal Close, decimal ChangePct)`——全部为 `sealed record`，`decimal` 用于价格与额，`long` 用于成交量。

- [ ] **Step 1: 写失败的序列化往返测试**

覆盖三类输入：正常值；**缺字段的 JSON**（只给 `symbol`，其余缺）；非法枚举字符串。

```csharp
public sealed class SerializationRoundTripTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DailyBar_round_trips_with_decimal_precision()
    {
        var bar = new DailyBar("600519.SH", new DateOnly(2026, 10, 26), 1697.00m, 1705.50m, 1690.10m, 1702.30m, 1234567L, 2100000000.55m);
        var json = JsonSerializer.Serialize(bar, Options);
        Assert.Equal(bar, JsonSerializer.Deserialize<DailyBar>(json, Options));
    }

    [Fact]
    public void Instrument_missing_fields_takes_defaults()
    {
        var json = """{"symbol":"600519.SH"}""";
        var instrument = JsonSerializer.Deserialize<Instrument>(json, Options);
        Assert.NotNull(instrument);
        Assert.Equal("600519.SH", instrument.Symbol);
        Assert.Equal(string.Empty, instrument.Name);
        Assert.Equal(AssetType.Stock, instrument.AssetType);
    }

    [Theory]
    [InlineData(AssetType.Stock, 0)]
    [InlineData(AssetType.Etf, 1)]
    [InlineData(AssetType.Index, 2)]
    public void AssetType_wire_numbers_are_pinned(AssetType value, int expected)
    {
        Assert.Equal(expected, (int)value);
        Assert.Equal(value, JsonSerializer.Deserialize<AssetType>(expected.ToString(), Options));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Tsp.Contracts.Tests`
Expected: 编译失败，提示 `DailyBar` / `Instrument` / `AssetType` 未定义。

- [ ] **Step 3: 实现 DTO**

`sealed record` + 位置参数。`Instrument` 的 `Name` 与 `ListedOn` 需要默认值才能在缺字段时取默认——实现方式：把 `Name` 声明为 `string Name = ""`、`ListedOn` 声明为 `DateOnly ListedOn = default`、`Exchange`/`AssetType` 亦给默认值，使缺字段不抛异常。枚举**不设** `JsonStringEnumConverter`（保持数字），且 **`AssetType.Stock` 必须是 0 号成员**——缺字段时取默认即为此值，上面的线号测试钉的就是这条。

> 事实说明：`System.Text.Json` **默认接受未定义的数值枚举**（`99` 会反序列化成 `(AssetType)99`，不抛异常）。因此 DTO 层不做越界校验；枚举合法性校验属 P2 的 API 边界职责。不要为"让越界值抛异常"而在 P0 引入自定义转换器。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Tsp.Contracts.Tests`
Expected: **6 passed**（Task 1 基线 1 个 + 本任务 5 个：`DailyBar_round_trips_with_decimal_precision` 1 个、`Instrument_missing_fields_takes_defaults` 1 个、`AssetType_wire_numbers_are_pinned` 的 3 个 InlineData 计 3 个）。

- [ ] **Step 5: 提交**

```bash
git add src/Tsp.Contracts tests/Tsp.Contracts.Tests
git commit -m "feat(contracts): 新增首批行情与标的 DTO（0.1.0）"
```

---

### Task 3: 路由注册表

**Files:**
- Create: `src/Tsp.Desktop.ViewModels/Tsp.Desktop.ViewModels.csproj`
- Create: `src/Tsp.Desktop.ViewModels/Navigation/PageDomain.cs`
- Create: `src/Tsp.Desktop.ViewModels/Navigation/PageRoute.cs`
- Create: `src/Tsp.Desktop.ViewModels/Navigation/Routes.cs`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/Tsp.Desktop.ViewModels.Tests.csproj`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/RoutesTests.cs`

**Interfaces:**
- Consumes: Task 1 的 CPM 与 Task 2 的 `Tsp.Contracts`
- Produces: `PageDomain { Core, Ext }`；`PageRoute(string Key, string Title, PageDomain Domain, int Order)`；`Routes.All`（`IReadOnlyList<PageRoute>`）、`Routes.Find(string key)`（`PageRoute?`）。`Routes` 上为每个键另有 `const string` 常量（`Routes.Dashboard`、`Routes.Watchlist`、`Routes.Screener`、`Routes.Factors`、`Routes.Backtest`、`Routes.Regime`、`Routes.Indices`、`Routes.Data`、`Routes.StockAnalysis`、`Routes.LimitLadder`、`Routes.ConceptAnalysis`、`Routes.IndustryAnalysis`、`Routes.Financials`、`Routes.Monitor`、`Routes.Abnormal`、`Routes.Lots`、`Routes.Paper`、`Routes.Signals`、`Routes.Review`），供后续任务引用时避免字符串字面量

- [ ] **Step 1: 建 `Tsp.Desktop.ViewModels` 项目**

`dotnet new classlib`（**不用** Avalonia 模板——本项目要可脱离 UI 单测）。csproj 引用 `Tsp.Contracts`。**本任务不引用 `CommunityToolkit.Mvvm`**：本任务只定义路由数据，第一个真正用到 `[ObservableProperty]` 的是 Task 6 的 `MainWindowViewModel`，届时再加——避免留下未被任何代码使用的包引用。加进 `Tsp.slnx`。

- [ ] **Step 2: 建测试项目**

同 Task 1 Step 8 的配置方式，引用 `Tsp.Desktop.ViewModels`。

- [ ] **Step 3: 写失败的路由表测试**

```csharp
public sealed class RoutesTests
{
    [Fact]
    public void All_contains_exactly_the_nineteen_spec_pages()
    {
        var expected = new[]
        {
            "/dashboard", "/watchlist", "/screener", "/factors", "/backtest", "/regime", "/indices", "/data",
            "/stock-analysis", "/limit-ladder", "/concept-analysis", "/industry-analysis", "/financials",
            "/monitor", "/abnormal", "/lots", "/paper", "/signals", "/review",
        };
        Assert.Equal(expected.OrderBy(k => k), Routes.All.Select(r => r.Key).OrderBy(k => k));
    }

    [Fact]
    public void All_has_unique_keys_and_unique_order()
    {
        Assert.Equal(Routes.All.Count, Routes.All.Select(r => r.Key).Distinct().Count());
        Assert.Equal(Routes.All.Count, Routes.All.Select(r => r.Order).Distinct().Count());
    }

    [Fact]
    public void Core_pages_match_spec_domains()
    {
        var core = Routes.All.Where(r => r.Domain == PageDomain.Core).Select(r => r.Key).OrderBy(k => k);
        Assert.Equal(
            new[] { "/backtest", "/dashboard", "/data", "/factors", "/indices", "/regime", "/screener", "/watchlist" },
            core);
    }

    [Fact]
    public void Find_returns_null_for_unknown_key()
    {
        Assert.Null(Routes.Find("/does-not-exist"));
    }
}
```

- [ ] **Step 4: 运行测试确认失败**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 编译失败，`Routes` / `PageRoute` / `PageDomain` 未定义。

- [ ] **Step 5: 实现 `PageRoute`、`PageDomain`、`Routes`**

`Routes.All` 按 Order 升序返回，顺序与 spec §5.6 的表格一致：核心 8 页（看板、自选、策略、因子、回测、市场环境、指数、数据）在前，扩展 11 页随后。标题用 spec 的中文页名。`Routes.Find` 用 `StringComparer.Ordinal` 精确匹配，未命中返回 `null`（**不抛异常**——这是 Review Focus 第 1 项的落点）。

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 4 passed。

- [ ] **Step 7: 提交**

```bash
git add src/Tsp.Desktop.ViewModels tests/Tsp.Desktop.ViewModels.Tests Tsp.slnx
git commit -m "feat(nav): 新增 19 页路由注册表（0.1.0）"
```

---

### Task 4: 导航服务

**Files:**
- Create: `src/Tsp.Desktop.ViewModels/Navigation/INavigationService.cs`
- Create: `src/Tsp.Desktop.ViewModels/Navigation/NavigationService.cs`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/NavigationServiceTests.cs`

**Interfaces:**
- Consumes: Task 3 的 `Routes`、`PageRoute`
- Produces: `INavigationService { PageRoute? Current { get; } event EventHandler<PageRoute?>? CurrentChanged; void Navigate(string routeKey); bool CanGoBack { get; } void GoBack(); }`

- [ ] **Step 1: 写失败的导航测试**

必须钉住未注册路由的行为（Review Focus 第 1 项）：

```csharp
public sealed class NavigationServiceTests
{
    [Fact]
    public void Navigate_to_registered_key_sets_current_and_raises_event()
    {
        var sut = new NavigationService();
        PageRoute? observed = null;
        sut.CurrentChanged += (_, route) => observed = route;

        sut.Navigate(Routes.Dashboard);

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.Equal("/dashboard", observed!.Key);
    }

    [Fact]
    public void Navigate_to_unknown_key_stops_at_empty_state_without_throwing()
    {
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);

        var ex = Record.Exception(() => sut.Navigate("/nope"));

        Assert.Null(ex);
        Assert.Null(sut.Current);
    }

    [Fact]
    public void GoBack_returns_to_previous_route_and_reports_can_go_back()
    {
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);
        sut.Navigate(Routes.Watchlist);
        Assert.True(sut.CanGoBack);

        sut.GoBack();

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.False(sut.CanGoBack);
    }

    [Fact]
    public void GoBack_at_root_is_a_no_op()
    {
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);

        var ex = Record.Exception(sut.GoBack);

        Assert.Null(ex);
        Assert.Equal("/dashboard", sut.Current!.Key);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 编译失败，`NavigationService` 未定义。

- [ ] **Step 3: 实现 `NavigationService`**

用 `Stack<PageRoute>` 做返回栈。`Navigate`：`Routes.Find` 命中则入栈并更新 `Current`，未命中则把 `Current` 置 `null` 并触发 `CurrentChanged`（**不抛异常**，且不清空返回栈）。`CanGoBack` 为栈深 > 1。`GoBack` 在栈深 ≤ 1 时是 no-op。`CurrentChanged` 的泛型参数用 `PageRoute?`，空状态也通知——这正是空状态页得以显示的机制。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 8 passed（Task 3 的 4 个 + 本任务的 4 个）。

- [ ] **Step 5: 提交**

```bash
git add src/Tsp.Desktop.ViewModels tests/Tsp.Desktop.ViewModels.Tests
git commit -m "feat(nav): 新增导航服务与返回栈（0.1.0）"
```

---

### Task 5: `ITspApiClient` 与夹具实现

契约冻结的关键任务：这个接口在 P2 会被 `HttpApiClient` 实现，因此签名一旦定下，View 不得再改。

**Files:**
- Create: `src/Tsp.Desktop.ViewModels/Services/ITspApiClient.cs`
- Create: `src/Tsp.Desktop.ViewModels/Services/FixtureDataException.cs`
- Create: `src/Tsp.Desktop.ViewModels/Services/FixtureApiClient.cs`
- Create: `src/Tsp.Desktop/Fixtures/index-quotes.json`
- Create: `src/Tsp.Desktop/Fixtures/instruments.json`
- Create: `src/Tsp.Desktop/Fixtures/watchlist.json`
- Create: `src/Tsp.Desktop/Fixtures/daily-bars/600519.SH.json`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/FixtureApiClientTests.cs`

**Interfaces:**
- Consumes: Task 2 的全部 DTO
- Produces: `ITspApiClient { bool IsFixtureData { get; } Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken ct); Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct); Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct); Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken ct); }`；`FixtureDataException(string message)`；`FixtureApiClient(string fixtureDirectory)`

- [ ] **Step 1: 写失败的夹具测试**

钉住缺失目录、JSON 损坏、缺字段三种情形（Review Focus 第 2、3 项）：

```csharp
public sealed class FixtureApiClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tsp-fixture-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private void Write(string relativePath, string json)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    [Fact]
    public async Task GetIndexQuotes_reads_and_maps_fixture()
    {
        Write("index-quotes.json", """[{"symbol":"000001.SH","name":"上证指数","close":3697.52,"changePct":0.19}]""");
        var sut = new FixtureApiClient(_dir);

        var quotes = await sut.GetIndexQuotesAsync(CancellationToken.None);

        Assert.Single(quotes);
        Assert.Equal(3697.52m, quotes[0].Close);
        Assert.True(sut.IsFixtureData);
    }

    [Fact]
    public async Task Missing_fixture_directory_reports_readable_error()
    {
        var sut = new FixtureApiClient(Path.Combine(_dir, "not-here"));

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains("not-here", ex.Message);
    }

    [Fact]
    public async Task Corrupt_json_reports_readable_error_naming_the_file()
    {
        Write("index-quotes.json", "{ this is not json");
        var sut = new FixtureApiClient(_dir);

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains("index-quotes.json", ex.Message);
    }

    [Fact]
    public async Task Fixture_missing_optional_fields_still_maps()
    {
        Write("watchlist.json", """[{"instrument":{"symbol":"600519.SH"},"close":1702.30,"changePct":0.31}]""");
        var sut = new FixtureApiClient(_dir);

        var items = await sut.GetWatchlistAsync(CancellationToken.None);

        Assert.Single(items);
        Assert.Null(items[0].Note);
        Assert.Equal("600519.SH", items[0].Instrument.Symbol);
    }

    [Fact]
    public async Task GetDailyBars_filters_by_date_range()
    {
        Write("daily-bars/600519.SH.json", """
            [{"symbol":"600519.SH","date":"2026-10-22","open":1690,"high":1700,"low":1685,"close":1695,"volume":100,"amount":169500},
             {"symbol":"600519.SH","date":"2026-10-23","open":1695,"high":1710,"low":1692,"close":1705,"volume":110,"amount":187550}]
            """);
        var sut = new FixtureApiClient(_dir);

        var bars = await sut.GetDailyBarsAsync("600519.SH", new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23), CancellationToken.None);

        Assert.Single(bars);
        Assert.Equal(new DateOnly(2026, 10, 23), bars[0].Date);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 编译失败，`FixtureApiClient` / `FixtureDataException` / `ITspApiClient` 未定义。

- [ ] **Step 3: 实现接口与客户端**

`FixtureApiClient` 只在 **ViewModels 项目**（读文件用 BCL，不需要 Avalonia），夹具目录由构造函数注入——这样测试可以用临时目录，而生产路径由 `Tsp.Desktop` 传入。行为约定：

- 目录不存在 → 抛 `FixtureDataException`，消息含**绝对路径**；
- 文件缺失 → 抛 `FixtureDataException`，消息含**文件名**；
- JSON 语法错误 → 捕获 `JsonException` 后抛 `FixtureDataException`，消息含**文件名**与原始异常消息；
- 字段缺失 → 由 Task 2 的 DTO 默认值兜住，正常映射；
- 日期区间过滤在客户端内做，边界**闭区间**；
- `IsFixtureData` 恒为 `true`。

JSON 用 `JsonSerializerDefaults.Web`，`DateOnly` 走内置转换器。

- [ ] **Step 4: 写夹具数据文件**

放在 `src/Tsp.Desktop/Fixtures/`，csproj 设 `CopyToOutputDirectory=PreserveNewest`。内容用**真实**的上证/深证/创业板/科创综指数值（取自 spec 截图：`3697.52` / `11534.38` / `2658.23` / `1573.83`），自选与日 K 用小样本，够驱动 P0 与后续页面骨架即可。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 13 passed。

- [ ] **Step 6: 提交**

```bash
git add src/Tsp.Desktop.ViewModels src/Tsp.Desktop/Fixtures tests/Tsp.Desktop.ViewModels.Tests
git commit -m "feat(contracts): 冻结 ITspApiClient 并实现夹具客户端（0.1.0）"
```

---

### Task 6: Avalonia 壳工程与主窗口骨架

从本任务起进入 UI。验收靠**真实运行 + 截图**（对齐 `E:\wpf-float-agent\AGENTS.md` §9 的验证习惯），不引入 headless 测试栈。

**Files:**
- Create: `src/Tsp.Desktop/Tsp.Desktop.csproj`
- Create: `src/Tsp.Desktop/Program.cs`
- Create: `src/Tsp.Desktop/App.axaml`
- Create: `src/Tsp.Desktop/App.axaml.cs`
- Create: `src/Tsp.Desktop/Themes/Tokens.axaml`
- Create: `src/Tsp.Desktop/Views/MainWindow.axaml`
- Create: `src/Tsp.Desktop/Views/MainWindow.axaml.cs`
- Create: `src/Tsp.Desktop.ViewModels/MainWindowViewModel.cs`
- Create: `src/Tsp.Desktop/Views/Pages/PlaceholderPage.axaml`
- Create: `src/Tsp.Desktop/Views/Pages/PlaceholderPage.axaml.cs`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: Task 3 的 `Routes`/`PageRoute`、Task 4 的 `INavigationService`、Task 5 的 `ITspApiClient`
- Produces: `MainWindowViewModel(MainWindowViewModel 依赖 INavigationService 与 ITspApiClient)`，暴露 `IReadOnlyList<PageRoute> Menu`、`PageRoute? CurrentRoute`、`bool IsFixtureData`、`string? ErrorMessage`、`IAsyncRelayCommand LoadCommand`、`IRelayCommand<PageRoute> NavigateCommand`

- [ ] **Step 1: 写失败的 ViewModel 测试**

```csharp
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Menu_exposes_all_routes_in_order()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        Assert.Equal(Routes.All.Select(r => r.Key), sut.Menu.Select(r => r.Key));
    }

    [Fact]
    public async Task Load_sets_current_route_to_the_first_menu_entry()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(sut.CurrentRoute);
        Assert.Equal(Routes.All[0].Key, sut.CurrentRoute!.Key);
    }

    [Fact]
    public async Task Load_surfaces_client_failure_as_readable_error_state()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient(new FixtureDataException("/missing: 夹具数据缺失")));

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(sut.ErrorMessage);
        Assert.Contains("夹具数据缺失", sut.ErrorMessage!);
    }

    [Fact]
    public void NavigateCommand_updates_current_route()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        sut.NavigateCommand.Execute(Routes.All[3]);

        Assert.Equal(Routes.All[3].Key, sut.CurrentRoute!.Key);
    }
}
```

`FakeApiClient` 是本测试文件内的私有实现，形状固定如下（Task 8 会复用它的 `IndexQuotes` 与 `IsFixtureData`）：

```csharp
private sealed class FakeApiClient(Exception? failure = null) : ITspApiClient
{
    public bool IsFixtureData { get; init; } = true;
    public IReadOnlyList<IndexQuote> IndexQuotes { get; init; } = [];

    public Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken ct) => Respond(IndexQuotes);
    public Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct) => Respond<IReadOnlyList<Instrument>>([]);
    public Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct) => Respond<IReadOnlyList<DailyBar>>([]);
    public Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken ct) => Respond<IReadOnlyList<WatchlistItem>>([]);

    private Task<T> Respond<T>(T value) => failure is null ? Task.FromResult(value) : Task.FromException<T>(failure);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 编译失败，`MainWindowViewModel` 未定义。

- [ ] **Step 3: 实现 `MainWindowViewModel`**

用 `[ObservableProperty]` / `[RelayCommand]`。`LoadCommand` 调 `ITspApiClient` 拉一次指数快照，成功则 `IsFixtureData = client.IsFixtureData`，任何 `FixtureDataException` 都**捕获后写入 `ErrorMessage`**（绝不外抛——Review Focus 第 2 项的落点）。`NavigateCommand` 委托给 `INavigationService.Navigate`，并订阅 `CurrentChanged` 同步 `CurrentRoute`。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 17 passed。

- [ ] **Step 5: 建 Avalonia 壳工程**

**手写项目文件，不要用 `dotnet new avalonia.app`**——本机未安装 `Avalonia.Templates`，且无需为一次性脚手架改动全局模板。若确实想用模板，先 `dotnet new install Avalonia.Templates::12.1.6`，但这不是必需路径。

csproj 需要：`<OutputType>WinExe</OutputType>`、`<TargetFramework>net10.0</TargetFramework>`、`<Nullable>enable</Nullable>`、`<ImplicitUsings>enable</ImplicitUsings>`、`<BuiltInComInteropSupport>true</BuiltInComInteropSupport>`、`<ApplicationManifest>app.manifest</ApplicationManifest>`（可省）、`<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`。引用 `Avalonia`、`Avalonia.Desktop`、`Avalonia.Themes.Fluent`、`Avalonia.Fonts.Inter`、`CommunityToolkit.Mvvm`、`Microsoft.Extensions.DependencyInjection` 与 `Tsp.Desktop.ViewModels`（**只写 Include，不写 Version**）。

注意 `AvaloniaUseCompiledBindingsByDefault=true` 意味着所有 `{Binding}` 必须能编译期解析到属性——`x:DataType` 要在各处写上，否则编译报错。若这一步反复卡住，可先设 `false` 用反射绑定跑通，再在 P1 收敛。加进 `Tsp.slnx`。

- [ ] **Step 6: 写 `App.axaml` / `App.axaml.cs`、`Program.cs` 与 `Themes/Tokens.axaml`**

**本步骤同时给 `Tsp.Desktop.ViewModels` 补上 `CommunityToolkit.Mvvm` 引用**——Task 3 刻意没加（当时无代码使用它），而本任务的 `MainWindowViewModel` 是第一个用到 `[ObservableProperty]` / `[RelayCommand]` 的地方，所以在这里补，保持"引用只在被使用时出现"。

`App.axaml.cs` 只做 DI 装配与主窗口创建，**禁止写业务逻辑**。注册：`INavigationService` → Singleton，`ITspApiClient` → Singleton（夹具目录取 `AppContext.BaseDirectory/Fixtures`），`MainWindowViewModel` → Singleton，`MainWindow` → Singleton。`Program.cs` 用 `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`；启动时 `Directory.SetCurrentDirectory(AppContext.BaseDirectory)`（对齐 `wpf-float-agent` 的运行时锚定习惯）。

**本步骤同时创建 `Themes/Tokens.axaml`**——主窗口的颜色全部引用它，键必须在此定义，否则下一步跑不起来。用 `ResourceDictionary.ThemeDictionaries` 定义 `Dark` 与 `Light` 两组，**键名完全一致**（这是 Review Focus 第 4 项的落点：令牌必须走 ThemeDictionaries，运行中切换才会实时生效）。颜色值逐字取自 spec §5.3：

| 键 | Dark | Light |
|---|---|---|
| `BaseBrush` | `#0A0A0B` | `#FAFAFA` |
| `SurfaceBrush` | `#18181B` | `#FFFFFF` |
| `ElevatedBrush` | `#212126` | `#F4F4F5` |
| `BorderBrush` | `#353539` | `#E4E4E7` |
| `FgPrimaryBrush` | `#FAFAFA` | `#18181B` |
| `FgSecondaryBrush` | `#C4C4CB` | `#52525B` |
| `FgMutedBrush` | `#8E8E96` | `#A1A1AA` |
| `AccentBrush` | `#3B82F6` | `#3B82F6` |
| `BullBrush` | `#F04438` | `#F04438` |
| `BearBrush` | `#12B76A` | `#12B76A` |
| `WarningBrush` | `#F79009` | `#F79009` |
| `DangerBrush` | `#F04438` | `#F04438` |

另有圆角 `CardRadius`(8)、`BtnRadius`(6)、`InputRadius`(4)、`DialogRadius`(12) 与缓动 `SmoothEasing`。字体族：sans 用 `Inter, HarmonyOS Sans SC, PingFang SC`（启用 `Avalonia.Fonts.Inter`），mono 用 `JetBrains Mono, IBM Plex Mono` 并在缺失时回落系统等宽。

`App.axaml` 里合并 `Tokens.axaml`，`RequestedThemeVariant` 设 `Dark`，`<Application.Styles>` 引入 Fluent 主题。

- [ ] **Step 7: 写 `MainWindow.axaml` 骨架**

三区布局：左侧栏（固定宽，含 19 项 `ListBox` 菜单绑定 `Menu`，底部放"设置"入口）+ 内容区（`ContentControl` 按 `CurrentRoute` 切占位页）+ 侧栏底部指数条（预留，Task 8 填内容）。颜色一律引用 Step 6 定义的令牌键，**不得硬编码色值**。

侧栏菜单项文本用 `TextTrimming="CharacterEllipsis"`，指数条数字用等宽字体——这是 Review Focus 第 5 项的落点。

- [ ] **Step 8: 写占位页**

`PlaceholderPage` 显示 `CurrentRoute` 的中文页名、路由键与 `核心/扩展` 徽标。未命中路由（`CurrentRoute` 为 `null`）时显示"未找到该页面"空状态——Review Focus 第 1 项的 UI 落点。

- [ ] **Step 9: 真实运行验证**

```bash
dotnet run --project src/Tsp.Desktop
```

Expected: 窗口启动，暗色主题，左侧 19 项可见。逐项点击，右侧标题随之变化；窗口最大化与缩到最小宽度时侧栏文字省略而非撑破。抓一张截图核对。

- [ ] **Step 10: 提交**

```bash
git add src/Tsp.Desktop src/Tsp.Desktop.ViewModels tests/Tsp.Desktop.ViewModels.Tests Tsp.slnx
git commit -m "feat(ui): 新增 Avalonia 主窗口骨架与 19 页占位（0.1.0）"
```

---

### Task 7: 控件样式库与明暗切换

Token 已在 Task 6 定义并接进 `App.axaml`（默认暗色）。本任务把散落在主窗口里的控件样式收敛成样式库，并加上运行中切换主题的入口与验证。

**Files:**
- Create: `src/Tsp.Desktop/Themes/Controls.axaml`
- Modify: `src/Tsp.Desktop/App.axaml`
- Modify: `src/Tsp.Desktop/Views/MainWindow.axaml`
- Modify: `src/Tsp.Desktop/Views/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: Task 6 的 `MainWindow` 与 `Tokens.axaml` 的全部令牌键
- Produces: `Controls.axaml` 中的具名样式（侧栏项、卡片、徽标、按钮），供 Task 8 与 P1 的页面复用

- [ ] **Step 1: 写 `Controls.axaml`**

把 Task 6 侧栏与指数条中用到的控件样式集中在此：侧栏项（选中态用 `AccentBrush`，非选中用 `FgSecondaryBrush`）、卡片（`SurfaceBrush` + `BorderBrush` 1px + `CardRadius`）、徽标（核心/扩展）、按钮（`BtnRadius` + `SmoothEasing` 过渡）。只引用令牌键，**不得出现硬编码色值**。主窗口相应位置改为引用这些具名样式。

- [ ] **Step 2: 接进 `App.axaml`**

`<Application.Styles>` 在 Fluent 主题之后引入 `Controls.axaml`。

- [ ] **Step 3: 加主题切换入口**

在 `MainWindow` 侧栏底部加一个切换按钮，`MainWindow.axaml.cs` 里设 `Application.Current.RequestedThemeVariant` 在 `Dark`/`Light` 间切换。不加新依赖。

- [ ] **Step 4: 真实运行验证**

```bash
dotnet run --project src/Tsp.Desktop
```

Expected: 启动即暗色。点切换按钮，**侧栏、内容区、边框颜色同帧全部跟随**（不得残留暗色值）——这是 Review Focus 第 4 项的验收。抓暗/亮两张截图对比。再把窗口切到最窄，确认侧栏文字仍省略而非溢出。

- [ ] **Step 5: 回归测试**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 19 passed（本任务不新增测试，确认未回归）。**注意**：不要跑全解决方案的 `dotnet test`——那会连 `Tsp.Contracts.Tests` 的 6 个一起算，总数是 25 而非 19。

- [ ] **Step 6: 提交**

```bash
git add src/Tsp.Desktop
git commit -m "feat(ui): 收敛控件样式库并支持明暗主题切换（0.1.0）"
```

---

### Task 8: 夹具角标、指数条与错误/空状态

收口 P0 的界面语义：让"数据是假的"这件事在界面上无法被忽略（spec §5.2 的硬要求）。

**Files:**
- Modify: `src/Tsp.Desktop/Views/MainWindow.axaml`
- Modify: `src/Tsp.Desktop.ViewModels/MainWindowViewModel.cs`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: Task 5 的 `ITspApiClient`、Task 6 的 `MainWindowViewModel`、Task 7 的令牌键
- Produces: `MainWindowViewModel` 追加 `IReadOnlyList<IndexQuote> IndexQuotes`、`bool ShowFixtureBadge`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact]
public async Task Load_populates_index_quotes_and_shows_fixture_badge()
{
    var client = new FakeApiClient { IndexQuotes = [new IndexQuote("000001.SH", "上证指数", 3697.52m, 0.19m)] };
    var sut = new MainWindowViewModel(new NavigationService(), client);

    await sut.LoadCommand.ExecuteAsync(null);

    Assert.Single(sut.IndexQuotes);
    Assert.True(sut.ShowFixtureBadge);
}

[Fact]
public async Task Fixture_badge_is_hidden_when_client_is_not_fixture_data()
{
    var client = new FakeApiClient { IsFixtureData = false };
    var sut = new MainWindowViewModel(new NavigationService(), client);

    await sut.LoadCommand.ExecuteAsync(null);

    Assert.False(sut.ShowFixtureBadge);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 编译失败，`IndexQuotes` / `ShowFixtureBadge` 未定义。

- [ ] **Step 3: 实现**

`ShowFixtureBadge = IsFixtureData`（夹具身份以客户端自报为准，不由 View 猜）。`IndexQuotes` 在 `LoadCommand` 中填充。

- [ ] **Step 4: 接进界面**

侧栏底部指数条绑定 `IndexQuotes`：每项显示名称、点位、涨跌幅，**涨用 `BullBrush`、跌用 `BearBrush`**，数字用等宽字体（Review Focus 第 5 项）。夹具角标用固定位置的醒目徽标绑定 `ShowFixtureBadge`，文本写明是夹具数据。`ErrorMessage` 非空时在内容区显示错误态而非空白。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests`
Expected: 19 passed。

- [ ] **Step 6: 真实运行验证**

```bash
dotnet run --project src/Tsp.Desktop
```

Expected: 指数条显示四个指数且涨红跌绿；夹具角标可见；把 `Fixtures` 目录临时改名后重启，界面显示可读错误而非崩溃或白屏（**验完必须改回**）。

- [ ] **Step 7: 提交**

```bash
git add src/Tsp.Desktop src/Tsp.Desktop.ViewModels tests/Tsp.Desktop.ViewModels.Tests
git commit -m "feat(ui): 新增夹具角标、指数条与错误状态（0.1.0）"
```

---

### Task 9: P0 验收与交付

**Files:**
- Create: `README.md`
- Modify: `AGENTS.md`（补验证与发版章节，若 Task 1 未写全）
- Modify: `docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md`（风险登记）

**Interfaces:**
- Consumes: Task 1–8 的全部产出
- Produces: 可交付的 P0 状态

- [ ] **Step 1: 跑完整验收**

```bash
dotnet build Tsp.slnx
dotnet test
dotnet run --project src/Tsp.Desktop
```

Expected: 构建**警告 0 错误 0**；全部测试通过；应用启动后逐条核对 spec §5.7 的 P0 验收——19 项可导航（含未注册路由停空状态）、暗/亮切换生效、夹具角标可见。

- [ ] **Step 2: 写 `README.md`**

项目简介、当前阶段（P0 已完成、P1 未开始）、如何构建与运行、验收定义、指向 spec 与 plan 的链接。**不得**宣称尚未实现的能力。

- [ ] **Step 3: 更新 spec 的风险登记**

把"Avalonia 12.1.6 与 net10.0 兼容性"一行标注为**已验证通过**（TFM 含 `net10.0`，本机 SDK 10.0.403），并记录 `Avalonia.Diagnostics` 无 12.x 版本这一事实，避免后续重复踩。

- [ ] **Step 4: 提交并请示推送**

```bash
git add -A
git commit -m "docs: 补充 README 与 P0 验收记录（0.1.0）"
```

然后**停下来问用户是否推送**——远端是公开仓库，推送是对外动作，不得自行执行。

---

## 附：P1–P7 的后续计划

本计划只覆盖 P0。其余阶段各自单独成篇，写到该阶段前再产出：

| 计划 | 覆盖 | 主要产出 |
|---|---|---|
| P1 | 19 页视觉全铺（夹具驱动） | 自绘图表控件集 + 组件库 + 20 个页面 |
| P2 | 真数据源 | TickFlow free-api 客户端、数据源插件接口、Parquet/DuckDB、复权口径 |
| P3 | 指标流水线 | 列式帧 + 核心指标 + enriched 宽表 |
| P4 | 策略引擎 | 声明式策略定义 + 全市场扫描 |
| P5 | 回测引擎 | T+1/费用/滑点 + 净值与绩效指标 |
| P6 | 监控与告警 | 四类规则 + 盘后评估 + SSE 推送 |
| P7 | 开放接口与 AI | Open API + Token/scope + MCP + AI 助手 |

P1 的体量最大，且会反过来暴露契约缺口——这是做法 C 的既定代价，spec §4 已记为风险并用契约冻结对冲。P1 开工前应先按 Task 6/7 确立的组件库做一次抽查，确认契约够用。
