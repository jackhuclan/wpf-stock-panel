namespace Tsp.Contracts.Tests;

public sealed class BaselineTest
{
    [Fact]
    public void Assembly_version_comes_from_Directory_Build_props()
    {
        var version = typeof(BaselineTest).Assembly.GetName().Version;

        Assert.Equal(new Version(0, 2, 8, 0), version);
    }
}
