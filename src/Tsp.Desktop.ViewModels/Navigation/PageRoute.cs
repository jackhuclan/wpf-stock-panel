namespace Tsp.Desktop.ViewModels.Navigation;

/// <summary>
/// 一条页面路由。<paramref name="Key"/> 是 <c>/dashboard</c> 形式的路径键，
/// <paramref name="Title"/> 是界面显示的中文页名，<paramref name="Order"/> 是导航排序序号（升序、唯一）。
/// </summary>
public sealed record PageRoute(
    string Key,
    string Title,
    PageDomain Domain,
    int Order);
