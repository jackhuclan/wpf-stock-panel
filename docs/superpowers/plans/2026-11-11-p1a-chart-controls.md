# P1-A 图表基座与自绘控件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付一组自绘的 Avalonia 图表控件（基座 + 6 个控件），全部令牌化配色、随主题实时重绘，其数学部分可脱离 UI 单测；并用一个仅开发用的配色页在真机上验证六个控件在两套主题下的渲染。

**Architecture:** 控件放在 `src/Tsp.Desktop/Charts/`（它们**是**视图，故属 `Tsp.Desktop`），全部继承一个 `ChartSurface` 基座：画笔声明为 `StyledProperty` 并挂 `AffectsRender`，由 `Themes/Controls.axaml` 用 `{DynamicResource}` 赋值——主题一变即 setter 重算 → 属性变更 → 重绘。**渲染之外的数学**（值域映射、刻度、命中测试）抽成 `Tsp.Desktop/Charts/Layout/` 下的**纯静态函数**，由新建的 `tests/Tsp.Desktop.Tests` 直接测试——因为 `Tsp.Desktop.ViewModels` 不能引用 Avalonia，图表数学必须在别处有归宿。

**Tech Stack:** .NET 10 / C# / Avalonia 12.1.6 / `Control` + `DrawingContext` / xunit 2.9.3

**Spec:** `docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md`（§5.5 图表选型、§5.7 的 P1 条目与继承约束）

**这是 P1 三篇计划中的第一篇。** P1-B（视图分发缝 + 核心 8 页）与 P1-C（扩展 11 页 + 设置页）各自单独成篇，写在开工前。

## Global Constraints

- TFM `net10.0`；Avalonia `12.1.6`；**CPM 集中包管理，csproj 内禁止写 `Version`**。图表自绘，`ScottPlot.Avalonia` 仍只登记在 CPM 作后备、不启用。
- **本阶段新增两个包，且仅测试用**：`Avalonia.Headless` 与 `Avalonia.Skia`（均 12.1.6），只进 `tests/Tsp.Desktop.Tests`。**这是对 P0 的 Ruling 8 的更正**：那条排除的是 `Avalonia.Headless.XUnit`（它依赖 `xunit.v3.extensibility.core`，会劈开测试栈），却错误地泛化成了"本仓库不做无头测试"。**已核实**：纯 `Avalonia.Headless` 12.1.6 只依赖 `Avalonia` / `Avalonia.Fonts.Inter` / `Avalonia.HarfBuzz`，**不碰 xunit**。代价是两个测试依赖；换来的是**"切主题重绘"从"只能人眼看一次"变成"每次 CI 都能像素比对"**——本阶段存在的理由就是这件事，不该把它留成永久手工。
- 依赖方向（强制）：`Tsp.Desktop → Tsp.Desktop.ViewModels → Tsp.Contracts`。图表控件在 `Tsp.Desktop`；**不得**为了测试方便把 Avalonia 引用加进 `Tsp.Desktop.ViewModels`。
- 构建基线 **0 警告 0 错误**；每步都跑 `dotnet build Tsp.slnx`。
- **颜色一律走 `Themes/Tokens.axaml` 的令牌，且必须用 `{DynamicResource}`。** P0 已有一条静态守卫测试（`ThemeTokenReferenceTests`）扫描 `src/Tsp.Desktop/**/*.axaml` 强制此事——**它必须始终为绿**，它是本阶段最大的失效防线。
- **红涨绿跌**（`BullBrush` 红 / `BearBrush` 绿），且该语义色**只用于价格与 K 线**，不用于界面外观。
- 数字一律用 `MonoFontFamily`（等宽/表格数字）。
- 令牌集由 spec §5.3 冻结；**本阶段不新增令牌**（`on-warning` 那条属 P1 的可访问性打磨，见 spec §5.7，不在本篇）。
- 命名：私有/内部字段（**含 `static`**）一律 `_camelCase`，**不用 `s_` 前缀**；类型/方法/属性 `PascalCase`。
- 格式：Allman；文件作用域命名空间且与文件夹一致；`using` 在命名空间外、`System.*` 在前；4 空格缩进（C#），**2 空格**（axaml/csproj）。
- **版本策略**：每次改动 → **补丁位 +1**；**子计划收口（本篇 P1-A 即属此档）→ 补丁位 +1**；**阶段性计划收口（P0、P1 整体做完）→ 次版本 +1、补丁位归零**。每个版本号要同步四处：`Directory.Build.props` 的 `<Version>`、`BaselineTest` 的版本断言、`README.md` 与 `AGENTS.md` 的「当前版本」；提交尾注必须等于新的 `<Version>`。**本阶段收口走补丁位，不要升次版本**；但 P1-C 收口同时是 P1 收口，那一次按**阶段**处理、升次版本。
- 提交信息：中文 Conventional Commits。**每个任务的提交都升一次补丁位**（尾注等于本次新的 `<Version>`），四处引用同步——这条按所有者定的版本策略执行，不必逐任务再判断该升哪一位。
- **给 Task 6–10 的一条前置注意**：`ChartSurface` 没有覆写 `MeasureOverride`/`ArrangeOverride`，所以 `PlotPadding` 只经 `PlotArea` 影响**绘制几何**，它在 `AffectsRender` 里是对的。**但派生控件若开始在 `MeasureOverride` 里用 `PlotPadding`，就必须同时注册 `AffectsMeasure`**，否则改内边距会以**过期的测量尺寸**重绘。
- **推送**：所有者已授权本仓库"完成任务后自动推送"——构建 0 警告、测试全绿、工作树干净后直接 `git push origin main`，不再逐次请示。

## Review Focus

以下是本阶段最可能被真实使用方式击穿的五处，各自的测试挂在下表所列任务里：

1. **空数据 / 单点 / 全部等值** → 控件不得抛异常、不得除零、不得画出无意义的坐标轴。（Task 2、4）
2. **切换主题后图表没有重绘** → 颜色残留旧主题。这正是 P0 花大力气堵上的静默失效在自绘控件里的翻版：`Render()` 里读不到 `{DynamicResource}`，只有"画笔走 `StyledProperty` + `AffectsRender`"这条路能实时跟随。（Task 4、11、12）
3. **极端值域**（量级差异极大、含负数、全负数）→ 刻度标签重叠或错位、柱高方向反了、K 线实体在零轴上下画翻。（Task 2、6、10）
4. **高 DPI 下的模糊与错位** → 1px 线与边框落在半像素上导致发虚。（Task 5）
5. **数据量增大**（250 个交易日的 K 线）→ 绘制卡顿或标签互相覆盖。（Task 2、10）

---

### Task 1: 图表测试工程

建立 `tests/Tsp.Desktop.Tests`——本阶段**唯一**能测图表的地方。它只测 `Charts/Layout/` 下的纯函数，**不实例化任何控件**（那需要 UI 线程与渲染栈，本阶段刻意不引入 headless 测试栈）。

**Files:**
- Create: `tests/Tsp.Desktop.Tests/Tsp.Desktop.Tests.csproj`
- Create: `tests/Tsp.Desktop.Tests/BaselineTest.cs`
- Modify: `Tsp.slnx`

**Interfaces:**
- Consumes: 无
- Produces: 一个可 `dotnet test` 的工程，引用 `Tsp.Desktop`（仅为拿到 `Charts/Layout/` 的纯类型）与 xunit 三件套

- [ ] **Step 1: 建工程并接入解决方案**

`dotnet new classlib`（**不用** Avalonia 模板），改为测试工程。csproj 需：`net10.0`、`IsPackable=false`、`<Using Include="Xunit" />`、对 `Tsp.Desktop` 的 `ProjectReference`，以及 `xunit` / `xunit.runner.visualstudio`（**带 `PrivateAssets="all"`**）/ `Microsoft.NET.Test.Sdk` 三个**只写 `Include`** 的 `PackageReference`。按 `tests/Tsp.Desktop.ViewModels.Tests/Tsp.Desktop.ViewModels.Tests.csproj` 的既有形状复制——那是本仓库已验证的测试工程模板。

加进 `Tsp.slnx` 的 `/tests/` 文件夹。

- [ ] **Step 2: 写基线测试**

沿用 `tests/Tsp.Contracts.Tests/BaselineTest.cs` 的既有形状（断言程序集版本来自 `Directory.Build.props`），把版本号改成**当前** `<Version>` 的值。

- [ ] **Step 3: 验证**

Run: `dotnet build Tsp.slnx` → 0 警告 0 错误；`dotnet test tests/Tsp.Desktop.Tests` → 1 passed。

- [ ] **Step 4: 提交（**不推送**——推送由控制者在复审通过后执行）**

提交信息带版本尾注，尾注等于本次新的 `<Version>`。同时按版本策略同步四处并升一个号。

---

### Task 2: 值域映射与刻度（纯函数）

图表的数学核心，也是 Review Focus 第 1、3、5 项的落点。**全部是纯静态函数，不碰 Avalonia 类型**——这样 Task 1 的工程能直接测。

**Files:**
- Create: `src/Tsp.Desktop/Charts/Layout/ValueRange.cs`
- Create: `src/Tsp.Desktop/Charts/Layout/AxisTicks.cs`
- Test: `tests/Tsp.Desktop.Tests/ValueRangeTests.cs`
- Test: `tests/Tsp.Desktop.Tests/AxisTicksTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `readonly record struct ValueRange(double Min, double Max)`，带静态工厂 `ValueRange.From(IEnumerable<double> values)` 与 `bool IsDegenerate { get; }`
  - `static class AxisTicks`，`static IReadOnlyList<double> Nice(double min, double max, int targetCount)`

- [ ] **Step 1: 写失败的测试**

必须钉住这几类输入（Review Focus 1、3）：

```csharp
[Fact] public void Empty_input_yields_a_usable_range_not_a_throw()          // (0,1)，**不是**退化区间——空与"全等值"是两回事
[Fact] public void Single_point_yields_a_degenerate_range()
[Fact] public void All_equal_values_yield_a_degenerate_range()
[Fact] public void From_ignores_NaN_and_Infinity()          // 不得让一个坏点毁掉整条线
[Fact] public void Nice_returns_expected_ticks_for_a_plain_range()   // 例：0..100 → 0,20,40,60,80,100
[Fact] public void Nice_on_a_degenerate_range_returns_a_usable_window() // 例：全 5.0 → 仍给出可画的上下界
[Fact] public void Nice_handles_a_huge_magnitude_spread()    // 例：0 与 1e9 同存
[Fact] public void Nice_handles_all_negative_values()
// 近退化带：min/max 只差几个 ULP —— 这两条是上面"至少 2 刻度且严格递增"契约的落点
[InlineData(100d, 100.00000000000001d, 7)]   // 曾产出 1 个刻度
[InlineData(1e16d, 1e16d + 4d, 100)]         // 曾产出 65 个刻度但只有 3 个不同值
public void Nice_stays_usable_in_the_near_degenerate_band(double min, double max, int targetCount)
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/Tsp.Desktop.Tests` → 编译失败（类型未定义）。

- [ ] **Step 3: 实现**

`ValueRange`：`From` 过滤非有限值；空集返回 `(0, 1)` 这类可用窗口而非 `(NaN, NaN)`；`IsDegenerate` 表示 `Min == Max`。`AxisTicks.Nice` 用经典的 nice-number 算法（步长取 1/2/5×10ⁿ），**保证不返回空列表、不产生 `NaN` 步长**。

**契约必须再加两条，且都要有测试钉住**（它们是给 Task 3 的画布用的——画布会算 `(v - ticks[0]) / (ticks[^1] - ticks[0])`）：

- **至少 2 个刻度，且严格递增。** 单刻度会让上式变成 `0/0 = NaN`，正是 `IsDegenerate` 存在要防的隐患；重复刻度会把 N 条网格线与 N 个标签画在同一坐标上。
- **区间窄到只剩几个 ULP 时必须检测并回退。** 当 `low/step` 与 `high/step` 四舍五入到同一个 `double`（`Ceiling`/`Floor` 于是相等 → `tickCount == 1`），或 `(double)(first + i)` 回写时丢掉 `i` 的低位，就会产出 1 个、或 N 个重复值。反例（实测）：`Nice(100, 100.00000000000001, 7)` → 1 个刻度；`Nice(2500, 2500.0000000000005, 5)` → 5 个但只有 2 个不同值。**做法**：建完列表后若 `Count < 2` 或非严格递增，回退到 `[low, high]` 两刻度（复用溢出路径已有的那条回退）。

- [ ] **Step 4: 运行确认通过**

- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 3: 命中测试与索引映射（纯函数）

**Files:**
- Create: `src/Tsp.Desktop/Charts/Layout/ChartHitTest.cs`
- Test: `tests/Tsp.Desktop.Tests/ChartHitTestTests.cs`

**Interfaces:**
- Consumes: `ValueRange`
- Produces: `static class ChartHitTest`，含 `static int? IndexAtX(double x, double plotLeft, double plotWidth, int count)` 与 `static double YFor(double value, ValueRange range, double plotTop, double plotHeight)`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void IndexAtX_maps_plot_left_to_zero_and_plot_right_to_count_minus_one()
[Fact] public void IndexAtX_returns_null_outside_the_plot_area()      // 左越界、右越界、宽度为零
[Fact] public void IndexAtX_returns_null_when_count_is_zero()          // 空数据不得除零
[Fact] public void YFor_inverts_because_screen_y_grows_downward()      // 值越大 y 越小
[Fact] public void YFor_maps_the_range_ends_to_the_plot_edges()
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 4: `ChartSurface` 基座控件

本阶段**最关键的一个文件**——Review Focus 第 2 项的成败就在这里。

**Files:**
- Create: `src/Tsp.Desktop/Charts/ChartSurface.cs`
- Test: `tests/Tsp.Desktop.Tests/ChartSurfaceContractTests.cs`

**Interfaces:**
- Consumes: 无（基座不依赖值域计算）
- Produces: `abstract class ChartSurface : Control`，提供：
  - `StyledProperty<IBrush?> SurfaceBrushProperty` / `BorderBrushProperty` / `AxisBrushProperty` / `LabelBrushProperty`（四类通用）
  - `StyledProperty<IBrush?> PrimaryBrushProperty` / `SecondaryBrushProperty`（主序列色与次要序列色——折线、柱、均线等**一切"数据序列"的颜色都取这两个之一**，避免每个图表各自发明一个画笔名）
  - `StyledProperty<double> PlotPaddingProperty`（默认 8）
  - `protected Rect PlotArea { get; }`——扣除内边距后的绘图矩形
  - `protected abstract void RenderChart(DrawingContext context, Rect plotArea);`
  - `sealed override void Render(DrawingContext context)`——画底/边框，然后调 `RenderChart`，**空数据时直接返回而不画轴**

  派生控件可按需**追加**自己的画笔（`KLineChart` 加 `UpBrush`/`DownBrush`，`DonutChart` 加 `SliceBrushes`），但**通用画笔一律用上面的六个，不得另起名字**。

- [ ] **Step 1: 写失败的测试**

这个类**不实例化控件**也有一半可测：它的静态构造与属性注册可被反射检查，而"哪些属性触发重绘"是可断言的契约。

```csharp
[Theory][InlineData(nameof(ChartSurface.SurfaceBrushProperty))] /* …四个画笔… */
public void Every_brush_property_affects_render(string propertyName)
{
    // 从 AvaloniaPropertyRegistry 取该属性，断言它已注册 AffectsRender
}

[Fact] public void ChartSurface_is_abstract_and_derives_from_Control()
```

（若取 `AffectsRender` 注册状态的公开 API 不可得，改为**在 Task 11/12 用实机验证**替代，并在报告中说明——不要为了可测而给基座加测试专用后门。）

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**

四类画笔与内边距全部注册为 `StyledProperty`，并在静态构造函数里对它们调用 `AffectsRender<ChartSurface>(...)`。**画笔不得在 `RenderChart` 里被赋值**——赋值只发生在样式里（Task 11）。`Render` 在 `DataCount == 0` 时不画任何轴。

- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 5: 绘制辅助与像素对齐

Review Focus 第 4 项的落点。

**Files:**
- Create: `src/Tsp.Desktop/Charts/Layout/PixelAlign.cs`
- Test: `tests/Tsp.Desktop.Tests/PixelAlignTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `static class PixelAlign`，`static double SnapStroke(double coordinate, double thickness)`

- [ ] **Step 1: 写失败的测试**

```csharp
[Theory]
[InlineData(10.0, 1.0, 10.5)]   // 1px 线落在半像素上才锐利
[InlineData(10.0, 2.0, 10.0)]   // 偶数宽不需要偏移
public void SnapStroke_aligns_odd_thickness_to_half_pixels(double input, double thickness, double expected)
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 6: `LineChart`

**Files:**
- Create: `src/Tsp.Desktop/Charts/LineChart.cs`
- Test: `tests/Tsp.Desktop.Tests/LineChartPathTests.cs`

**Interfaces:**
- Consumes: `ChartSurface`、`ValueRange`、`AxisTicks`、`PixelAlign`
- Produces: `sealed class LineChart : ChartSurface`，`StyledProperty<IReadOnlyList<double>?> ValuesProperty`、`bool FillAreaProperty`，以及**纯静态**的 `static IReadOnlyList<Point> BuildPoints(IReadOnlyList<double> values, ValueRange range, Rect plotArea)` 供直接测试

  **调用方义务（Task 3 的契约，必须遵守）**：`ChartHitTest.YFor` 对**非有限值**返回非有限值，作为"这一点不可绘制"的信号——指标预热期会合法地产生 `NaN`。**`BuildPoints` 必须在建几何之前跳过这些点**，判据用 `!double.IsFinite(y)`，**不要**用"是否越出值域"来判（`Infinity` 是非有限的，但不是"比较上越界"）。若照中心点画，会画出一条往中间掉的**假线**——比跳过更糟。这条义务要写进 `BuildPoints` 的 XML 注释。

- [ ] **Step 1: 写失败的测试**

把点的计算抽成纯静态函数，就能不实例化控件地测：

```csharp
[Fact] public void BuildPoints_maps_first_and_last_to_the_plot_edges()
[Fact] public void BuildPoints_on_empty_input_returns_empty()
[Fact] public void BuildPoints_on_a_degenerate_range_centres_the_line()   // 全等值 → 横线居中，不是贴边或越界
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**——`RenderChart` 用 `BuildPoints` + `StreamGeometry` 折线；`FillArea` 时以 `SurfaceBrush` 半透明填充。
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 7: `BarChart`

**Files:**
- Create: `src/Tsp.Desktop/Charts/BarChart.cs`
- Test: `tests/Tsp.Desktop.Tests/BarLayoutTests.cs`

**Interfaces:**
- Consumes: `ChartSurface`、`ValueRange`、`PixelAlign`
- Produces: `sealed class BarChart : ChartSurface`，`StyledProperty<IReadOnlyList<double>?> ValuesProperty`、`double GapRatioProperty`（默认 0.2），纯静态 `static (Rect Bar, double Baseline) LayoutBar(int index, int count, double value, ValueRange range, Rect plotArea, double gapRatio)`

- [ ] **Step 1: 写失败的测试**

Review Focus 第 3 项在此落实：

```csharp
[Fact] public void Positive_and_negative_values_grow_in_opposite_directions_from_the_baseline()
[Fact] public void All_negative_values_still_produce_visible_bars()      // 不得全部塌成零高
[Fact] public void Zero_value_produces_a_zero_height_bar_at_the_baseline()
[Fact] public void Bars_do_not_overlap_and_stay_inside_the_plot_area()
[Fact] public void LayoutBar_on_empty_input_is_never_called()           // 契约：调用方先判空
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**——基线取 `ValueRange` 中 0 的位置（若 0 不在域内则取靠近 0 的边界）。
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 8: `DonutChart`

**Files:**
- Create: `src/Tsp.Desktop/Charts/DonutChart.cs`
- Test: `tests/Tsp.Desktop.Tests/DonutSliceTests.cs`

**Interfaces:**
- Produces: `sealed class DonutChart : ChartSurface`，`StyledProperty<IReadOnlyList<double>?> ValuesProperty`、`StyledProperty<IReadOnlyList<IBrush>?> SliceBrushesProperty`、`double InnerRadiusRatioProperty`（默认 0.6），纯静态 `static IReadOnlyList<(double StartAngle, double SweepAngle)> ComputeSlices(IReadOnlyList<double> values)`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Slices_sum_to_a_full_circle_within_floating_point_tolerance()
[Fact] public void Empty_input_returns_no_slices()
[Fact] public void All_zero_values_return_no_slices_rather_than_dividing_by_zero()  // 关键：不得 NaN
[Fact] public void A_single_value_occupies_the_whole_circle()
[Fact] public void Negative_values_are_ignored_not_reversed()
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**——总计为 0 时返回空；负值按 0 计并在报告中记明该口径。
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 9: `RadarChart`

**Files:**
- Create: `src/Tsp.Desktop/Charts/RadarChart.cs`
- Test: `tests/Tsp.Desktop.Tests/RadarGeometryTests.cs`

**Interfaces:**
- Produces: `sealed class RadarChart : ChartSurface`，`StyledProperty<IReadOnlyList<double>?> ValuesProperty`（每维 0..1 归一）、`StyledProperty<IReadOnlyList<string>?> AxisLabelsProperty`，纯静态 `static IReadOnlyList<Point> ComputeVertices(IReadOnlyList<double> values, Point centre, double radius)`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Three_axes_produce_three_vertices_on_the_outer_ring_when_all_values_are_one()
[Fact] public void Zero_values_collapse_to_the_centre()
[Fact] public void Vertices_stay_within_the_radius_for_in_range_values()
[Fact] public void Empty_input_returns_no_vertices()
[Fact] public void Fewer_than_three_axes_returns_no_vertices()   // 少于 3 维画不成雷达图
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 10: `Heatmap`

**Files:**
- Create: `src/Tsp.Desktop/Charts/Heatmap.cs`
- Test: `tests/Tsp.Desktop.Tests/HeatmapCellTests.cs`

**Interfaces:**
- Produces: `sealed class Heatmap : ChartSurface`，`StyledProperty<IReadOnlyList<double>?> ValuesProperty`、`int ColumnsProperty`，纯静态 `static IReadOnlyList<(Rect Cell, double Normalized)> LayoutCells(IReadOnlyList<double> values, int columns, Rect plotArea)`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact] public void Cells_tile_the_plot_area_without_gaps_or_overlap()
[Fact] public void Normalized_value_is_zero_for_the_minimum_and_one_for_the_maximum()
[Fact] public void Degenerate_range_normalizes_every_cell_to_one_half()   // 全等值 → 中性色，不是除零
[Fact] public void Columns_below_one_are_rejected()
[Fact] public void Empty_input_returns_no_cells()
```

- [ ] **Step 2: 运行确认失败**
- [ ] **Step 3: 实现**——行数由 `(count + columns - 1) / columns` 得出；最后一行的空位不画。
- [ ] **Step 4: 运行确认通过**
- [ ] **Step 5: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 11: 令牌接线与守卫确认

**Files:**
- Modify: `src/Tsp.Desktop/Themes/Controls.axaml`
- Test: `tests/Tsp.Desktop.ViewModels.Tests/ThemeTokenReferenceTests.cs`（**不改**——它是守卫，必须自动通过）

**Interfaces:**
- Consumes: Task 4–10 的全部控件与画笔属性
- Produces: 每个图表控件在样式库里都有一组用 `{DynamicResource}` 赋值的画笔

- [ ] **Step 1: 在 `Controls.axaml` 里为图表控件加样式**

形如：

```xml
<Style Selector="charts|LineChart">
  <Setter Property="SurfaceBrush"   Value="{DynamicResource BaseBrush}" />
  <Setter Property="PrimaryBrush"   Value="{DynamicResource AccentBrush}" />
  <Setter Property="SecondaryBrush" Value="{DynamicResource FgMutedBrush}" />
  <Setter Property="AxisBrush"      Value="{DynamicResource BorderBrush}" />
  <Setter Property="LabelBrush"     Value="{DynamicResource FgMutedBrush}" />
</Style>
```

`KLineChart` 的涨跌画笔**必须**是 `BullBrush` / `BearBrush`（红涨绿跌），其 `PrimaryBrush` 留给均线。**不得**在 C# 里给画笔赋任何字面色值。

- [ ] **Step 2: 确认 P0 的守卫测试仍然通过**

Run: `dotnet test tests/Tsp.Desktop.ViewModels.Tests --filter FullyQualifiedName~ThemeTokenReference`
Expected: 通过，且它扫描到的 `{DynamicResource}` 计数**因本任务而增加**。

若它变红，说明某处写了 `{StaticResource}` 或字面色值——**修代码，不要动守卫**。

- [ ] **Step 3: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 12: 无头渲染验证 + 开发用图表页

**本任务是本阶段验收的落点**，分两层：先建**无头渲染测试宿主**把"切主题重绘"变成可自动验的像素比对，再用开发页在真机上确认六图观感。原计划只有后者（纯手工），那意味着本阶段的核心验收标准**永不受 CI 保护**——这不是这个项目该接受的状态。

**Files:**
- Modify: `tests/Tsp.Desktop.Tests/Tsp.Desktop.Tests.csproj`（加 `Avalonia.Headless` + `Avalonia.Skia`，**只写 `Include`**）
- Create: `tests/Tsp.Desktop.Tests/Charts/HeadlessRenderHarness.cs`（会话/宿主搭建，供各控件渲染测试复用）
- Create: `tests/Tsp.Desktop.Tests/Charts/ChartRenderTests.cs`
- Create: `src/Tsp.Desktop/Views/DevChartsPage.axaml`
- Create: `src/Tsp.Desktop/Views/DevChartsPage.axaml.cs`
- Modify: `src/Tsp.Desktop/Themes/Controls.axaml`（若需要页级样式）
- Modify: `src/Tsp.Desktop.ViewModels/Navigation/Routes.cs`（加 `Routes.DevCharts = "/dev/charts"`，**不入 `Routes.All`**）

**Interfaces:**
- Consumes: 全部六个控件、`ChartSurface` 的六个通用画笔、`Themes/Tokens.axaml` 的两套主题
- Produces: 一个可复用的无头渲染宿主 + 一组像素级断言 + 一个六格开发页

- [ ] **Step 1: 接无头宿主**

把 `Avalonia.Headless` 与 `Avalonia.Skia` 加进测试工程。目标形态：能在测试里把一个控件 `Arrange` 到指定矩形、渲染到 `RenderTargetBitmap`、再读回像素。**不要**引入 `Avalonia.Headless.XUnit`（它依赖 `xunit.v3`，会劈开测试栈）。

- [ ] **Step 2: 写"画笔变了必须重绘"的像素断言（本阶段最关键的一条）**

这是整个 P1-A 的验收核心，也是静态守卫**只能保证"注册了"、不能保证"生效"**的那一半：

```csharp
// 渲染一次 → 改一个画笔 → 再渲染一次 → 两次像素必须不同
// 且把同一控件的 DrawOperation 计数或像素差做成断言，而不是靠人看
[Fact] public void Changing_a_brush_changes_the_rendered_pixels()
[Fact] public void A_null_brush_renders_without_throwing()
[Fact] public void HasData_false_renders_nothing()
```

**证其有牙**：把 `AffectsRender` 里某个画笔删掉（或用错泛型参数），`Changing_a_brush_…` 必须变红——否则这条测试是空转的。

- [ ] **Step 3: 逐控件渲染冒烟**

六个控件各喂一组夹具数据，在无头宿主里渲染成功且**产出的像素非全空**；K 线**涨红跌绿**（逐元素数像素，涨只应出现 `BullBrush`、跌只应出现 `BearBrush`）。空列表输入六个控件都**不抛异常**（Review Focus 1）。

- [ ] **Step 4: 建开发页与路由**

按 P0 的既有做法：路由键加常量、**不进 `Routes.All`**（P0 已有测试钉住 `All` 恰好 19 项），并让它可被 `Routes.Find` 解析；页面**不进侧栏菜单**。这会改到 `Routes.cs` 的两处——spec §5.7 已记明这个双改点，收敛成 `Routes.Registered` 是 P1-B 的活。

- [ ] **Step 5: 实机确认六图**

Run: `dotnet run --project src/Tsp.Desktop`

六个控件渲染出有意义的图形；数字等宽；**图表不在父容器原点也能正确落位**（Task 4 的 Critical 是关于这个的，实机要盖住它：把某一格放进有偏移的网格/有内边距的面板里看）。

- [ ] **Step 6: 实机切换主题，逐图确认**

切明暗主题，六个控件**逐个**确认颜色实时跟随；用像素计数佐证（切换后旧主题专属令牌的像素应为 0）。

- [ ] **Step 7: 提交（**不推送**——推送由控制者在复审通过后执行）**

---

### Task 13: P1-A 验收与文档

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`（若新增了需要记录的形状）
- Modify: `docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md`（§5.5 补记实际落地的基座形状）

**Interfaces:**
- Consumes: Task 1–12 的全部产出
- Produces: P1-A 的可交付状态

- [ ] **Step 1: 跑完整验收**

Run: `dotnet build Tsp.slnx`（0 警告 0 错误）、`dotnet test`（全绿）、`dotnet run --project src/Tsp.Desktop`（六个控件渲染正常、主题重绘正常、空数据不崩）。

- [ ] **Step 2: 更新文档**

README 补「图表控件」一节（六个控件各自的用途与数据形状）；spec §5.5 补记基座的实际形状（`StyledProperty` + `AffectsRender` 的主题跟随机制、数学抽到 `Charts/Layout/` 的原因）。

**不得**宣称尚未实现的能力（P1-B/C 的页面、P2 的真实数据都还不存在）。

- [ ] **Step 3: 版本与提交**

按版本策略：**子计划收口 → 补丁位 +1**（不是次版本；次版本留给 P1 整体收口）。同步四处（props / `BaselineTest` / README / AGENTS.md），提交尾注等于新版本号。

- [ ] **Step 4: 推送**

`git push origin main`。

---

## 附：P1 的其余两篇

| 计划 | 覆盖 | 主要产出 |
|---|---|---|
| P1-B | 视图分发缝 + 核心 8 页 | ViewLocator/`DataTemplate` 选择器、`Routes.Registered` 聚合、看板 / 自选 / 策略 / 因子 / 回测 / 市场环境 / 指数 / 数据 |
| P1-C | 扩展 11 页 + 设置页 | 个股分析 / 连板梯队 / 概念分析 / 行业分析 / 财务分析 / 监控中心 / 异动监控 / 持仓提醒 / 模拟盘 / 信号库 / 复盘 + 设置 |

P1-B 开工前必须先落 **ViewLocator**——壳里现在直接实例化 `PlaceholderPage`，不先解决这条，写第二页时就会被迫做出错误的结构。
