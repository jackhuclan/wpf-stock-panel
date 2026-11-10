namespace Tsp.Desktop.ViewModels.Navigation;

/// <summary>
/// 导航服务：持有"当前是哪一页"与返回栈，是这一问题的唯一事实来源。
/// 主窗口壳经由本接口驱动侧栏选中态与返回命令；实现是纯 ViewModel 层类型，无任何 UI 依赖。
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 当前页。处于空状态（路由键未注册）时为 <c>null</c>。
    /// </summary>
    PageRoute? Current { get; }

    /// <summary>
    /// 当前页变更通知。载荷类型**可空**：进入空状态时同样触发，载荷为 <c>null</c>——
    /// 这正是空状态页得以显示的机制。
    /// </summary>
    event EventHandler<PageRoute?>? CurrentChanged;

    /// <summary>
    /// 导航到路由键。<see cref="Routes.Find"/> 命中则入栈并更新 <see cref="Current"/>；
    /// 未命中（拼错、深链失效、页面被裁掉）则把 <see cref="Current"/> 置 <c>null</c> 并触发
    /// <see cref="CurrentChanged"/>，**不抛异常、也不改动返回栈**。
    /// 导航到与当前相同的路由按普通导航处理（再次入栈），不特判为 no-op。
    /// </summary>
    /// <param name="routeKey">路由键，形如 <c>/dashboard</c>。</param>
    void Navigate(string routeKey);

    /// <summary>
    /// 返回栈深是否大于 1，即是否存在可返回的上一页。
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// 返回上一页。栈深 ≤ 1（根）时为静默 no-op：不抛异常、不改动 <see cref="Current"/>、也不触发
    /// <see cref="CurrentChanged"/>。
    /// </summary>
    void GoBack();
}
