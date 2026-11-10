using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Tsp.Desktop.ViewModels;

namespace Tsp.Desktop.Views;

/// <summary>
/// 主窗口。它只负责把 <see cref="MainWindowViewModel"/> 摆到屏幕上并触发首屏装配，
/// 不含任何导航或取数判断（那些在 ViewModel 里，可脱离 Avalonia 单测）。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 无参构造：供设计器与运行时 XAML 加载器构造。应用运行时走下面注入 ViewModel 的那个重载，
    /// 本重载构造出的窗口没有 DataContext。
    /// </summary>
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// 由 DI 注入 ViewModel 后构造。
    /// </summary>
    /// <param name="viewModel">主窗口壳的 ViewModel。</param>
    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;

        // 首屏装配：落在菜单第一项并拉一次数据。取数失败在 ViewModel 内转成 ErrorMessage，不外抛。
        viewModel.LoadCommand.Execute(null);
    }

    /// <summary>
    /// 明暗主题切换。这是 P0 用来验证"运行中切主题同帧生效"的入口（P1/P2 并入设置页），
    /// 是纯视图层行为，因此留在 code-behind 而不上 ViewModel——它没有任何可单测的判断。
    ///
    /// 只翻应用级的主题变体：所有颜色都经 {DynamicResource} 取令牌，翻完之后
    /// 侧栏、内容区与 1px 分隔线会在同一次布局里全部重取，不会残留上一主题的色值。
    /// </summary>
    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        // 设计器与 XAML 加载器构造窗口时 Application.Current 可能尚未就绪，安静退出即可。
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = application.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}
