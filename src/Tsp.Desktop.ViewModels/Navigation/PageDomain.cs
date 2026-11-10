namespace Tsp.Desktop.ViewModels.Navigation;

/// <summary>
/// 页面域。<see cref="Core"/> 为承载核心能力的页面；<see cref="Ext"/> 为可整体裁剪的扩展页面；
/// <see cref="Settings"/> 为设置页——它按 spec §5.6 独立于 19 页表格命名，由主窗口侧栏底部入口承载，
/// 不在 <see cref="Routes.All"/> 内，但同样经 <see cref="INavigationService"/> 导航。
/// </summary>
public enum PageDomain
{
    Core,
    Ext,
    Settings,
}
