namespace JointBench.TwinCatHelper;

public sealed class CommandLine
{
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "dry-run",
        "json",
    };

    private readonly Dictionary<string, string> options;
    private readonly HashSet<string> flags;

    private CommandLine(
        string command,
        Dictionary<string, string> options,
        HashSet<string> flags,
        IReadOnlyList<string> positionals)
    {
        Command = command;
        this.options = options;
        this.flags = flags;
        Positionals = positionals;
    }

    public string Command { get; }

    public IReadOnlyList<string> Positionals { get; }

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            return new CommandLine("help", [], [], []);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var index = 1; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            var option = token[2..];
            var equalsIndex = option.IndexOf('=');
            if (equalsIndex >= 0)
            {
                options[option[..equalsIndex]] = option[(equalsIndex + 1)..];
                continue;
            }

            if (!KnownFlags.Contains(option) && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[option] = args[++index];
            }
            else
            {
                flags.Add(option);
            }
        }

        return new CommandLine(command, options, flags, positionals);
    }

    public bool HasFlag(string name) => flags.Contains(name);

    public string? Option(string name) => options.TryGetValue(name, out var value) ? value : null;

    public string RequireOption(string name)
    {
        var value = Option(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option --{name}.");
        }

        return value;
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";
}
