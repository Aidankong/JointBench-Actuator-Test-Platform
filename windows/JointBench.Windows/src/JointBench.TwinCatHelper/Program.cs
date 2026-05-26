namespace JointBench.TwinCatHelper;

public static class Program
{
    public static int Main(string[] args) => new HelperApp(new ConsoleOutput()).Run(args);
}
