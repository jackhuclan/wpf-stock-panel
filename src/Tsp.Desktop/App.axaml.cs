using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Tsp.Desktop.ViewModels;
using Tsp.Desktop.ViewModels.Navigation;
using Tsp.Desktop.ViewModels.Services;
using Tsp.Desktop.Views;

namespace Tsp.Desktop;

/// <summary>
/// 应用装配点：本类只做两件事——注册服务、创建主窗口。业务判断一律不在这里，
/// 它属于 <see cref="MainWindowViewModel"/>（可脱离 Avalonia 单测）。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ServiceCollection services = new();

        // 生命周期见 AGENTS §8：四项全部 Singleton。
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ITspApiClient>(_ => new FixtureApiClient(Path.Combine(AppContext.BaseDirectory, "Fixtures")));
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += OnDesktopExit;
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 进程退出时释放 DI 容器（其中的单例若实现了 <see cref="IDisposable"/> 会被一并释放）。
    /// </summary>
    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _serviceProvider?.Dispose();
    }
}
