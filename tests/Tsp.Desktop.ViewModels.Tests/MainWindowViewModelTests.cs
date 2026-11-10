using Tsp.Contracts.Instruments;
using Tsp.Contracts.Market;
using Tsp.Contracts.Watchlist;
using Tsp.Desktop.ViewModels.Navigation;
using Tsp.Desktop.ViewModels.Services;

namespace Tsp.Desktop.ViewModels.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Menu_exposes_all_routes_in_order()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        Assert.Equal(Routes.All.Select(r => r.Key), sut.Menu.Select(r => r.Key));
    }

    [Fact]
    public void Settings_route_stays_outside_the_nineteen_page_menu()
    {
        // 设置页按 spec §5.6 独立命名，不属于 19 页表格；它由侧栏底部入口承载。
        // 但它必须是**可导航**的：路由注册表不认得它，导航服务就会把它当未知路由丢进空状态页。
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        Assert.Equal(19, sut.Menu.Count);
        Assert.DoesNotContain(sut.Menu, route => route.Key == Routes.Settings);
        Assert.Equal(Routes.Settings, sut.SettingsRoute.Key);
        Assert.Equal(PageDomain.Settings, sut.SettingsRoute.Domain);
        Assert.Equal(sut.SettingsRoute, Routes.Find(Routes.Settings));
    }

    [Fact]
    public async Task Load_sets_current_route_to_the_first_menu_entry()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(sut.CurrentRoute);
        Assert.Equal(Routes.All[0].Key, sut.CurrentRoute!.Key);
    }

    [Fact]
    public async Task Load_surfaces_client_failure_as_readable_error_state()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient(new TspApiException("/missing: 夹具数据缺失")));

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(sut.ErrorMessage);
        Assert.Contains("夹具数据缺失", sut.ErrorMessage!);
    }

    [Fact]
    public void NavigateCommand_updates_current_route()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        sut.NavigateCommand.Execute(Routes.All[3]);

        Assert.Equal(Routes.All[3].Key, sut.CurrentRoute!.Key);
    }

    [Fact]
    public async Task Load_reports_fixture_identity_from_the_client()
    {
        var fixtureClient = new FakeApiClient();
        var realClient = new FakeApiClient { IsFixtureData = false };
        var fixtureSut = new MainWindowViewModel(new NavigationService(), fixtureClient);
        var realSut = new MainWindowViewModel(new NavigationService(), realClient);

        await fixtureSut.LoadCommand.ExecuteAsync(null);
        await realSut.LoadCommand.ExecuteAsync(null);

        Assert.True(fixtureSut.IsFixtureData);
        Assert.False(realSut.IsFixtureData);
    }

    [Fact]
    public async Task Load_pushes_exactly_one_history_frame_and_selects_the_first_menu_entry()
    {
        // 首屏那次导航同时要走两条路：导航服务（CurrentRoute）与侧栏选中态（SelectedMenuRoute）。
        // 两条路各自都触发导航的话会入栈两次，返回栈当场失真而界面毫无异样——这条钉住只入栈一次。
        var navigation = new NavigationService();
        var sut = new MainWindowViewModel(navigation, new FakeApiClient());

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.False(navigation.CanGoBack);
        Assert.Equal(Routes.All[0].Key, sut.SelectedMenuRoute!.Key);
        Assert.Equal(Routes.All[0].Key, sut.CurrentRoute!.Key);
    }

    [Fact]
    public void Selecting_a_sidebar_entry_navigates()
    {
        // 侧栏 ListBox 的 SelectedItem 两向绑定到 SelectedMenuRoute：视图里的一次点击，
        // 在 ViewModel 层就是一次属性赋值，导航必须由这次赋值发起。
        var navigation = new NavigationService();
        var sut = new MainWindowViewModel(navigation, new FakeApiClient());

        sut.SelectedMenuRoute = Routes.All[3];

        Assert.Equal(Routes.All[3].Key, navigation.Current!.Key);
        Assert.Equal(Routes.All[3].Key, sut.CurrentRoute!.Key);
    }

    [Fact]
    public void Sidebar_selection_stays_empty_on_the_settings_page_and_its_null_write_back_is_ignored()
    {
        // ListBox 的选中项不在 Items 内时会把 SelectedItem 回写成 null。设置页不在 19 页菜单内，
        // 于是"导航到设置页"必然引出这次回写；回写若被当成一次导航意图，当前页就会被打回空状态页。
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        sut.NavigateCommand.Execute(sut.SettingsRoute);

        Assert.Null(sut.SelectedMenuRoute);
        Assert.Equal(Routes.Settings, sut.CurrentRoute!.Key);

        sut.SelectedMenuRoute = null;

        Assert.Equal(Routes.Settings, sut.CurrentRoute!.Key);
        Assert.True(sut.HasRoute);
    }

    [Fact]
    public void Unknown_route_stops_at_the_empty_state_instead_of_throwing()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());

        var ex = Record.Exception(() => sut.NavigateCommand.Execute(new PageRoute("/not-registered", "不存在", PageDomain.Core, 99)));

        Assert.Null(ex);
        Assert.Null(sut.CurrentRoute);
        Assert.True(sut.IsRouteMissing);
        Assert.False(sut.HasRoute);
        Assert.Equal("", sut.DomainBadge);
        Assert.Null(sut.SelectedMenuRoute);
    }

    [Fact]
    public void Domain_badge_follows_the_current_page_domain()
    {
        var sut = new MainWindowViewModel(new NavigationService(), new FakeApiClient());
        Assert.Equal("", sut.DomainBadge);

        sut.SelectedMenuRoute = Routes.All[0];
        Assert.Equal("核心", sut.DomainBadge);

        sut.SelectedMenuRoute = Routes.All[8];
        Assert.Equal("扩展", sut.DomainBadge);

        sut.NavigateCommand.Execute(sut.SettingsRoute);
        Assert.Equal("设置", sut.DomainBadge);
    }

    /// <summary>
    /// 测试替身：按构造参数决定"取数成功"还是"抛 <see cref="TspApiException"/>"。
    /// Task 8 会复用它的 <see cref="IndexQuotes"/> 与 <see cref="IsFixtureData"/>。
    /// </summary>
    private sealed class FakeApiClient(TspApiException? failure = null) : ITspApiClient
    {
        public bool IsFixtureData { get; init; } = true;
        public IReadOnlyList<IndexQuote> IndexQuotes { get; init; } = [];

        public Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken cancellationToken) => Respond(IndexQuotes);
        public Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken cancellationToken) => Respond<IReadOnlyList<Instrument>>([]);
        public Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken) => Respond<IReadOnlyList<DailyBar>>([]);
        public Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken cancellationToken) => Respond<IReadOnlyList<WatchlistItem>>([]);

        private Task<T> Respond<T>(T value) => failure is null ? Task.FromResult(value) : Task.FromException<T>(failure);
    }
}
