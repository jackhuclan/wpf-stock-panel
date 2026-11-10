using Avalonia.Controls;
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
}
