using System.Text;
using System.Text.RegularExpressions;
using Tsp.Desktop.Charts;
using Tsp.Desktop.ViewModels.Tests;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <c>src/Tsp.Desktop/Charts/</c> 下全部 C# 源码的两条纪律守卫。都只读源码、不加载 Avalonia，
/// 与 P0 的 <c>ThemeTokenReferenceTests</c>（静态扫 <c>.axaml</c>）同源做法，
/// 仓库根的定位复用它的 <c>RepoRoot</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>1) 画笔-重绘登记</b>：每个声明了画笔属性的文件，都必须把那些画笔登记进
/// <b>该画笔所属的那个类</b>的 <c>AffectsRender&lt;那个类&gt;(…)</c> 调用里。
/// </para>
/// <para>
/// <b>为什么需要它。</b>"切换主题图表实时变色"是本阶段的验收标准，而它全靠
/// 「画笔是 <c>StyledProperty</c> + 静态构造里挂 <c>AffectsRender</c>」这一条链路。
/// <c>AffectsRender</c> 的注册状态<b>没有公开查询接口</b>（Avalonia 12.1.6 的
/// <c>AvaloniaProperty</c> / <c>AvaloniaPropertyRegistry</c> 公开成员里没有任何与渲染联动相关的
/// 查询，它是 <c>Visual</c> 的 protected static，注册结果落在 <c>LightweightSubject</c> 的私有订阅表里），
/// 而 <c>DrawingContext</c> 的构造函数不可访问、测试里造不出渲染上下文——于是
/// <b>漏登记这件事在运行期没有任何信号</b>：不抛异常、不给构建警告、界面照常渲染，
/// 只是颜色停在上一套主题。它正是 P0 起连着几个任务在追的那类静默失效。
/// </para>
/// <para>
/// <b>与实机验证分工不同，两者都要。</b>实机验证证明这条链路<b>能工作</b>；本条守卫防止它被
/// <b>悄悄拆掉</b>——实机验证是手动的，删掉一行 <c>AffectsRender</c> 参数不会让任何自动化变红。
/// 按命名空间而不是只按基座来守，是因为 Task 6–10 的派生控件各自会加画笔，只守基座等于留下
/// 九次犯同一个错而无人察觉的机会。
/// </para>
/// <para>
/// <b>2) 渲染路径不得用相对父控件的 <c>Bounds</c></b>：见 <c>Render_path_never_reads_parent_relative_Bounds</c>。
/// </para>
/// <para>
/// <b>本文件的两条判据都是文本级的</b>（正则 + 括号配对），不是编译期类型系统。它们宁可<b>假红</b>
/// 也不假绿：解析不出来时按违规处理并说明原因，于是"守卫自己失效了"会以红的形式暴露出来，
/// 而不是安静地全绿。
/// </para>
/// </remarks>
public sealed class ChartsSourceGuardTests
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

    /// <summary><c>AvaloniaProperty.Register&lt;那个类, TValue&gt;(…)</c> 里的 <c>Register</c>。</summary>
    private static readonly Regex _registerKeyword = new(@"\bRegister\s*<", RegexOptions.Compiled);

    /// <summary>匹配一段文本末尾的标识符（用于取"类型之后、<c>=</c> 之前"的那个属性名）。</summary>
    private static readonly Regex _identifierAtEnd = new(@"(?<name>[A-Za-z_]\w*)\s*\z", RegexOptions.Compiled);

    /// <summary>匹配一个纯标识符（用于校验解析出来的类名，而不是把一段表达式当类名）。</summary>
    private static readonly Regex _plainIdentifier = new(@"\A[A-Za-z_]\w*\z", RegexOptions.Compiled);

    /// <summary>
    /// 匹配一次**相对父控件**的 <c>Bounds</c> 取用：<c>Bounds</c> 后面不是 <c>.Size</c> /
    /// <c>.Width</c> / <c>.Height</c> 的那些（<c>Bounds.X</c>、裸的 <c>Bounds</c>、
    /// <c>Bounds.Right</c>……）。<c>BoundsProperty</c> 这种标识符不会被命中（词边界）。
    /// </summary>
    private static readonly Regex _parentRelativeBounds = new(
        @"\bBounds\b(?!\s*\.\s*(?:Size|Width|Height)\b)",
        RegexOptions.Compiled);

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

            // 同一个文件里可能不止一次 AffectsRender 调用；下面按**类型参数**把它们分开，
            // 只有类型参数等于"这个画笔所属的那个类"的调用才算数。
            List<AffectsRenderCall> calls = AffectsRenderCalls(source);

            foreach (StyledPropertyDeclaration brush in brushes)
            {
                brushesSeen.Add(brush.Name);
                string site = $"{relative}:{LineAt(source, brush.Index)}";

                if (brush.DeclaringType is null)
                {
                    violations.Add(
                        $"  {site}: {brush.Name}（读不出它属于哪个类——画笔请用 AvaloniaProperty.Register<那个类, TValue>(…) 声明）");
                    continue;
                }

                // 类型参数必须对得上：AffectsRender<别的类>(ThisBrushProperty) 是**假登记**——
                // 它满足"这个名字出现在一次 AffectsRender 里"，但类处理器挂在别的类型上，
                // 本控件的这个画笔照样不会重绘。这正是本条守卫要防的那种静默失效。
                string registered = string.Join(
                    "\n",
                    calls.Where(call => call.TargetType == brush.DeclaringType).Select(call => call.Arguments));

                // 按标识符匹配而不是精确字符串：换行、缩进、参数顺序、注释都不影响判定，
                // 而从调用里**删掉**一个画笔一定判定失败。
                if (!Regex.IsMatch(registered, $@"\b{Regex.Escape(brush.Name)}\b"))
                {
                    violations.Add(
                        $"  {site}: {brush.Name}（需出现在 AffectsRender<{brush.DeclaringType}>(…) 的实参里）");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "下列画笔属性没有登记到**它自己所属的类**的 AffectsRender<…>(…) 调用里——它变色时不会触发重绘，"
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
    /// <c>src/Tsp.Desktop/Charts/</c> 下的绘制代码<b>只允许读 <c>Bounds</c> 的尺寸</b>
    /// （<c>Size</c> / <c>Width</c> / <c>Height</c>），不得取用它相对父控件的位置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是一条真出过事的纪律（复审 Critical）。</b><see cref="Visual.Bounds"/> 的 <c>X</c>/<c>Y</c>
    /// 是<b>相对父控件</b>的（布局留给它的位置），而 <c>Render</c> 拿到的 <c>DrawingContext</c>
    /// 用的是<b>控件自己的局部坐标系</b>（原点在控件左上角）——两者根本不在一个坐标系里。
    /// Avalonia 自己的 <c>Border</c> / <c>Panel</c> / <c>Image</c> 在 <c>Render</c> 里都只读
    /// <c>Bounds.Size</c>，正是这个原因。
    /// </para>
    /// <para>
    /// 把 <c>Bounds</c> 整块（或它的 <c>X</c>/<c>Y</c>）用于绘制，会让**凡是不在父容器原点上的**
    /// 图表——格子里的、第二行的、带内边距的面板里的，也就是真实布局里的每一个——整体位移；
    /// 而恰好摆在原点上的那些看起来完全正常。<b>这个失效连渲染体变异都测不出来</b>：
    /// 只要测试给控件的 <c>Arrange</c> 用的是 <c>Rect(0, 0, …)</c>，位移量恰好是 0。
    /// 它只能靠"非零原点"的用例（见 <c>ChartSurfaceContractTests.PlotArea_is_independent_of_the_arrange_origin</c>）
    /// 与这条源码纪律一起挡住。
    /// </para>
    /// <para>
    /// 这条规则覆盖整个 <c>Charts/</c> 目录，于是 Task 6–10 的每个派生控件都自动受它保护。
    /// 若将来某处确实需要父控件坐标，正确的做法是用
    /// <c>e.GetPosition(this)</c> 这类已经换算到本控件坐标系的入口，而不是给这条守卫开豁免。
    /// </para>
    /// </remarks>
    [Fact]
    public void Render_path_never_reads_parent_relative_Bounds()
    {
        List<string> files = ChartSourceFiles();
        List<string> violations = [];

        foreach (string file in files)
        {
            string relative = Relative(file);
            string source = StripComments(File.ReadAllText(file));

            foreach (Match match in _parentRelativeBounds.Matches(source))
            {
                int line = LineAt(source, match.Index);
                string context = source[match.Index..Math.Min(source.Length, match.Index + 60)]
                    .Split('\n')[0]
                    .Trim();

                violations.Add($"  {relative}:{line}: {context}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "下列地方取用了 Bounds 的**相对父控件**位置——Render 用的是控件局部坐标系（原点在控件左上角），"
                + "两者不在一个坐标系里。所有不在父容器原点上的图表都会整体位移，"
                + "而 Arrange(0, 0, …) 的用例看不出任何异常。"
                + Environment.NewLine
                + "修法：绘制时用 new Rect(Bounds.Size)（即 (0, 0, 宽, 高)），只读 Bounds 的 Size / Width / Height。"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));

        Assert.True(files.Count > 0, $"{ChartsRelativePath} 下一个 .cs 文件都没扫到。");
    }

    /// <summary>
    /// 判定一条 <c>StyledProperty</c> 声明是不是"画笔 / 颜色"。判据是几条的<b>并集</b>：
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1) 名字以 <c>BrushProperty</c> / <c>BrushesProperty</c> / <c>ColorProperty</c> 收尾（命名口径）；
    /// </para>
    /// <para>
    /// 2) 声明的值类型里出现 <c>Brush</c>（含 <c>IBrush</c>、<c>SolidColorBrush</c>、
    /// <c>LinearGradientBrush</c> 等一切画刷类型）；
    /// </para>
    /// <para>
    /// 3) 值类型是 <c>Color</c>（整体标识符匹配）。派生控件完全可能声明
    /// <c>StyledProperty&lt;Color?&gt; UpColorProperty</c> 并在渲染时转成画刷——那同样是"一个颜色"，
    /// 变色必须重绘。
    /// </para>
    /// <para>
    /// 这几条是刻意放宽的，理由有实测载荷：<c>DonutChart</c> 的分片色是
    /// <c>StyledProperty&lt;IReadOnlyList&lt;IBrush&gt;?&gt; SliceBrushesProperty</c>，
    /// 名字既不以 <c>BrushProperty</c> 收尾（是 <c>Brushes</c>），只按名字守会把它整个漏掉。
    /// 反过来，<c>PlotPaddingProperty</c>（<c>double</c>）与 <c>ValuesProperty</c>
    /// （<c>IReadOnlyList&lt;double&gt;?</c>）三条判据都不满足——它们影响的是几何与数据，
    /// 不是颜色，本就不该被这条规则管。
    /// </para>
    /// <para>
    /// <b>剩下的口子，明说</b>：若某个颜色属性的类型名里既没有 <c>Brush</c> 也不是 <c>Color</c>
    /// （比如自定义的 <c>IMyPalette</c>），这条规则认不出来。那种情况下请<b>扩宽这里</b>而不是
    /// 让新画种安静地溜过去。
    /// </para>
    /// </remarks>
    private static bool IsBrush(StyledPropertyDeclaration declaration) =>
        declaration.Name.EndsWith("BrushProperty", StringComparison.Ordinal)
        || declaration.Name.EndsWith("BrushesProperty", StringComparison.Ordinal)
        || declaration.Name.EndsWith("ColorProperty", StringComparison.Ordinal)
        || declaration.Type.Contains("Brush", StringComparison.Ordinal)
        || Regex.IsMatch(declaration.Type, @"\bColor\b");

    /// <summary>
    /// 取出源码里所有 <c>StyledProperty&lt;…&gt;</c> 声明（属性名、值类型文本、所属类名、名字在源码里的下标）。
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
            if (!name.Success)
            {
                continue;
            }

            int nameIndex = angleEnd + 1 + name.Groups["name"].Index;
            declarations.Add(new StyledPropertyDeclaration(
                name.Groups["name"].Value,
                type,
                RegisterOwnerType(source, equals),
                nameIndex));
        }

        return declarations;
    }

    /// <summary>
    /// 从 <paramref name="equalsIndex"/>（声明的 <c>=</c>）往后找出
    /// <c>AvaloniaProperty.Register&lt;那个类, TValue&gt;(…)</c> 里的"那个类"。
    /// 读不出（不是 <c>Register</c> 形状、或第一个类型参数不是纯标识符）时返回 <c>null</c>——
    /// 调用方按违规处理，于是"解析不出来"会以红的形式暴露，而不是静默放行。
    /// </summary>
    private static string? RegisterOwnerType(string source, int equalsIndex)
    {
        Match register = _registerKeyword.Match(source, equalsIndex + 1);

        // 只认同一处声明里的 Register：隔得太远（例如到了下一个成员）就不算。
        if (!register.Success || register.Index - equalsIndex > 200)
        {
            return null;
        }

        int openAngle = register.Index + register.Length - 1;
        if (!TryBalancedSpan(source, openAngle, '<', '>', out int angleEnd))
        {
            return null;
        }

        string genericArguments = source[(openAngle + 1)..angleEnd];
        int comma = genericArguments.IndexOf(',');
        string owner = (comma < 0 ? genericArguments : genericArguments[..comma]).Trim();

        return _plainIdentifier.IsMatch(owner) ? owner : null;
    }

    /// <summary>
    /// 取出源码里每一次 <c>AffectsRender&lt;目标类型&gt;(…)</c> 调用的目标类型与<b>实参文本</b>。
    /// </summary>
    private static List<AffectsRenderCall> AffectsRenderCalls(string source)
    {
        List<AffectsRenderCall> calls = [];

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

            calls.Add(new AffectsRenderCall(
                source[(openAngle + 1)..angleEnd].Trim(),
                source[(openParen + 1)..parenEnd]));
        }

        return calls;
    }

    /// <summary>
    /// 从 <paramref name="openIndex"/> 处的 <paramref name="open"/> 起，找到与它配对的那个
    /// <paramref name="close"/> 的下标（嵌套也认：<c>IReadOnlyList&lt;IBrush&gt;?</c> 里就有尖括号）。
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
    /// 把 <paramref name="index"/> 换算成 1 起算的行号（<b>换行在剥注释时一律保留</b>，所以行号与源文件一致）。
    /// </summary>
    private static int LineAt(string text, int index)
    {
        int line = 1;

        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// 剥掉注释，其余内容原样保留（<b>换行也保留</b>，好让报错里的行号对得上源文件）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 不剥注释的话，本仓库那种写满 <c>&lt;c&gt;AffectsRender&lt;ChartSurface&gt;(…)&lt;/c&gt;</c> 的文档注释
    /// 会被当成一次"登记"，而把某个画笔注释掉也照样算数——两种假绿都会让守卫在真正的违规面前变绿。
    /// </para>
    /// <para>
    /// 认得：<c>//</c> 行注释、<c>/* */</c> 块注释、普通字符串（含 <c>\</c> 转义）、字符字面量
    /// <c>'…'</c>、逐字字符串 <c>@"…"</c>（<c>""</c> 表示一个引号）、原样字符串 <c>"""…"""</c>。
    /// <b>字符字面量必须认</b>：一个 <c>'"'</c> 就能让"字符串里"的状态错位到文件末尾，
    /// 从而**把剥注释整个关掉**——那之后被注释掉的画笔又会被算成已登记，是条不声不响的假绿通道。
    /// </para>
    /// <para>
    /// <b>不认得的（明说的口子）</b>：插值字符串里的花括号洞中再出现引号
    /// （<c>$"{dict["k"]}"</c>）、以及 <c>""""</c> 以上引号数的原样字符串。
    /// 这两种写法在本目录下一处都没有；真出现了，假绿通道是"该处注释不被剥"，
    /// 后果限于那一段代码，不会扩大。
    /// </para>
    /// </remarks>
    private static string StripComments(string source)
    {
        StringBuilder stripped = new(source.Length);
        State state = State.Normal;

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];

            if (state is not State.Normal)
            {
                // 换行一律留下（含注释内部），行号才对得上源文件。
                if (current == '\n')
                {
                    stripped.Append(current);
                }

                switch (state)
                {
                    case State.LineComment:
                        if (current == '\n')
                        {
                            state = State.Normal;
                        }

                        break;

                    case State.BlockComment:
                        if (current == '*' && i + 1 < source.Length && source[i + 1] == '/')
                        {
                            state = State.Normal;
                            i++;
                        }

                        break;

                    case State.String:
                    case State.Char:
                        if (current == '\\' && i + 1 < source.Length)
                        {
                            i++;
                        }
                        else if (current == (state == State.String ? '"' : '\''))
                        {
                            state = State.Normal;
                        }

                        break;

                    case State.VerbatimString:
                        if (current == '"')
                        {
                            if (i + 1 < source.Length && source[i + 1] == '"')
                            {
                                i++;    // "" 是一个转义后的引号，字符串还没结束
                            }
                            else
                            {
                                state = State.Normal;
                            }
                        }

                        break;

                    case State.RawString:
                        if (current == '"' && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
                        {
                            state = State.Normal;
                            i += 2;
                        }

                        break;
                }

                continue;
            }

            if (current == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                state = State.LineComment;
                i++;
                continue;
            }

            if (current == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                state = State.BlockComment;
                i++;
                continue;
            }

            if (current == '"')
            {
                if (i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
                {
                    state = State.RawString;
                    stripped.Append(current);
                    i += 2;
                    continue;
                }

                state = IsVerbatimPrefix(source, i) ? State.VerbatimString : State.String;
                stripped.Append(current);
                continue;
            }

            if (current == '\'')
            {
                state = State.Char;
                stripped.Append(current);
                continue;
            }

            stripped.Append(current);
        }

        return stripped.ToString();
    }

    /// <summary>
    /// 引号前面（跳过插值的 <c>$</c>）是不是 <c>@</c>，即这是不是一个逐字字符串。
    /// </summary>
    private static bool IsVerbatimPrefix(string source, int quoteIndex)
    {
        for (int i = quoteIndex - 1; i >= 0; i--)
        {
            if (source[i] == '$')
            {
                continue;
            }

            return source[i] == '@';
        }

        return false;
    }

    /// <summary>
    /// <c>src/Tsp.Desktop/Charts</c> 下的全部 <c>.cs</c> 文件（含子目录）。
    /// </summary>
    private static List<string> ChartSourceFiles() => [.. Directory
        .EnumerateFiles(RepoRoot.Combine(ChartsRelativePath), "*.cs", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)];

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(RepoRoot.Directory, fullPath).Replace('\\', '/');

    /// <summary>剥注释时的扫描状态。</summary>
    private enum State
    {
        Normal,
        LineComment,
        BlockComment,
        String,
        Char,
        VerbatimString,
        RawString,
    }

    /// <summary>
    /// 一条 <c>StyledProperty</c> 声明：属性名、值类型文本、所属类名（读不出时为 <c>null</c>）、
    /// 属性名在源码里的下标（用来报行号）。
    /// </summary>
    private readonly record struct StyledPropertyDeclaration(string Name, string Type, string? DeclaringType, int Index);

    /// <summary>一次 <c>AffectsRender&lt;目标类型&gt;(实参)</c> 调用。</summary>
    private readonly record struct AffectsRenderCall(string TargetType, string Arguments);
}
