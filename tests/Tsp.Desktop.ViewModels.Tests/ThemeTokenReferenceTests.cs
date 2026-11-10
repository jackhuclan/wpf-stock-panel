using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tsp.Desktop.ViewModels.Tests;

/// <summary>
/// 令牌引用守卫：<c>Themes/Tokens.axaml</c> 里的令牌键在视图里**必须**以 <c>{DynamicResource}</c> 引用。
///
/// 为什么值得一条测试：<c>{StaticResource}</c> 会把令牌在加载时烤死，之后切换主题不再跟随，
/// 而"运行中切换主题实时生效"是 P0 的验收标准之一。这条失效**没有任何运行时信号**——
/// 不抛异常、不给构建警告，界面也照常渲染（只是颜色停在上一套主题），
/// 因此它能穿过编译、穿过全部其它测试，只被重新读 diff 的人发现。
///
/// 测试直接读仓库里的 <c>.axaml</c> 源文件，不加载 Avalonia（测试栈刻意不含 Headless）。
/// </summary>
public sealed class ThemeTokenReferenceTests
{
    private const string TokensRelativePath = "src/Tsp.Desktop/Themes/Tokens.axaml";
    private const string DesktopProjectRelativePath = "src/Tsp.Desktop";

    private static readonly XNamespace _xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace _avaloniaNamespace = "https://github.com/avaloniaui";

    /// <summary>
    /// 匹配一处资源引用，两种写法都认：<c>{DynamicResource Key}</c> 与 <c>{DynamicResource Key=Key}</c>。
    /// 只认单行写法——本仓库的 axaml 一律如此。
    /// </summary>
    private static readonly Regex _tokenReference = new(
        @"\{\s*(?<kind>StaticResource|DynamicResource)\s+(?:Key\s*=\s*)?(?<key>[A-Za-z_][A-Za-z0-9_.]*)\s*\}",
        RegexOptions.Compiled);

    /// <summary>
    /// 白名单：允许以 <c>{StaticResource}</c> 引用令牌的 (文件, 键, 允许出现的次数)。
    /// 每条豁免都必须**显式登记**，并配一个次数——于是"豁免"是看得见的决定，
    /// 而不是"恰好没被检出"；某个条目一旦用不上（次数对不上），本测试会失败。
    ///
    /// 唯一的条目是缓动 <c>SmoothEasing</c>：它不在 Dark / Light 两个主题字典里，
    /// 切主题不会让它变，用 <c>{StaticResource}</c> 取它在加载时烤死也无所谓——
    /// 这正是 Controls.axaml 那两处的理由。
    /// </summary>
    private static readonly (string File, string Key, int Uses)[] _staticResourceAllowList =
    [
        ("src/Tsp.Desktop/Themes/Controls.axaml", "SmoothEasing", 2),
    ];

    [Fact]
    public void Every_token_reference_outside_Tokens_axaml_is_dynamic_resource()
    {
        IReadOnlySet<string> tokenKeys = TokenKeys();
        List<string> viewFiles = ViewFiles();
        List<string> violations = [];
        Dictionary<(string File, string Key), int> allowedUses = [];
        int dynamicReferences = 0;

        foreach (string file in viewFiles)
        {
            string relative = Relative(file);

            foreach (Match match in _tokenReference.Matches(File.ReadAllText(file)))
            {
                string key = match.Groups["key"].Value;
                if (!tokenKeys.Contains(key))
                {
                    // 不是本项目的设计令牌（例如 FluentTheme 自带的键），不在守护范围内。
                    continue;
                }

                if (match.Groups["kind"].Value == "DynamicResource")
                {
                    dynamicReferences++;
                    continue;
                }

                (string File, string Key) entry = (relative, key);
                if (_staticResourceAllowList.Any(item => item.File == entry.File && item.Key == entry.Key))
                {
                    allowedUses[entry] = allowedUses.GetValueOrDefault(entry) + 1;
                    continue;
                }

                violations.Add($"{relative}: {{StaticResource {key}}}");
            }
        }

        // 违例逐条列全（Assert.Empty 会把长条目截断，这里要的就是"哪个文件、哪个键"完整可读）。
        Assert.True(
            violations.Count == 0,
            "下列令牌引用不是 {DynamicResource}，会在加载时把色值烤死、之后切主题不再跟随："
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));

        // 白名单不许过期：正被用到的次数若少于登记的次数，说明豁免还挂在那里但已经没人用了。
        foreach ((string file, string key, int uses) in _staticResourceAllowList)
        {
            Assert.Equal(uses, allowedUses.GetValueOrDefault((file, key)));
        }

        // 反面守卫：上面两条若因为"一个文件都没扫到""一个键都没取到"或"正则没匹配上"而全绿，
        // 那是假绿——守卫必须证明自己真的看过了东西。门槛刻意留足余量（实测 4 个文件、19 个键、
        // 40 处引用），它只用来发现"守卫本身失效"，不用来钉令牌清单的增减。
        Assert.True(viewFiles.Count >= 4, $"只扫到 {viewFiles.Count} 个 .axaml 文件，扫描范围可能被削掉了。");
        Assert.True(tokenKeys.Count >= 16, $"只取到 {tokenKeys.Count} 个令牌键（两套主题字典都各有 16 个键），Tokens.axaml 的解析可能失效了。");
        Assert.True(dynamicReferences >= 10, $"只匹配到 {dynamicReferences} 处 {{DynamicResource}} 引用，引用正则可能失效了。");
    }

    /// <summary>
    /// 取出 <c>Tokens.axaml</c> 里的全部令牌键：Dark / Light 两个主题字典的键，
    /// 外加主题字典之外的键（字体族、缓动）。后者一并纳入守护范围，好让 SmoothEasing
    /// 的两处 <c>{StaticResource}</c> 必须走白名单，而不是因为"不在字典里"被静默跳过。
    /// </summary>
    private static IReadOnlySet<string> TokenKeys()
    {
        XDocument document = XDocument.Load(RepoRoot.Combine(TokensRelativePath));
        XElement root = document.Root
            ?? throw new InvalidOperationException($"{TokensRelativePath} 没有根元素。");
        XElement themeDictionaries = root.Element(_avaloniaNamespace + "ResourceDictionary.ThemeDictionaries")
            ?? throw new InvalidOperationException(
                $"{TokensRelativePath} 里没有 ResourceDictionary.ThemeDictionaries——令牌必须按 Dark / Light 成对定义。");

        List<XElement> dictionaries = [.. themeDictionaries.Elements(_avaloniaNamespace + "ResourceDictionary")];
        Assert.Equal(2, dictionaries.Count);

        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (XElement dictionary in dictionaries)
        {
            List<string> dictionaryKeys = Keys(dictionary);
            Assert.NotEmpty(dictionaryKeys);
            keys.UnionWith(dictionaryKeys);
        }

        keys.UnionWith(Keys(root));
        return keys;
    }

    /// <summary>
    /// 取一个元素下所有带 <c>x:Key</c> 的直接子元素之键。
    /// </summary>
    private static List<string> Keys(XElement element) => [.. element
        .Elements()
        .Select(child => (string?)child.Attribute(_xamlNamespace + "Key"))
        .Where(key => key is not null)
        .Select(key => key!)];

    /// <summary>
    /// 待扫的视图文件：<c>src/Tsp.Desktop</c> 下全部 <c>.axaml</c>，排除 <c>bin</c> / <c>obj</c>
    /// 的构建产物与 <c>Tokens.axaml</c> 自身（令牌的**定义**处当然不引用令牌）。
    /// </summary>
    private static List<string> ViewFiles() => [.. Directory
        .EnumerateFiles(RepoRoot.Combine(DesktopProjectRelativePath), "*.axaml", SearchOption.AllDirectories)
        .Select(Relative)
        .Where(relative => !relative.StartsWith("src/Tsp.Desktop/bin/", StringComparison.Ordinal))
        .Where(relative => !relative.StartsWith("src/Tsp.Desktop/obj/", StringComparison.Ordinal))
        .Where(relative => relative != TokensRelativePath)
        .OrderBy(relative => relative, StringComparer.Ordinal)
        .Select(RepoRoot.Combine)];

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(RepoRoot.Directory, fullPath).Replace('\\', '/');
}
