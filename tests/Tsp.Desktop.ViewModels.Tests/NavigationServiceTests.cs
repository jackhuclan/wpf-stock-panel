using Tsp.Desktop.ViewModels.Navigation;

namespace Tsp.Desktop.ViewModels.Tests;

public sealed class NavigationServiceTests
{
    [Fact]
    public void Navigate_to_registered_key_sets_current_and_raises_event()
    {
        var sut = new NavigationService();
        PageRoute? observed = null;
        sut.CurrentChanged += (_, route) => observed = route;

        sut.Navigate(Routes.Dashboard);

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.Equal("/dashboard", observed!.Key);
    }

    [Fact]
    public void Navigate_to_unknown_key_stops_at_empty_state_without_throwing()
    {
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);

        var ex = Record.Exception(() => sut.Navigate("/nope"));

        Assert.Null(ex);
        Assert.Null(sut.Current);
    }

    [Fact]
    public void GoBack_returns_to_previous_route_and_reports_can_go_back()
    {
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);
        sut.Navigate(Routes.Watchlist);
        Assert.True(sut.CanGoBack);

        sut.GoBack();

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.False(sut.CanGoBack);
    }

    [Fact]
    public void GoBack_at_root_is_a_no_op()
    {
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);

        var ex = Record.Exception(sut.GoBack);

        Assert.Null(ex);
        Assert.Equal("/dashboard", sut.Current!.Key);
    }

    [Fact]
    public void Navigate_to_unknown_key_raises_current_changed_with_null_payload()
    {
        // 空状态页依赖这条通知：CurrentChanged 的载荷是可空的，"无当前路由"也必须触发它。
        // 哨兵初值取一个非 null 路由，若事件根本没被触发，载荷断言会失败而不是假通过。
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);

        var raised = 0;
        PageRoute? payload = Routes.Find(Routes.Dashboard);
        sut.CurrentChanged += (_, route) =>
        {
            raised++;
            payload = route;
        };

        sut.Navigate("/nope");

        Assert.Equal(1, raised);
        Assert.Null(payload);
    }

    [Fact]
    public void GoBack_after_unknown_key_navigation_pops_the_intact_back_stack()
    {
        // 未知键既不 push 也不清栈，故栈内容与那次导航前完全相同：
        // CanGoBack 仍为 true（栈深 2），GoBack 走正常出栈路径回到栈内的上一页。
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);
        sut.Navigate(Routes.Watchlist);

        sut.Navigate("/nope");

        Assert.Null(sut.Current);
        Assert.True(sut.CanGoBack);

        sut.GoBack();

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.False(sut.CanGoBack);
    }

    [Fact]
    public void Navigate_to_the_same_route_twice_pushes_twice()
    {
        // 重复导航按普通导航处理，不特判成 no-op：否则返回栈语义会随菜单点击次数漂移。
        var sut = new NavigationService();
        sut.Navigate(Routes.Dashboard);
        sut.Navigate(Routes.Dashboard);

        Assert.True(sut.CanGoBack);

        sut.GoBack();

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.False(sut.CanGoBack);
    }

    [Fact]
    public void GoBack_at_root_is_silent_whether_current_is_set_or_null()
    {
        // 根 = 栈深 ≤ 1，此时 GoBack 一律静默：不抛异常、不改动 Current、也不触发事件。
        // 空状态不占返回栈一格（未知键不 push），因此它不会让 GoBack 变成"有东西可回"。
        var sut = new NavigationService();
        var raised = 0;
        sut.CurrentChanged += (_, _) => raised++;

        sut.Navigate(Routes.Dashboard);
        sut.GoBack();

        Assert.Equal("/dashboard", sut.Current!.Key);
        Assert.Equal(1, raised);

        sut.Navigate("/nope");
        Assert.Null(sut.Current);

        sut.GoBack();

        Assert.Null(sut.Current);
        Assert.Equal(2, raised);
        Assert.False(sut.CanGoBack);
    }
}
