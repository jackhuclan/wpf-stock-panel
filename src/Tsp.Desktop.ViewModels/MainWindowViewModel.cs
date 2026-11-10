using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tsp.Contracts.Market;
using Tsp.Desktop.ViewModels.Navigation;
using Tsp.Desktop.ViewModels.Services;

namespace Tsp.Desktop.ViewModels;

/// <summary>
/// 主窗口壳的 ViewModel：侧栏菜单、当前页与首屏取数。它不认识 Avalonia——
/// 主窗口只负责把这里的状态摆到界面上，导航与数据失败的判断全部落在本类型，因而可直接单测。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ITspApiClient _apiClient;

    /// <summary>
    /// "当前页由导航服务同步过来"期间的闸门。见 <see cref="OnNavigationCurrentChanged"/>。
    /// </summary>
    private bool _isSyncingFromNavigation;

    /// <summary>
    /// 组装壳的 ViewModel。两个依赖都是接口：导航是纯 ViewModel 层实现，取数是夹具还是 P2 的 HTTP 与本层无关。
    /// </summary>
    /// <param name="navigation">导航服务，当前页的唯一事实来源。</param>
    /// <param name="apiClient">行情数据访问，P0 为夹具实现。</param>
    public MainWindowViewModel(INavigationService navigation, ITspApiClient apiClient)
    {
        _navigation = navigation;
        _apiClient = apiClient;
        _navigation.CurrentChanged += OnNavigationCurrentChanged;
    }

    /// <summary>
    /// 侧栏 19 项菜单，按 <see cref="PageRoute.Order"/> 升序。设置页不在其中，它由侧栏底部入口单独承载。
    /// </summary>
    public IReadOnlyList<PageRoute> Menu => Routes.All;

    /// <summary>
    /// 侧栏底部"设置"入口要导航到的路由。
    /// </summary>
    public PageRoute SettingsRoute => Routes.SettingsRoute;

    /// <summary>
    /// 当前页，与 <see cref="INavigationService.Current"/> 同步；未知路由停留在空状态时为 <c>null</c>。
    /// 只由导航服务驱动写入——视图侧的点击走 <see cref="SelectedMenuRoute"/>。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoute))]
    [NotifyPropertyChangedFor(nameof(IsRouteMissing))]
    [NotifyPropertyChangedFor(nameof(DomainBadge))]
    private PageRoute? _currentRoute;

    /// <summary>
    /// 侧栏菜单的选中项，两向绑定到 <c>ListBox.SelectedItem</c>。它可与 <see cref="CurrentRoute"/> 不同：
    /// 设置页与空状态都不在 <see cref="Menu"/> 内，此时侧栏留空而当前页照常显示。
    /// 由视图写入的每一次变更都当作一次导航意图（见 <see cref="OnSelectedMenuRouteChanged"/>）。
    /// </summary>
    [ObservableProperty]
    private PageRoute? _selectedMenuRoute;

    /// <summary>
    /// 最近一次成功取到的指数快照，侧栏指数条的数据源。顺序即数据源给出的顺序。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexBar))]
    private IReadOnlyList<IndexQuote> _indexQuotes = [];

    /// <summary>
    /// 当前数据是否来自夹具。界面据此明示"你看到的不是真实行情"。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFixtureBadge))]
    private bool _isFixtureData;

    /// <summary>
    /// 取数失败的**可读**说明，取自 <see cref="TspApiException.Message"/>；无失败时为 <c>null</c>。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// 指数条的绑定源：<see cref="IndexQuotes"/> 的界面投影（文案 + 涨跌方向），随每次成功取数刷新。
    /// 不另存一份状态——它就是快照的投影，快照一变就跟着变。
    /// </summary>
    public IReadOnlyList<IndexQuoteRow> IndexBar => IndexQuotes.Select(IndexQuoteRow.From).ToList();

    /// <summary>
    /// 是否显示"夹具数据"角标。**恒等于客户端的自报身份**（<see cref="ITspApiClient.IsFixtureData"/>）：
    /// 角标存在的全部意义就是让用户无法把夹具数据当成真实行情，若由 View 自己判断（按数据形状、按路由猜），
    /// 这个保证就没了——那正是接口带着这个属性的理由。
    /// 它是 <see cref="IsFixtureData"/> 的别名而非第二份状态：两者不可能各自漂移。
    /// </summary>
    public bool ShowFixtureBadge => IsFixtureData;

    /// <summary>
    /// 是否处于取数失败态（<see cref="ErrorMessage"/> 非空）。
    /// 它与另外两件事互不相干：<see cref="IsRouteMissing"/> 说的是"这条路由没注册"，
    /// 本属性说的是"客户端这次没读到数据"，<see cref="ShowFixtureBadge"/> 说的是"读到的是夹具"。
    /// 三者都不由对方推导，界面也不得把它们合并成一种状态。
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// 是否停在某一页上（含设置页）。为 <c>false</c> 即空状态。
    /// </summary>
    public bool HasRoute => CurrentRoute is not null;

    /// <summary>
    /// 是否停在空状态（<see cref="CurrentRoute"/> 为 <c>null</c>）。
    /// </summary>
    public bool IsRouteMissing => CurrentRoute is null;

    /// <summary>
    /// 当前页的域徽标文案：核心 / 扩展 / 设置。空状态下为空串——没有页就没有徽标。
    /// 映射放在这里而不是 XAML：枚举到中文的对应关系是可单测的判断，不必为它引入值转换器。
    /// </summary>
    public string DomainBadge => CurrentRoute?.Domain switch
    {
        PageDomain.Core => "核心",
        PageDomain.Ext => "扩展",
        PageDomain.Settings => "设置",
        _ => "",
    };

    /// <summary>
    /// 首屏装配：落在菜单第一项，并拉一次指数快照确认数据可读。
    /// 取数失败**不外抛**——<see cref="TspApiException"/> 被就地转成 <see cref="ErrorMessage"/>，
    /// 壳照常可用（这是界面显示可读错误而非崩溃或白屏的落点）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        // 先落页再取数：数据失败不该让壳停在空状态页，那是"路由没找到"的语义，两回事。
        _navigation.Navigate(Routes.All[0].Key);

        try
        {
            // 快照落在侧栏指数条上；这次调用同时是"夹具可读"的确认。
            IndexQuotes = await _apiClient.GetIndexQuotesAsync(CancellationToken.None);
            IsFixtureData = _apiClient.IsFixtureData;

            // 重跑成功要抹掉上一次的错误，否则界面会留着一条已经过期的失败提示。
            ErrorMessage = null;
        }
        catch (TspApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// 导航到某一页。视图、设置入口与后续页面都经这里进入，保证"改当前页"只有一个入口。
    /// </summary>
    /// <param name="route">目标路由。未注册的路由键会让导航服务停在空状态，不抛异常。</param>
    [RelayCommand]
    private void Navigate(PageRoute route) => _navigation.Navigate(route.Key);

    /// <summary>
    /// 当前页只由导航服务驱动：它同时是 <see cref="CurrentRoute"/> 与侧栏选中态的事实来源。
    /// </summary>
    private void OnNavigationCurrentChanged(object? sender, PageRoute? route)
    {
        CurrentRoute = route;

        // 侧栏选中态跟随当前页，但仅当该页在 19 页菜单内：设置页与空状态都让侧栏留空。
        // 这次赋值是"同步"而非"用户在侧栏点了一下"，所以要先合上闸门，
        // 否则它会顺着 OnSelectedMenuRouteChanged 再导航一次，把同一页重复入栈。
        _isSyncingFromNavigation = true;
        try
        {
            SelectedMenuRoute = route is not null && Menu.Contains(route) ? route : null;
        }
        finally
        {
            _isSyncingFromNavigation = false;
        }
    }

    /// <summary>
    /// 侧栏选中项由视图写入，即一次导航意图。
    /// <c>null</c> 不算意图：ListBox 对"选中项不在 Items 内"（设置页就是这种情况）的回写就是 <c>null</c>，
    /// 若照它导航，当前页会被打回空状态。
    /// </summary>
    partial void OnSelectedMenuRouteChanged(PageRoute? value)
    {
        if (_isSyncingFromNavigation || value is null)
        {
            return;
        }

        _navigation.Navigate(value.Key);
    }
}
