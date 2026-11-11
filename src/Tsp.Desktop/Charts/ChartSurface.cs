using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tsp.Desktop.Charts;

/// <summary>
/// 自绘图表的基座：持有<b>外框</b>（底色、边框、绘图区、空数据短路），把<b>框里的东西</b>留给派生控件。
/// </summary>
/// <remarks>
/// <para>
/// <b>主题跟随机制（本类型存在的理由，Review Focus 第 2 项）。</b>
/// 画笔一律声明为 <see cref="StyledProperty{T}"/>，并在静态构造函数里挂上
/// <see cref="Visual.AffectsRender{T}"/>；赋值只发生在 <c>Themes/Controls.axaml</c> 的
/// <c>&lt;Style&gt;</c> setter 里，用 <c>{DynamicResource}</c> 取令牌。
/// 于是切换主题时资源被重新解析 → 属性变更 → <c>AffectsRender</c> 触发 <c>InvalidateVisual()</c> → 重绘。
/// </para>
/// <para>
/// <b>绝对不许在渲染期读画笔</b>：<c>TryFindResource</c> / <c>FindResource</c> / 遍历
/// <c>Application.Resources</c> 都拿不到 <c>{DynamicResource}</c> 的实时值，而且这类查找
/// <b>绕得过 P0 的令牌守卫</b>（那条守卫只静态扫描 <c>.axaml</c> 文件，看不见运行期查找）。
/// 后果是静默的、还看主题脸色：控制台不报错、构建不警告，界面照常渲染，只是颜色停在上一套主题。
/// P0 为同一个坑花过力气（<c>{StaticResource}</c> 在加载时把色值烤死），别再走回去。
/// </para>
/// <para>
/// <b>六个通用画笔是唯一的命名来源</b>：<see cref="SurfaceBrush"/>（底）、<see cref="BorderBrush"/>（边框）、
/// <see cref="AxisBrush"/>（坐标轴）、<see cref="LabelBrush"/>（刻度文字）、
/// <see cref="PrimaryBrush"/> / <see cref="SecondaryBrush"/>（<b>一切"数据序列"的颜色都取这两个之一</b>：
/// 折线、柱、均线……）。派生控件可以<b>追加</b>自己的画笔（<c>KLineChart</c> 的涨跌色、
/// <c>DonutChart</c> 的分片色），但<b>不得改名或复刻这六个</b>——否则每个图表各自发明画笔名，
/// Task 11 的样式库与 P0 的令牌守卫就同时失去了单一事实源。
/// </para>
/// <para>
/// <b>画笔为 <c>null</c> 是合法状态，含义是"这一层不画"</b>，不是黑色、更不是异常。
/// Task 11 落地样式之前六个画笔全是 <c>null</c>，控件必须在这个状态下正常渲染；
/// 这也让"有人忘了写样式"表现为少画一层，而不是整窗白屏。
/// </para>
/// <para>
/// <b>配色只在样式里出现。</b>本文件（以及任何派生控件）<b>不得</b>给画笔赋字面色值——
/// 连 <c>Colors.Red</c> 这样的默认值都不行，那会让令牌库名存实亡。
/// </para>
/// </remarks>
public abstract class ChartSurface : Control
{
    /// <summary>底色画笔（控件整块面板的底）。由样式以 <c>{DynamicResource}</c> 赋值；<c>null</c> 时不画。</summary>
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(SurfaceBrush));

    /// <summary>边框画笔。由样式以 <c>{DynamicResource}</c> 赋值；<c>null</c> 时不画。</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(BorderBrush));

    /// <summary>坐标轴画笔（轴线、网格线、刻度线）。由样式以 <c>{DynamicResource}</c> 赋值；<c>null</c> 时不画。</summary>
    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(AxisBrush));

    /// <summary>刻度文字画笔。由样式以 <c>{DynamicResource}</c> 赋值；<c>null</c> 时不画。</summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(LabelBrush));

    /// <summary>
    /// 主数据序列色（折线、主柱、均线……）。一切"数据序列"的颜色都取它与 <see cref="SecondaryBrush"/> 之一，
    /// 不许派生控件另起名字。由样式以 <c>{DynamicResource}</c> 赋值；<c>null</c> 时不画。
    /// </summary>
    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(PrimaryBrush));

    /// <summary>次要数据序列色（第二条折线、基准线、参照序列……）。由样式以 <c>{DynamicResource}</c> 赋值；<c>null</c> 时不画。</summary>
    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(SecondaryBrush));

    /// <summary>绘图区内边距（四边各让出的像素数），默认 8。</summary>
    public static readonly StyledProperty<double> PlotPaddingProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(PlotPadding), 8d);

    static ChartSurface()
    {
        // 画笔与内边距任一变化都要重绘：画笔决定颜色，内边距决定绘图几何。
        // 这一句是"切主题图表实时变色"的全部机关——少了它，<Style> setter 照旧会写进属性，
        // 但画布不会重新画，颜色就停在上一套主题上，且没有任何报错。
        AffectsRender<ChartSurface>(
            SurfaceBrushProperty,
            BorderBrushProperty,
            AxisBrushProperty,
            LabelBrushProperty,
            PrimaryBrushProperty,
            SecondaryBrushProperty,
            PlotPaddingProperty);
    }

    /// <summary>底色画笔。赋值只发生在样式库里。</summary>
    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    /// <summary>边框画笔。赋值只发生在样式库里。</summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>坐标轴画笔。赋值只发生在样式库里。</summary>
    public IBrush? AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    /// <summary>刻度文字画笔。赋值只发生在样式库里。</summary>
    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    /// <summary>主数据序列色。赋值只发生在样式库里。</summary>
    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    /// <summary>次要数据序列色。赋值只发生在样式库里。</summary>
    public IBrush? SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    /// <summary>
    /// 绘图区内边距，默认 8。负值与非有限值按 0 处理（见 <see cref="PlotArea"/>）。
    /// 赋值只发生在样式库或页面里。
    /// </summary>
    public double PlotPadding
    {
        get => GetValue(PlotPaddingProperty);
        set => SetValue(PlotPaddingProperty, value);
    }

    /// <summary>
    /// 扣除 <see cref="PlotPadding"/> 之后的绘图矩形，派生控件据此把数据映射到像素。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>坐标系是控件自己的局部坐标</b>：原点在控件左上角，与 <see cref="Render"/> 拿到的
    /// <see cref="DrawingContext"/> 一致。因此 <see cref="PlotArea"/> 的左上角恒为
    /// <c>(PlotPadding, PlotPadding)</c>，与控件在父容器里的位置无关——<b>绝不能</b>把
    /// <see cref="Visual.Bounds"/> 的 <c>X</c> / <c>Y</c> 加进来：那两个值是**相对父控件**的，
    /// 加进来会让所有不在父容器原点上的图表整体偏移（详见 <see cref="Render"/> 的文档）。
    /// </para>
    /// <para>
    /// <b>尺寸恒非负</b>。控件还小（或还没排过版，<c>Bounds</c> 为 0）时，四边各让出内边距会算负宽高；
    /// 负尺寸的 <see cref="Rect"/> 会让几何运算给出反直觉的结果（比较、包含、求交都不再符合直觉），
    /// 所以一律夹到 0。零尺寸的含义是<b>"这块地方画不出来"</b>——渲染方据此直接收手即可。
    /// 此时矩形的原点仍是 <c>(PlotPadding, PlotPadding)</c>：控件比两倍内边距还小时它会落在控件之外，
    /// 但零尺寸下位置没有意义，别拿它去做判断，要看就看尺寸。
    /// </para>
    /// <para>
    /// <b><see cref="PlotPadding"/> 为负或非有限值时按 0 处理。</b>这不是"友善的容错"而是必须：
    /// 负内边距会把绘图区撑得比控件还大，<c>NaN</c> 则会让上面两处减法产出 <c>NaN</c> 尺寸的矩形，
    /// 而 <see cref="Rect"/> 一旦带毒，后面每一次几何运算都在无声地传毒。这个方法跑在
    /// <c>Render()</c> 的路径上，那里抛异常就是整窗白屏——所以它<b>只做裁剪，不做校验、不抛异常</b>。
    /// </para>
    /// </remarks>
    protected Rect PlotArea
    {
        get
        {
            double padding = PlotPadding;
            if (!double.IsFinite(padding) || padding < 0d)
            {
                padding = 0d;
            }

            return new Rect(
                padding,
                padding,
                Math.Max(0d, Bounds.Width - (2d * padding)),
                Math.Max(0d, Bounds.Height - (2d * padding)));
        }
    }

    /// <summary>
    /// 本控件当前<b>有没有可画的数据</b>，缺省 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 基座不持有数据（数据在各派生控件的 <c>ValuesProperty</c> 之类上），所以它无法自己判断空不空，
    /// 只能问派生控件。派生控件覆盖它来报告自己的序列是否为空——数据为空时
    /// <see cref="Render"/> 会整块收手，<b>底色、边框、坐标轴一概不画</b>：
    /// 没有数据却画出一套刻度，比什么都不画更容易被误读成"数据全是 0"。
    /// </remarks>
    protected virtual bool HasData => true;

    /// <summary>
    /// 画绘图区<b>里</b>的东西。由派生控件实现，坐标系是 <paramref name="plotArea"/> 自身的像素坐标。
    /// </summary>
    /// <param name="context">绘制上下文，由 <see cref="Render"/> 传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，<b>尺寸可能为 0</b>（此时直接收手）。</param>
    /// <remarks>
    /// 底色、边框、空数据短路、绘图区计算都已在 <see cref="Render"/> 里做完，这里只管框里的内容。
    /// <b>不许在这里给画笔属性赋值</b>：画笔的值只由样式库给出，在渲染路径上写属性会既触发重绘
    /// 又让"主题跟随"这条链路无从追查。
    /// </remarks>
    protected abstract void RenderChart(DrawingContext context, Rect plotArea);

    /// <summary>
    /// 画整个控件：空数据短路 → 底色 → 边框 → 交给 <see cref="RenderChart"/>。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <remarks>
    /// <para>
    /// <b>本方法封死</b>（<c>sealed override</c>）：外框是基座的职责，派生控件只提供
    /// <see cref="RenderChart"/>。这样"空数据不画轴"这条契约由基座一次性保证，
    /// 派生控件既插不进手，也不可能漏掉。
    /// </para>
    /// <para>
    /// <b>空数据时连底与边框都不画</b>——<see cref="HasData"/> 的判定排在所有绘制之前。
    /// </para>
    /// <para>
    /// 边框的 <see cref="Pen"/> 每帧现建而不是缓存：画笔会随主题变，缓存的笔一旦没跟着失效，
    /// 就会变成"切主题后边框还留着旧颜色"这类查不出来的静默故障。现建把跟随做成了结构上的必然，
    /// 一帧一支笔的开销在图表的几何计算面前可以忽略。
    /// </para>
    /// <para>
    /// <b>底与边框都用局部坐标 <c>(0, 0, 宽, 高)</c>（即 <c>new Rect(Bounds.Size)</c>）</b>——
    /// <see cref="Visual.Bounds"/> 的 <c>X</c> / <c>Y</c> 是<b>相对父控件</b>的位置，而这里的
    /// <see cref="DrawingContext"/> 已经被父级平移到本控件的原点，两者不在一个坐标系里。
    /// 把 <c>Bounds</c> 整块递给绘制调用，会让所有不在父容器原点上的图表（格子里的、第二行的、
    /// 带内边距的面板里的——真实布局里的每一个）整体偏移，而恰好摆在原点上的那些看起来完全正常。
    /// Avalonia 自己的 <c>Border</c> / <c>Panel</c> / <c>Image</c> 在 <c>Render</c> 里也都只读
    /// <c>Bounds.Size</c>。<b>本类只允许读 <c>Bounds</c> 的尺寸</b>（<c>Size</c> / <c>Width</c> /
    /// <c>Height</c>），这条纪律由 <c>ChartsSourceGuardTests</c> 静态守卫。
    /// 描边为 1px、居中于矩形边线（一半压在控件外缘），像素对齐用的是 Task 5 的 <c>PixelAlign</c>，
    /// 届时再统一收口。
    /// </para>
    /// </remarks>
    public sealed override void Render(DrawingContext context)
    {
        if (!HasData)
        {
            return;
        }

        Rect plotArea = PlotArea;

        // 控件的整块矩形，**局部坐标**（原点在控件左上角），所以是 (0, 0, 宽, 高)。
        // 只能用 Bounds.Size：Bounds 的 X/Y 是相对父控件的，见 Render 的文档。
        Rect controlBox = new(Bounds.Size);

        if (SurfaceBrush is { } surfaceBrush)
        {
            context.FillRectangle(surfaceBrush, controlBox);
        }

        if (BorderBrush is { } borderBrush)
        {
            context.DrawRectangle(new Pen(borderBrush), controlBox);
        }

        RenderChart(context, plotArea);
    }
}
