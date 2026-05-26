namespace JointBench.TwinCatHelper.Tests;

public sealed class HelperAppTests
{
    [Fact]
    public void HelpCommandReturnsSuccess()
    {
        var output = new BufferOutput();
        var exitCode = new HelperApp(output).Run(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("check-prereqs", output.Text);
    }

    private sealed class BufferOutput : IOutput
    {
        private readonly StringWriter writer = new();

        public string Text => writer.ToString();

        public void WriteLine(string message) => writer.WriteLine(message);

        public void WriteError(string message) => writer.WriteLine(message);
    }
}
