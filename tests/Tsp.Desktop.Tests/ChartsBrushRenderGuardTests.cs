using System.Text;
using System.Text.RegularExpressions;
using Tsp.Desktop.Charts;
using Tsp.Desktop.ViewModels.Tests;

namespace Tsp.Desktop.Tests;

/// <summary>
/// 画笔-重绘登记守卫：<c>src/Tsp.Desktop/Charts/</c> 下**每个**声明了画笔的类，都必须把那些画笔
/// 登记进 <c>AffectsRender&lt;该类&gt;(…)</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要一条只会读源码的守卫。</b>"切换主题图表实时变色"是本阶段的验收标准，而它全靠
/// 「画笔是 <c>StyledProperty</c> + 静态构造里挂 <c>AffectsRender</c>」这一条链路。
/// <c>AffectsRender</c> 的注册状态<b>没有公开查询接口</b>（Avalonia 12.1.6 的
/// <c>AvaloniaProperty</c> / <c>AvaloniaPropertyRegistry</c> 公开成员里没有任何与渲染联动相关的
/// 查询，它是 <c>Visual</c> 的 protected static，注册结果落在 <c>LightweightSubject</c> 的私有订阅表里），
/// 而 <c>DrawingContext</c> 的构造函数不可访问、测试里造不出渲染上下文——于是
/// <b>漏登记这件事在运行期没有任何信号</b>：不抛异常、不给构建警告、界面照常渲染，
/// 只是颜色停在上一套主题。它正是 P0 起连着四个任务在追的那类静默失效。
/// </para>
/// <para>
/// <b>和 Task 12 的实机验证分工不同，两者都要。</b>实机验证证明这条链路<b>能工作</b>；
/// 本条守卫防止它被<b>悄悄拆掉</b>——实机验证是手动的，不会在 CI 里跑，删掉一行
/// <c>AffectsRender</c> 参数不会让任何自动化变红，直到有人在暗色主题下盯着一张没变色的图。
/// </para>
/// <para>
/// <b>为什么扫整个命名空间而不是只扫基座。</b>Task 6–10 的派生控件各自会加画笔
/// （<c>KLineChart</c> 的涨跌色、<c>DonutChart</c> 的分片色……），每一个都需要同一次登记，
/// 只守基座等于留下九次犯同一个错而无人察觉的机会。这条守卫把
/// 「声明了画笔却没触发重绘」变成**任何文件里都成立**的违规。
/// </para>
/// <para>
/// 与 P0 的 <c>ThemeTokenReferenceTests</c> 同一种做法（那条也是静态扫描 <c>.axaml</c> 源文件，
/// 不加载 Avalonia）；仓库根的定位复用它的 <c>RepoRoot</c>，不写死绝对路径。
/// </para>
/// </remarks>
public sealed class ChartsBrushRenderGuardTests
{
    /// <summary>图表控件所在的目录（相对仓库根）。</summary>
    private const string ChartsRelativePath = "src/Tsp.Desktop/Charts";

    /// <summary>
    /// 六个通用画笔的静态字段名。<b>用 <c>nameof</c> 钉住</b>：基座一旦改名，这条守卫先编译失败。
    /// </summary>
    private static readonly string[] _commonBrushFieldNames =
    [
        nameof(ChartSurface.SurfaceBrushProperty),
        nameof(ChartSurface.BorderBrushProperty),
        nameof(ChartSurface.AxisBrushProperty),
        nameof(ChartSurface.LabelBrushProperty),
        nameof(ChartSurface.PrimaryBrushProperty),
        nameof(ChartSurface.SecondaryBrushProperty),
    ];

    private static readonly Regex _styledPropertyKeyword = new(@"StyledProperty\s*<", RegexOptions.Compiled);

    private static readonly Regex _affectsRenderKeyword = new(@"AffectsRender\s*<", RegexOptions.Compiled);

    /// <summary>匹配一段文本末尾的标识符（用于取"类型之后、<c>=</c> 之前"的那个属性名）。</summary>
    private static readonly Regex _identifierAtEnd = new(@"(?<name>[A-Za-z_]\w*)\s*\z", RegexOptions.Compiled);

    [Fact]
    public void Every_brush_property_under_Charts_is_registered_with_AffectsRender()
    {
        List<string> files = ChartSourceFiles();
        List<string> violations = [];
        HashSet<string> brushesSeen = new(StringComparer.Ordinal);

        foreach (string file in files)
        {
            string relative = Relative(file);

            // 先剥掉注释再解析：注释里的 AffectsRender<…> 字样（本仓库的文档注释里就有）不该
            // 被当成登记，而被注释掉的一行画笔也不该还算登记——剥注释让这两种假绿都不成立。
            string source = StripComments(File.ReadAllText(file));

            List<StyledPropertyDeclaration> brushes = [.. DeclaredStyledProperties(source).Where(IsBrush)];
            if (brushes.Count == 0)
            {
                continue;
            }

            // 同一个文件里可能不止一次 AffectsRender 调用；"登记没登记"按文件口径判定，
            // 于是把该文件所有调用的实参文本并起来看。
            string registered = string.Join("\n", AffectsRenderArguments(source));

            foreach (StyledPropertyDeclaration brush in brushes)
            {
                brushesSeen.Add(brush.Name);

                // 按标识符匹配而不是精确字符串：换行、缩进、参数顺序、注释都不影响判定，
                // 而从调用里**删掉**一个画笔一定判定失败。
                if (!Regex.IsMatch(registered, $@"\b{Regex.Escape(brush.Name)}\b"))
                {
                    violations.Add($"  {relative}: {brush.Name}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "下列画笔属性没有出现在同文件的 AffectsRender<…>(…) 调用里——它变色时不会触发重绘，"
                + "切主题后颜色会停在上一套，而且没有任何运行期信号。"
                + Environment.NewLine
                + "修法：把它加进所在类的静态构造函数里的 AffectsRender<那个类>(…) 调用。"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));

        // 反面守卫：上面那条若是因为"一个文件都没扫到""属性声明一个都没解析出来"而全绿，
        // 那是假绿——守卫得先证明自己真的看过了东西。
        Assert.True(files.Count > 0, $"{ChartsRelativePath} 下一个 .cs 文件都没扫到。");
        Assert.True(
            brushesSeen.Count >= _commonBrushFieldNames.Length,
            $"只解析出 {brushesSeen.Count} 个画笔属性，低于六个通用画笔这条底线——属性声明的解析可能失效了。");

        foreach (string commonBrush in _commonBrushFieldNames)
        {
            Assert.Contains(commonBrush, brushesSeen);
        }
    }

    /// <summary>
    /// 判定一条 <c>StyledProperty</c> 声明是不是"画笔"，两条判据取<b>并集</b>：
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1) 名字以 <c>BrushProperty</c> 收尾（命名口径，也是简报里的说法）；
    /// </para>
    /// <para>
    /// 2) 声明的**值类型里出现 <c>IBrush</c>**。这一条是刻意放宽的：<c>DonutChart</c> 的分片色是
    /// <c>StyledProperty&lt;IReadOnlyList&lt;IBrush&gt;?&gt; SliceBrushesProperty</c>，
    /// 它的名字<b>不以 <c>BrushProperty</c> 收尾</b>（是 <c>Brushes</c>），只按名字守会把它漏掉——
    /// 而它恰恰是一组数据序列的颜色，变色同样必须重绘。反过来，
    /// <c>PlotPaddingProperty</c>（<c>double</c>）、<c>ValuesProperty</c>（<c>IReadOnlyList&lt;double&gt;?</c>）
    /// 两条判据都不满足：它们影响的是几何与数据，不是颜色，本就不该被这条规则管。
    /// </para>
    /// <para>
    /// 若将来某个图表声明了"确实不该触发重绘"的画笔，这条守卫会把它标出来——那时该讨论的是
    /// 为什么这个画笔不需要重绘，而不是给守卫开豁免。
    /// </para>
    /// </remarks>
    private static bool IsBrush(StyledPropertyDeclaration declaration) =>
        declaration.Name.EndsWith("BrushProperty", StringComparison.Ordinal)
        || declaration.Type.Contains("IBrush", StringComparison.Ordinal);

    /// <summary>
    /// 取出源码里所有 <c>StyledProperty&lt;…&gt;</c> 声明（属性名 + 值类型文本）。
    /// </summary>
    private static List<StyledPropertyDeclaration> DeclaredStyledProperties(string source)
    {
        List<StyledPropertyDeclaration> declarations = [];

        foreach (Match match in _styledPropertyKeyword.Matches(source))
        {
            int openAngle = match.Index + match.Length - 1;
            if (!TryBalancedSpan(source, openAngle, '<', '>', out int angleEnd))
            {
                continue;
            }

            string type = source[(openAngle + 1)..angleEnd];

            // `StyledProperty<IBrush?> SurfaceBrushProperty = …`：类型之后、第一个 `=` 之前的
            // 那个标识符就是属性名。跨到别的语句（出现 ; { }）就说明这不是一处能解析的声明。
            int equals = source.IndexOf('=', angleEnd + 1);
            if (equals < 0)
            {
                continue;
            }

            string between = source[(angleEnd + 1)..equals];
            if (between.Contains(';') || between.Contains('{') || between.Contains('}'))
            {
                continue;
            }

            Match name = _identifierAtEnd.Match(between);
            if (name.Success)
            {
                declarations.Add(new StyledPropertyDeclaration(name.Groups["name"].Value, type));
            }
        }

        return declarations;
    }

    /// <summary>
    /// 取出源码里每一次 <c>AffectsRender&lt;…&gt;(…)</c> 调用的<b>实参文本</b>（括号内的原文）。
    /// </summary>
    private static List<string> AffectsRenderArguments(string source)
    {
        List<string> arguments = [];

        foreach (Match match in _affectsRenderKeyword.Matches(source))
        {
            int openAngle = match.Index + match.Length - 1;
            if (!TryBalancedSpan(source, openAngle, '<', '>', out int angleEnd))
            {
                continue;
            }

            int openParen = source.IndexOf('(', angleEnd + 1);
            if (openParen < 0 || !TryBalancedSpan(source, openParen, '(', ')', out int parenEnd))
            {
                continue;
            }

            arguments.Add(source[(openParen + 1)..parenEnd]);
        }

        return arguments;
    }

    /// <summary>
    /// 从 <paramref name="openIndex"/> 处的 <paramref name="open"/> 起，找到与它配对的那个
    /// <paramref name="close"/> 的下标（嵌套也认：<c>SliceBrushesProperty</c> 的泛型参数里就有尖括号）。
    /// </summary>
    private static bool TryBalancedSpan(string text, int openIndex, char open, char close, out int closeIndex)
    {
        int depth = 0;

        for (int i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close)
            {
                depth--;

                if (depth == 0)
                {
                    closeIndex = i;
                    return true;
                }
            }
        }

        closeIndex = -1;
        return false;
    }

    /// <summary>
    /// 剥掉 <c>//</c> 行注释与 <c>/* */</c> 块注释，其余字符（含换行）原样保留。
    /// </summary>
    /// <remarks>
    /// 不这么做的话，本仓库那种写满 <c>&lt;c&gt;AffectsRender&lt;ChartSurface&gt;(…)&lt;/c&gt;</c> 的文档注释
    /// 会被当成一次"登记"，而把某个画笔注释掉也照样算数——两种假绿都会让守卫在真正的违规面前变绿。
    /// 字符串字面量里的注释符按字面处理（本目录下的文件里有 <c>"*.cs"</c> 这类字符串，但没有 <c>//</c>）。
    /// </remarks>
    private static string StripComments(string source)
    {
        StringBuilder stripped = new(source.Length);
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inString = false;

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    stripped.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                stripped.Append(current);

                if (current == '\\' && i + 1 < source.Length)
                {
                    stripped.Append(source[++i]);
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (current == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
            }

            stripped.Append(current);
        }

        return stripped.ToString();
    }

    /// <summary>
    /// <c>src/Tsp.Desktop/Charts</c> 下的全部 <c>.cs</c> 文件（含子目录）。
    /// </summary>
    private static List<string> ChartSourceFiles() => [.. Directory
        .EnumerateFiles(RepoRoot.Combine(ChartsRelativePath), "*.cs", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)];

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(RepoRoot.Directory, fullPath).Replace('\\', '/');

    /// <summary>一条 <c>StyledProperty</c> 声明：属性名与它的值类型文本。</summary>
    private readonly record struct StyledPropertyDeclaration(string Name, string Type);
}
