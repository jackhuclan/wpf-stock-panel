using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Tsp.Desktop.Charts;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="ChartSurface"/> 的基座契约：六个通用画笔、内边距、绘图矩形与渲染骨架。
/// </summary>
/// <remarks>
/// <para>
/// <b>这份用例刻意不把控件真的画出来</b>：仓库的测试栈不含 Avalonia Headless
/// （<c>Avalonia.Headless.XUnit</c> 在 12.1.6 依赖 <c>xunit.v3</c>，会让测试栈分裂，P0 已裁定不引入），
/// 而 <c>DrawingContext</c> 的构造函数是私有的、无法在测试里伪造，所以
/// <b>「画笔为 null 时不抛异常」「空数据不画轴」这类渲染行为在本工程里无法断言</b>，
/// 只能靠实机验证（Task 12 第 4 步）。本文件钉的是<b>结构契约与不依赖渲染的行为</b>，
/// 边界写得很清楚，免得日后有人把「这里没测」误读成「这里没问题」。
/// </para>
/// <para>
/// <b><c>AffectsRender</c> 的注册状态没有公开查询接口</b>（Avalonia 12.1.6 的
/// <c>AvaloniaProperty</c> / <c>AvaloniaPropertyRegistry</c> 公开成员里没有任何与渲染联动相关的
/// 查询——<c>AffectsRender</c> 是 <c>Visual</c> 的 protected static，注册结果落在
/// <c>LightweightSubject</c> 的私有订阅表里）。所以这里分两层处理，同一件事<b>没有任何一层</b>
/// 靠反射 Avalonia 的私有字段去断言——那种替身换个版本就会静默失真，比不测更坏：
/// <b>「登记有没有写」由 <c>ChartsSourceGuardTests</c> 静态扫源码守卫</b>（它在 CI 里跑），
/// <b>「登记了到底会不会重绘」只能靠 Task 12 实机验证</b>（它是手动的，不会在 CI 里跑）。
/// 本条用例只负责"这六个属性存在、是 <c>StyledProperty</c>、注册在 <c>ChartSurface</c> 名下"。
/// </para>
/// <para>
/// <b>可以在无 UI 线程下做的事</b>（本文件用的就是这些）：反射取类型与其静态字段、
/// 从 <see cref="AvaloniaPropertyRegistry"/> 取注册信息、<c>Activator</c> / <c>new</c> 出一个
/// 派生自 <see cref="ChartSurface"/> 的测试替身、读写它的 <c>StyledProperty</c>、
/// 以及用 <c>Arrange</c> 给它一个尺寸（<c>Bounds</c> 是只读属性，只能这么给）。
/// </para>
/// </remarks>
public sealed class ChartSurfaceContractTests
{
    /// <summary>六个通用画笔的属性名——**唯一的画笔命名来源**，派生控件不得另起名字。</summary>
    private static readonly string[] _commonBrushNames = ["SurfaceBrush", "BorderBrush", "AxisBrush", "LabelBrush", "PrimaryBrush", "SecondaryBrush"];

    /// <summary>
    /// 一个不覆盖任何成员的最小派生类：用来观察基座的默认行为
    /// （默认画笔值、默认内边距、<see cref="ChartSurface.HasData"/> 的缺省口径）。
    /// </summary>
    private sealed class BareSurface : ChartSurface
    {
        // 受保护成员的名字只能在派生类里用 nameof 取到（在测试类里写 nameof(ChartSurface.PlotArea) 是不合法的）。
        // 在这里取一次，既是编译期安全的字符串，也顺带钉住"这几个成员确实在派生类里可见"。
        public const string HasDataName = nameof(HasData);

        public const string PlotAreaName = nameof(PlotArea);

        public const string RenderChartName = nameof(RenderChart);

        public bool ExposedHasData => HasData;

        protected override void RenderChart(DrawingContext context, Rect plotArea)
        {
            // 渲染行为（画笔为 null 不抛、空数据不画轴）在本工程里跑不到这里——见类注释。
        }
    }

    /// <summary>
    /// 把 <see cref="ChartSurface"/> 的受保护成员暴露给用例的替身。抽象基类本来就必须派生才能实例化，
    /// 这里只是顺手把受保护成员读出来——**基座本身没有为测试开任何口子**。
    /// </summary>
    private sealed class ExposedSurface : ChartSurface
    {
        public bool HasDataOverride { get; set; } = true;

        public bool ExposedHasData => HasData;

        public Rect ExposedPlotArea => PlotArea;

        protected override bool HasData => HasDataOverride;

        protected override void RenderChart(DrawingContext context, Rect plotArea)
        {
        }
    }

    [Fact]
    public void ChartSurface_is_abstract_and_derives_from_Control()
    {
        // 它是"一切图表的基座"这条定位在类型系统上的体现：抽象（只能派生）且是 Avalonia 的 Control。
        Assert.True(typeof(ChartSurface).IsAbstract);
        Assert.True(typeof(Control).IsAssignableFrom(typeof(ChartSurface)));
        Assert.Equal(typeof(Control), typeof(ChartSurface).BaseType);
    }

    [Fact]
    public void The_only_brush_properties_are_the_six_common_ones()
    {
        // 命名收敛的守卫。派生控件（KLineChart 的 Up/Down、DonutChart 的 SliceBrushes）只能**追加**
        // 自己的画笔，不得改名或复刻这六个——否则每个图表各自发明画笔名，
        // Task 11 的样式库与 P0 的令牌守卫就都失去了单一事实源。
        EnsureTypeInitialized();

        List<string> brushNames = [.. AvaloniaPropertyRegistry.Instance
            .GetRegistered(typeof(ChartSurface))
            .Where(property => property.OwnerType == typeof(ChartSurface))
            .Where(property => property.PropertyType == typeof(IBrush))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal([.. _commonBrushNames.OrderBy(name => name, StringComparer.Ordinal)], brushNames);
    }

    [Theory]
    [InlineData(nameof(ChartSurface.SurfaceBrushProperty))]
    [InlineData(nameof(ChartSurface.BorderBrushProperty))]
    [InlineData(nameof(ChartSurface.AxisBrushProperty))]
    [InlineData(nameof(ChartSurface.LabelBrushProperty))]
    [InlineData(nameof(ChartSurface.PrimaryBrushProperty))]
    [InlineData(nameof(ChartSurface.SecondaryBrushProperty))]
    public void Every_brush_is_a_styled_brush_property_registered_on_ChartSurface(string fieldName)
    {
        // 画笔必须是 StyledProperty<IBrush?>：只有 StyledProperty 才能在 Themes/Controls.axaml 里
        // 用 <Setter> + {DynamicResource} 赋值，主题一变 setter 就重算——这是"切主题图表跟着变色"
        // 的唯一一条路。DirectProperty 与普通 CLR 属性都不行。
        //
        // AffectsRender 的**登记**由 ChartsSourceGuardTests 静态扫源码守卫，
        // 它的**运行期效果**由 Task 12 实机验证；两者都不在本条用例里（见类注释）。
        EnsureTypeInitialized();

        FieldInfo field = typeof(ChartSurface).GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"ChartSurface 上没有公开静态字段 {fieldName}。");

        AvaloniaProperty property = Assert.IsAssignableFrom<AvaloniaProperty>(field.GetValue(null));
        Assert.IsAssignableFrom<StyledProperty<IBrush>>(property);
        Assert.Equal(typeof(IBrush), property.PropertyType);

        // 属性名 = 字段名去掉 Property 后缀。样式里的 <Setter Property="SurfaceBrush"> 走的就是它。
        string expectedName = fieldName[..^"Property".Length];
        Assert.Equal(expectedName, property.Name);
        Assert.Contains(expectedName, _commonBrushNames);

        // 注册在 ChartSurface 自己的名下（不是继承来的、也不是挂到别的类型上）。
        Assert.Equal(typeof(ChartSurface), property.OwnerType);
        Assert.Same(property, AvaloniaPropertyRegistry.Instance.FindRegistered(typeof(ChartSurface), expectedName));
    }

    [Fact]
    public void PlotPadding_is_a_double_styled_property()
    {
        Assert.IsAssignableFrom<StyledProperty<double>>(ChartSurface.PlotPaddingProperty);
        Assert.Equal(typeof(double), ChartSurface.PlotPaddingProperty.PropertyType);
        Assert.Equal(nameof(ChartSurface.PlotPadding), ChartSurface.PlotPaddingProperty.Name);
        Assert.Equal(typeof(ChartSurface), ChartSurface.PlotPaddingProperty.OwnerType);
    }

    [Fact]
    public void A_fresh_surface_defaults_to_the_brief_values()
    {
        // 实例化本身也是断言的一部分：任何 Avalonia 控件在无 UI 线程下能被构造出来，
        // 说明基座的静态构造与属性注册没有偷偷依赖平台。
        BareSurface surface = new();

        Assert.Equal(8d, surface.PlotPadding);

        // Task 11 之前样式还不存在，六个画笔一律为 null——控件必须在这个状态下不崩（渲染侧实机验证）。
        Assert.Null(surface.SurfaceBrush);
        Assert.Null(surface.BorderBrush);
        Assert.Null(surface.AxisBrush);
        Assert.Null(surface.LabelBrush);
        Assert.Null(surface.PrimaryBrush);
        Assert.Null(surface.SecondaryBrush);

        // 基座自己不知道有没有数据，缺省按"有"处理；派生控件覆盖 HasData 来报告自己的序列。
        Assert.True(surface.ExposedHasData);
    }

    [Fact]
    public void Brushes_round_trip_through_the_styled_property()
    {
        // 样式赋值走的就是这条路：setter 写进去，RenderChart 里读出来。
        BareSurface surface = new();
        SolidColorBrush brush = new(Colors.Red);

        surface.SurfaceBrush = brush;
        surface.PrimaryBrush = brush;

        Assert.Same(brush, surface.SurfaceBrush);
        Assert.Same(brush, surface.PrimaryBrush);
    }

    [Fact]
    public void PlotArea_is_independent_of_the_arrange_origin()
    {
        // 回归钉子（复审 Critical）：Visual.Bounds 的 X/Y 是**相对父控件**的（布局留给它的位置），
        // 而 Render 拿到的 DrawingContext 用的是**控件自己的局部坐标系**（原点在控件左上角）。
        // Avalonia 自己的 Border / Panel / Image 在 Render 里都只读 Bounds.Size，从不读 X/Y。
        //
        // 所以把 Bounds.X / Bounds.Y 夹进绘图区，会让**凡是不在父容器原点上的**图表
        // （格子里的、第二行的、带内边距的面板里的——也就是真实布局里的每一个）整体偏移，
        // 而恰好摆在原点上的图表看起来完全正常。原用例清一色 Arrange(0, 0, …)，
        // 正好只覆盖"看起来正常"的那一种，因此这个 bug 能活过整个渲染体变异集。
        ExposedSurface surface = new();
        surface.Arrange(new Rect(11d, 22d, 100d, 50d));

        Assert.Equal(new Rect(8d, 8d, 84d, 34d), surface.ExposedPlotArea);
    }

    [Fact]
    public void PlotArea_deducts_the_padding_on_every_side()
    {
        // 绘图区 = 控件矩形四边各让出 PlotPadding。这是 RenderChart 唯一的坐标系来源，
        // 算错一格就是整张图偏移。
        ExposedSurface surface = new();
        surface.Arrange(new Rect(0d, 0d, 100d, 50d));

        Assert.Equal(new Rect(8d, 8d, 84d, 34d), surface.ExposedPlotArea);
    }

    [Fact]
    public void PlotArea_follows_a_changed_padding()
    {
        // 同样刻意用一个非零的 Arrange 原点：绘图区只看尺寸，不看控件在父容器里的位置。
        ExposedSurface surface = new();
        surface.Arrange(new Rect(11d, 22d, 100d, 50d));
        surface.PlotPadding = 0d;

        Assert.Equal(new Rect(0d, 0d, 100d, 50d), surface.ExposedPlotArea);

        surface.PlotPadding = 20d;

        Assert.Equal(new Rect(20d, 20d, 60d, 10d), surface.ExposedPlotArea);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(4d, 9d)]
    [InlineData(8d, 8d)]
    [InlineData(15d, 20d)]
    public void PlotArea_never_has_a_negative_size(double width, double height)
    {
        // 小尺寸下减去 2 × 8 会算出负宽高。负尺寸的 Rect 会让几何运算行为不定
        // （比较、包含、求交都会给出反直觉的结果），所以一律夹到 0——
        // 宁可给 RenderChart 一个"画不出来"的零尺寸矩形，也不给一个负的。
        ExposedSurface surface = new();
        surface.Arrange(new Rect(0d, 0d, width, height));

        Rect plotArea = surface.ExposedPlotArea;

        Assert.True(plotArea.Width >= 0d, $"宽度成了 {plotArea.Width}。");
        Assert.True(plotArea.Height >= 0d, $"高度成了 {plotArea.Height}。");
    }

    [Theory]
    [InlineData(-8d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PlotArea_treats_a_bogus_padding_as_zero(double padding)
    {
        // 负内边距会把绘图区撑得比控件还大，非有限值会让减法算出 NaN 尺寸。
        // 两者都按 0 处理：Render() 路径上只能裁剪，不能抛异常，更不能把毒值往下游传。
        ExposedSurface surface = new();
        surface.Arrange(new Rect(0d, 0d, 100d, 50d));
        surface.PlotPadding = padding;

        Rect plotArea = surface.ExposedPlotArea;

        Assert.Equal(new Rect(0d, 0d, 100d, 50d), plotArea);
        Assert.True(double.IsFinite(plotArea.Width), $"宽度成了 {plotArea.Width}。");
        Assert.True(double.IsFinite(plotArea.Height), $"高度成了 {plotArea.Height}。");
    }

    [Fact]
    public void HasData_is_an_overridable_protected_hook()
    {
        // 基座不持有数据，所以"空数据不画轴"必须由派生控件通过这个钩子报告。
        PropertyInfo hasData = typeof(ChartSurface).GetProperty(BareSurface.HasDataName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("ChartSurface 上没有 protected HasData。");

        Assert.Equal(typeof(bool), hasData.PropertyType);
        Assert.NotNull(hasData.GetMethod);
        Assert.True(hasData.GetMethod!.IsFamily, "HasData 必须是 protected。");
        Assert.True(hasData.GetMethod.IsVirtual && !hasData.GetMethod.IsFinal, "HasData 必须可被派生控件覆盖。");

        // 覆盖生效（替身报 false 时基座就该认账）。
        ExposedSurface surface = new() { HasDataOverride = false };

        Assert.False(surface.ExposedHasData);
    }

    [Fact]
    public void PlotArea_is_a_read_only_protected_rect()
    {
        PropertyInfo plotArea = typeof(ChartSurface).GetProperty(BareSurface.PlotAreaName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("ChartSurface 上没有 protected PlotArea。");

        Assert.Equal(typeof(Rect), plotArea.PropertyType);
        Assert.True(plotArea.GetMethod!.IsFamily, "PlotArea 必须是 protected。");
        Assert.Null(plotArea.SetMethod);
    }

    [Fact]
    public void Render_is_sealed_and_RenderChart_is_the_abstract_inner_step()
    {
        // 基座独占外框（底 / 边框 / 绘图区 / 空数据短路），派生控件只负责框里的东西——
        // 这条分工靠 sealed Render + abstract RenderChart 钉死。
        MethodInfo render = typeof(ChartSurface).GetMethod(nameof(ChartSurface.Render), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("ChartSurface 上没有 Render。");

        Assert.True(render.IsVirtual && render.IsFinal, "Render 必须是 sealed override。");
        Assert.Equal(typeof(void), render.ReturnType);
        Assert.Equal([typeof(DrawingContext)], render.GetParameters().Select(parameter => parameter.ParameterType));

        MethodInfo renderChart = typeof(ChartSurface).GetMethod(BareSurface.RenderChartName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("ChartSurface 上没有 RenderChart。");

        Assert.True(renderChart.IsAbstract, "RenderChart 必须是 abstract。");
        Assert.True(renderChart.IsFamily, "RenderChart 必须是 protected。");
        Assert.False(renderChart.IsPublic);
        Assert.Equal(typeof(void), renderChart.ReturnType);
        Assert.Equal(
            [typeof(DrawingContext), typeof(Rect)],
            renderChart.GetParameters().Select(parameter => parameter.ParameterType));
    }

    /// <summary>
    /// 跑一遍 <see cref="ChartSurface"/> 的静态构造函数。
    /// 光取 <see cref="Type"/> 是不会触发静态构造的，而属性注册（<c>AvaloniaProperty.Register</c>）恰恰发生在那里——
    /// 不先跑一遍，<see cref="AvaloniaPropertyRegistry"/> 里就什么都查不到。
    /// </summary>
    private static void EnsureTypeInitialized() =>
        RuntimeHelpers.RunClassConstructor(typeof(ChartSurface).TypeHandle);
}
