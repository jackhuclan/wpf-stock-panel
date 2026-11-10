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
    public async Task Load_populates_index_quotes_and_shows_fixture_badge()
    {
        var client = new FakeApiClient { IndexQuotes = [new IndexQuote("000001.SH", "上证指数", 3697.52m, 0.19m)] };
        var sut = new MainWindowViewModel(new NavigationService(), client);

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.Single(sut.IndexQuotes);
        Assert.True(sut.ShowFixtureBadge);
    }

    [Fact]
    public async Task Fixture_badge_is_hidden_when_client_is_not_fixture_data()
    {
        var client = new FakeApiClient { IsFixtureData = false };
        var sut = new MainWindowViewModel(new NavigationService(), client);

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.False(sut.ShowFixtureBadge);
    }

    [Fact]
    public async Task Index_bar_rows_carry_the_display_text_and_direction()
    {
        // 指数条的格子只绑 ViewModel：文案（名称 / 点位 / 涨跌幅）与涨跌方向都在这里定，
        // View 只负责把方向映射成红涨绿跌的令牌色。方向判断可单测，所以它不在 XAML 的值转换器里。
        var client = new FakeApiClient
        {
            IndexQuotes =
            [
                new IndexQuote("000001.SH", "上证指数", 3697.52m, 0.19m),
                new IndexQuote("399006.SZ", "创业板指", 2658.23m, -0.31m),
            ],
        };
        var sut = new MainWindowViewModel(new NavigationService(), client);

        await sut.LoadCommand.ExecuteAsync(null);

        Assert.Equal(
            new[]
            {
                ("上证指数", "3697.52", "+0.19%", true, false),
                // 跌：数字自带负号，不靠颜色单独承载涨跌信息（灰度截图与色觉障碍下同样读得出来）。
                ("创业板指", "2658.23", "-0.31%", false, true),
            },
            sut.IndexBar.Select(row => (row.Name, row.CloseText, row.ChangeText, row.IsUp, row.IsDown)));
    }

    [Fact]
    public async Task Successful_load_clears_a_stale_error_message()
    {
        // Task 8 起这条错误在内容区是**可见的**：若成功取数之后不抹掉上一次的失败提示，
        // 界面就会一边显示着数据、一边挂着一句已经过期的"读不到数据"——那是界面在说谎。
        var client = new FakeApiClient(new TspApiException("/gone: 夹具目录不存在"));
        var sut = new MainWindowViewModel(new NavigationService(), client);
        await sut.LoadCommand.ExecuteAsync(null);
        Assert.True(sut.HasError);

        client.Failure = null;
        await sut.LoadCommand.ExecuteAsync(null);

        Assert.Null(sut.ErrorMessage);
        Assert.False(sut.HasError);
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
    /// 测试替身：按 <see cref="Failure"/> 决定"取数成功"还是"抛 <see cref="TspApiException"/>"。
    /// 它是可写属性而不是只读入参——Task 8 要验证"失败之后的下一次成功必须抹掉过期错误"，
    /// 同一次装配就得既能失败又能成功。
    /// </summary>
    private sealed class FakeApiClient(TspApiException? failure = null) : ITspApiClient
    {
        public TspApiException? Failure { get; set; } = failure;

        public bool IsFixtureData { get; init; } = true;
        public IReadOnlyList<IndexQuote> IndexQuotes { get; init; } = [];

        public Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken cancellationToken) => Respond(IndexQuotes);
        public Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken cancellationToken) => Respond<IReadOnlyList<Instrument>>([]);
        public Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken) => Respond<IReadOnlyList<DailyBar>>([]);
        public Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken cancellationToken) => Respond<IReadOnlyList<WatchlistItem>>([]);

        private Task<T> Respond<T>(T value) => Failure is null ? Task.FromResult(value) : Task.FromException<T>(Failure);
    }
}
