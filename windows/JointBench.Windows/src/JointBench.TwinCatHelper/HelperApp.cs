using System.Text.Json;
using JointBench.TwinCat;

namespace JointBench.TwinCatHelper;

public sealed class HelperApp
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IOutput output;
    private readonly EsiService esiService;
    private readonly SystemProbe systemProbe;
    private readonly AdsSymbolValidator adsSymbolValidator;
    private readonly AutomationProbe automationProbe;

    public HelperApp(IOutput output)
        : this(output, new EsiService(), new SystemProbe(), new AdsSymbolValidator(), new AutomationProbe())
    {
    }

    public HelperApp(
        IOutput output,
        EsiService esiService,
        SystemProbe systemProbe,
        AdsSymbolValidator adsSymbolValidator,
        AutomationProbe automationProbe)
    {
        this.output = output;
        this.esiService = esiService;
        this.systemProbe = systemProbe;
        this.adsSymbolValidator = adsSymbolValidator;
        this.automationProbe = automationProbe;
    }

    public int Run(string[] args)
    {
        try
        {
            var commandLine = CommandLine.Parse(args);
            return commandLine.Command switch
            {
                "help" => PrintHelp(),
                "check-prereqs" => RunCheckPrereqs(commandLine),
                "twincat-info" => RunTwinCatInfo(commandLine),
                "esi-summary" => RunEsiSummary(commandLine),
                "install-esi" => RunInstallEsi(commandLine),
                "check-ads-symbols" => RunCheckAdsSymbols(commandLine),
                "automation-smoke" => RunAutomationSmoke(commandLine),
                _ => UnknownCommand(commandLine.Command),
            };
        }
        catch (Exception exc)
        {
            output.WriteError(exc.Message);
            return 1;
        }
    }

    private int RunCheckPrereqs(CommandLine commandLine)
    {
        var report = systemProbe.CheckPrerequisites();
        Write(report, commandLine.HasFlag("json"));
        return report.Ok ? 0 : 2;
    }

    private int RunTwinCatInfo(CommandLine commandLine)
    {
        var report = systemProbe.CheckPrerequisites();
        Write(report, commandLine.HasFlag("json"));
        return 0;
    }

    private int RunEsiSummary(CommandLine commandLine)
    {
        var summary = esiService.ReadSummary(commandLine.RequireOption("file"));
        Write(summary, commandLine.HasFlag("json"));
        return 0;
    }

    private int RunInstallEsi(CommandLine commandLine)
    {
        var result = esiService.Install(
            commandLine.RequireOption("file"),
            commandLine.Option("target-dir"),
            commandLine.HasFlag("dry-run"));
        Write(result, commandLine.HasFlag("json"));
        return 0;
    }

    private int RunCheckAdsSymbols(CommandLine commandLine)
    {
        var options = new AdsConnectionOptions(
            commandLine.RequireOption("ams"),
            int.Parse(commandLine.Option("port") ?? "851"),
            commandLine.Option("prefix") ?? "MAIN.stJointBench");
        var report = adsSymbolValidator.Check(options);
        Write(report, commandLine.HasFlag("json"));
        return report.Ok ? 0 : 2;
    }

    private int RunAutomationSmoke(CommandLine commandLine)
    {
        var result = automationProbe.Smoke(
            commandLine.Option("prog-id") ?? "TcXaeShell.DTE.15.0",
            commandLine.Option("solution"));
        Write(result, commandLine.HasFlag("json"));
        return result.Ok ? 0 : 2;
    }

    private int PrintHelp()
    {
        output.WriteLine(
            """
            JointBench TwinCAT Helper

            Commands:
              check-prereqs [--json]
              twincat-info [--json]
              esi-summary --file <path> [--json]
              install-esi --file <path> [--target-dir <dir>] [--dry-run] [--json]
              check-ads-symbols --ams <ams-net-id> [--port 851] [--prefix MAIN.stJointBench] [--json]
              automation-smoke [--prog-id TcXaeShell.DTE.15.0] [--solution <path>] [--json]
            """);
        return 0;
    }

    private int UnknownCommand(string command)
    {
        output.WriteError($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private void Write(object value, bool json)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            return;
        }

        switch (value)
        {
            case PreflightReport report:
                output.WriteLine($"JointBench TwinCAT prerequisite report: {(report.Ok ? "OK" : "FAILED")}");
                foreach (var check in report.Checks)
                {
                    output.WriteLine($"[{check.Status}] {check.Name}: {check.Message}");
                    if (!string.IsNullOrWhiteSpace(check.Detail))
                    {
                        output.WriteLine($"    {check.Detail}");
                    }
                }

                break;
            case EsiSummary summary:
                output.WriteLine(summary.Label);
                break;
            case EsiInstallResult result:
                output.WriteLine(result.DryRun ? "ESI dry run completed." : "ESI installed.");
                output.WriteLine(result.Summary.Label);
                output.WriteLine($"Source: {result.SourcePath}");
                output.WriteLine($"Target: {result.TargetPath}");
                break;
            case AdsSymbolCheckReport report:
                output.WriteLine($"ADS symbol check: {(report.Ok ? "OK" : "FAILED")}");
                output.WriteLine($"Target: {report.AmsNetId}:{report.Port} {report.SymbolPrefix}");
                foreach (var symbol in report.Symbols)
                {
                    output.WriteLine($"[{(symbol.Ok ? "ok" : "error")}] {symbol.Name} ({symbol.ExpectedType}): {symbol.Message}");
                }

                break;
            case AutomationSmokeResult result:
                output.WriteLine($"Automation smoke: {(result.Ok ? "OK" : "FAILED")}");
                output.WriteLine($"ProgID: {result.ProgId}");
                if (result.Ok)
                {
                    output.WriteLine($"Name: {result.Name}");
                    output.WriteLine($"Version: {result.Version}");
                    if (!string.IsNullOrWhiteSpace(result.OpenedSolution))
                    {
                        output.WriteLine($"Solution: {result.OpenedSolution}");
                    }
                }
                else
                {
                    output.WriteLine(result.Error);
                }

                break;
            default:
                output.WriteLine(value.ToString() ?? string.Empty);
                break;
        }
    }
}
