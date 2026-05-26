namespace JointBench.TwinCatHelper;

public interface IOutput
{
    void WriteLine(string message);

    void WriteError(string message);
}

public sealed class ConsoleOutput : IOutput
{
    public void WriteLine(string message) => Console.WriteLine(message);

    public void WriteError(string message) => Console.Error.WriteLine(message);
}
