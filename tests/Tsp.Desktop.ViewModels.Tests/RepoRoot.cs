namespace Tsp.Desktop.ViewModels.Tests;

/// <summary>
/// 仓库根的定位。从测试自己的输出目录向上走到仓库根（以 <c>Tsp.slnx</c> 为标记），
/// 不写死绝对路径，换机器、换检出位置都成立。
/// 需要读仓库内真实文件的测试（生产夹具、<c>.axaml</c> 视图）共用这一处，
/// 免得各自抄一份上溯逻辑、日后只改对一处。
/// </summary>
internal static class RepoRoot
{
    /// <summary>
    /// 仓库根目录的绝对路径。
    /// </summary>
    public static string Directory { get; } = Locate();

    /// <summary>
    /// 由仓库根起拼出一条路径。
    /// </summary>
    /// <param name="relativePath">相对仓库根的路径，如 <c>src/Tsp.Desktop/Themes/Tokens.axaml</c>。</param>
    /// <returns>绝对路径。</returns>
    public static string Combine(string relativePath) => Path.Combine(Directory, relativePath);

    private static string Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tsp.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException($"没能从 {AppContext.BaseDirectory} 向上找到仓库根（Tsp.slnx）。");
        }

        return directory.FullName;
    }
}
