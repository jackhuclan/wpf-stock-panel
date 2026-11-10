using Avalonia;

namespace Tsp.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 运行期资源（夹具 JSON）以 exe 所在目录为锚：双击启动与 dotnet run 的资源解析结果一致。
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
