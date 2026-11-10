using System.Globalization;
using Tsp.Contracts.Market;

namespace Tsp.Desktop.ViewModels;

/// <summary>
/// 侧栏指数条里的一格：把 <see cref="IndexQuote"/> 折成界面可直接绑的形状——名称、点位、涨跌幅与涨跌方向。
/// </summary>
/// <remarks>
/// 折算放在 ViewModel 层而不是 XAML 里，理由与 <see cref="MainWindowViewModel.DomainBadge"/> 相同：
/// 涨跌是判断、小数位是格式，两者都可单测，不必为它们在 View 里引入值转换器；
/// 而颜色本身仍留在 <c>Themes/Controls.axaml</c> 的具名样式里——这里只给方向，不给色彩。
/// </remarks>
/// <param name="Name">指数中文名。</param>
/// <param name="CloseText">点位文案，固定两位小数。</param>
/// <param name="ChangeText">涨跌幅文案：正数补 <c>+</c> 号，负数自带 <c>-</c> 号，末尾带 <c>%</c>。</param>
/// <param name="IsUp">是否上涨（涨跌幅为正）。</param>
/// <param name="IsDown">是否下跌（涨跌幅为负）。两者都为假即平盘，界面走中性色。</param>
public sealed record IndexQuoteRow(string Name, string CloseText, string ChangeText, bool IsUp, bool IsDown)
{
    /// <summary>
    /// 由指数快照折出一格。
    /// </summary>
    /// <param name="quote">指数快照。</param>
    public static IndexQuoteRow From(IndexQuote quote)
    {
        // InvariantCulture 是刻意的：文案要跨机器稳定（小数点是点号、没有千分位），
        // 否则同一份夹具在不同区域设置的机器上会渲染出不同的数字，单测也跟着漂。
        string closeText = quote.Close.ToString("F2", CultureInfo.InvariantCulture);

        // 正数补 "+"：涨跌不靠颜色单独承载信息——灰度截图、色觉障碍与屏幕对比度差时同样读得出来。
        string sign = quote.ChangePct > 0m ? "+" : "";
        string changeText = sign + quote.ChangePct.ToString("F2", CultureInfo.InvariantCulture) + "%";

        return new IndexQuoteRow(quote.Name, closeText, changeText, quote.ChangePct > 0m, quote.ChangePct < 0m);
    }
}
