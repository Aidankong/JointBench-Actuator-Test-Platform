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
    private readonly EtherCatScanProbe etherCatScanProbe;
    private readonly TwinCatPreparationService preparationService;
    private readonly TwinCatProjectProbe projectProbe;

    public HelperApp(IOutput output)
        : this(
            output,
            new EsiService(),
            new SystemProbe(),
            new AdsSymbolValidator(),
            new AutomationProbe(),
            new EtherCatScanProbe(),
            new TwinCatPreparationService(),
            new TwinCatProjectProbe())
    {
    }

    public HelperApp(
        IOutput output,
        EsiService esiService,
        SystemProbe systemProbe,
        AdsSymbolValidator adsSymbolValidator,
        AutomationProbe automationProbe,
        EtherCatScanProbe etherCatScanProbe,
        TwinCatPreparationService? preparationService = null,
        TwinCatProjectProbe? projectProbe = null)
    {
        this.output = output;
        this.esiService = esiService;
        this.systemProbe = systemProbe;
        this.adsSymbolValidator = adsSymbolValidator;
        this.automationProbe = automationProbe;
        this.etherCatScanProbe = etherCatScanProbe;
        this.preparationService = preparationService ?? new TwinCatPreparationService();
        this.projectProbe = projectProbe ?? new TwinCatProjectProbe();
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
                "scan-spike" => RunScanSpike(commandLine),
                "prepare-twincat" => RunPrepareTwinCat(commandLine),
                "project-spike" => RunProjectSpike(commandLine),
                "run-sequence" => RunSequence(commandLine),
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

    private int RunScanSpike(CommandLine commandLine)
    {
        var result = etherCatScanProbe.Scan(commandLine.Option("prog-id") ?? "TcXaeShell.DTE.15.0");
        Write(result, commandLine.HasFlag("json"));
        return result.Ok && result.Ti5Found ? 0 : 2;
    }

    private int RunPrepareTwinCat(CommandLine commandLine)
    {
        var result = preparationService.Prepare(new TwinCatPreparationRequest(
            commandLine.RequireOption("station"),
            commandLine.HasFlag("activate"),
            commandLine.Option("prog-id") ?? "TcXaeShell.DTE.15.0"));
        Write(result, commandLine.HasFlag("json"));
        return result.Ok ? 0 : 2;
    }

    private int RunProjectSpike(CommandLine commandLine)
    {
        var result = projectProbe.CreateProjectWithPlcTemplates(
            commandLine.Option("repo-root") ?? Environment.CurrentDirectory,
            commandLine.Option("prog-id") ?? "TcXaeShell.DTE.15.0",
            commandLine.Option("output"));
        Write(result, commandLine.HasFlag("json"));
        return result.Ok ? 0 : 2;
    }

    private int RunSequence(CommandLine commandLine)
    {
        var station = StationConfigLoader.Load(commandLine.RequireOption("station"));
        var language = ParseLanguage(commandLine.Option("language"));
        var fake = commandLine.HasFlag("fake");
        if (!fake && !commandLine.HasFlag("confirm-motion"))
        {
            throw new InvalidOperationException("Real motion requires --confirm-motion after physical E-stop, fixture, and current-limited power are confirmed.");
        }

        using IAdsSymbolClient client = fake ? new FakeAdsSymbolClient() : new BeckhoffAdsSymbolClient();
        var adapter = new AdsMotionAdapter(client, station.Ads);
        var runner = new ProductionTestSequenceRunner(adapter, new TestReportWriter());
        var request = new ProductionSequenceRequest(
            commandLine.Option("reports") ?? Path.Combine(Environment.CurrentDirectory, "reports"),
            language,
            station.Ads,
            station.Safety,
            station.Tests);
        var result = runner.RunAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Write(result, commandLine.HasFlag("json"));
        return result.OverallResult == "PASS" ? 0 : 2;
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
              scan-spike [--prog-id TcXaeShell.DTE.15.0] [--json]
              prepare-twincat --station <dir> [--activate] [--prog-id TcXaeShell.DTE.15.0] [--json]
              project-spike [--repo-root <dir>] [--output <dir>] [--prog-id TcXaeShell.DTE.15.0] [--json]
              run-sequence --station <dir> [--language zh-CN|en-US] [--reports <dir>] [--confirm-motion] [--fake] [--json]
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
            case EtherCatScanReport report:
                output.WriteLine($"EtherCAT scan spike: {(report.Ok ? "OK" : "FAILED")}");
                output.WriteLine($"Ti5 found: {report.Ti5Found}");
                output.WriteLine($"Temp root: {report.TempRoot}");
                if (!report.Ok)
                {
                    output.WriteLine(report.Error);
                    break;
                }

                foreach (var master in report.Masters)
                {
                    output.WriteLine(
                        $"Master {master.Index}: {master.ItemSubTypeName} {master.DeviceDescription} {master.DeviceData}");
                }

                foreach (var box in report.Boxes)
                {
                    output.WriteLine(
                        $"Box {box.MasterIndex}.{box.BoxIndex}: {box.Name}, vendor 0x{box.VendorId:X8}, product 0x{box.ProductCode:X8}, revision 0x{box.RevisionNo:X8}, phys {box.PhysAddr}");
                    output.WriteLine($"    ESI: {box.EsiFile}");
                    output.WriteLine($"    XML: {box.XmlPath}");
                }

                break;
            case TwinCatPreparationReport report:
                output.WriteLine($"TwinCAT preparation: {(report.Ok ? "OK" : "FAILED")}");
                output.WriteLine($"Activated: {report.Activated}");
                output.WriteLine(report.Message);
                if (report.LinkPlan is not null)
                {
                    output.WriteLine($"Ti5: vendor 0x{report.LinkPlan.VendorId:X8}, product 0x{report.LinkPlan.ProductCode:X8}, revision 0x{report.LinkPlan.RevisionNumber:X8}");
                    foreach (var link in report.LinkPlan.Links)
                    {
                        output.WriteLine($"Link: {link.PlcVariablePath} <= {link.EtherCatVariablePath}");
                    }
                }

                break;
            case TwinCatProjectProbeReport report:
                output.WriteLine($"TwinCAT project spike: {(report.Ok ? "OK" : "FAILED")}");
                output.WriteLine($"Temp root: {report.TempRoot}");
                output.WriteLine($"PLC project: {report.PlcProjectName}");
                output.WriteLine($"PLC build: {(report.PlcBuildSucceeded ? "OK" : "FAILED")}");
                if (!report.Ok)
                {
                    output.WriteLine(report.Error);
                    break;
                }

                foreach (var path in report.ImportedPouTemplates)
                {
                    output.WriteLine($"Imported: {path}");
                }

                if (report.LinkPlan is not null)
                {
                    output.WriteLine($"Ti5: vendor 0x{report.LinkPlan.VendorId:X8}, product 0x{report.LinkPlan.ProductCode:X8}, revision 0x{report.LinkPlan.RevisionNumber:X8}");
                    foreach (var link in report.LinkedVariables)
                    {
                        output.WriteLine($"Linked: {link}");
                    }
                    foreach (var warning in report.LinkPlan.Warnings)
                    {
                        output.WriteLine($"Warning: {warning}");
                    }
                }

                break;
            case ProductionSequenceResult result:
                output.WriteLine($"Production sequence: {result.OverallResult}");
                output.WriteLine($"Test ID: {result.TestId}");
                output.WriteLine($"Output: {result.OutputDirectory}");
                foreach (var stage in result.StageResults)
                {
                    output.WriteLine($"[{stage.Result}] {stage.StageName}: {string.Join("; ", stage.FailureReasons)}");
                }

                break;
            default:
                output.WriteLine(value.ToString() ?? string.Empty);
                break;
        }
    }

    private static ReportLanguage ParseLanguage(string? language) =>
        string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)
            ? ReportLanguage.SimplifiedChinese
            : ReportLanguage.English;
}
