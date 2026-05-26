namespace JointBench.TwinCatHelper.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void ParsesOptionsFlagsAndPositionals()
    {
        var parsed = CommandLine.Parse(
        [
            "install-esi",
            "--file",
            "C:\\Temp\\ti5.xml",
            "--target-dir=C:\\TwinCAT\\3.1\\Config\\Io\\EtherCAT",
            "--dry-run",
            "extra",
        ]);

        Assert.Equal("install-esi", parsed.Command);
        Assert.Equal("C:\\Temp\\ti5.xml", parsed.RequireOption("file"));
        Assert.Equal("C:\\TwinCAT\\3.1\\Config\\Io\\EtherCAT", parsed.RequireOption("target-dir"));
        Assert.True(parsed.HasFlag("dry-run"));
        Assert.Equal(["extra"], parsed.Positionals);
    }
}
