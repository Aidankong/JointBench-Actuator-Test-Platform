namespace JointBench.TwinCat;

using System.Xml.Linq;

public sealed record PdoVariableLink(string PlcVariablePath, string EtherCatVariablePath);

public sealed record Ti5PdoLinkPlan(
    int VendorId,
    int ProductCode,
    int RevisionNumber,
    string BoxPath,
    IReadOnlyList<PdoVariableLink> Links,
    IReadOnlyList<string> MissingEntries,
    IReadOnlyList<string> Warnings);

public static class TwinCatPdoLinkPlanner
{
    private const string DefaultPlcProjectName = "JointBenchPlc";

    public static Ti5PdoLinkPlan BuildTi5Plan(EtherCatBoxInfo box)
    {
        if (!box.IsTi5)
        {
            throw new InvalidOperationException(
                $"Expected Ti5 slave 0x00522227/0x00009253/0x00010005, got 0x{box.VendorId:X8}/0x{box.ProductCode:X8}/0x{box.RevisionNo:X8}.");
        }

        var boxPath = box.PathName;
        var entries = File.Exists(box.XmlPath) ? ReadPdoEntries(box.XmlPath) : DefaultPdoEntries();
        var links = new List<PdoVariableLink>();
        var missing = new List<string>();
        var warnings = new List<string>();

        AddLink(links, missing, entries, "nTi5Statusword", boxPath, "0x6041:0", input: true, required: true);
        AddLink(links, missing, entries, "nTi5ActualPosition", boxPath, "0x6064:0", input: true, required: true);
        AddLink(links, missing, entries, "nTi5ActualVelocity", boxPath, "0x606c:0", input: true, required: true);
        AddLink(links, missing, entries, "nTi5ActualTorqueOrCurrent", boxPath, "0x6077:0", input: true, required: false);
        AddLink(links, missing, entries, "nTi5ModeOfOperationDisplay", boxPath, "0x6061:0", input: true, required: true);
        AddLink(links, missing, entries, "nTi5ErrorCode", boxPath, "0x603F:0", input: true, required: false);
        AddLink(links, missing, entries, "nTi5Controlword", boxPath, "0x6040:0", input: false, required: true);
        AddLink(links, missing, entries, "nTi5ModeOfOperation", boxPath, "0x6060:0", input: false, required: true);
        AddLink(links, missing, entries, "nTi5TargetPosition", boxPath, "0x607a:0", input: false, required: true);
        AddLink(links, missing, entries, "nTi5TargetVelocity", boxPath, "0x60ff:0", input: false, required: true);

        if (!entries.ContainsKey("0x603F:0"))
        {
            warnings.Add("Optional error-code PDO 0x603F:0 is not mapped; PLC nErrorCode will rely on local safety/fault status unless the PDO is added.");
        }

        warnings.Add("Optional temperature PDO entry is not present in the scanned Ti5 default PDO; PLC temperature feedback will remain zero unless a station-specific PDO is added.");

        return new Ti5PdoLinkPlan(box.VendorId, box.ProductCode, box.RevisionNo, boxPath, links, missing, warnings);
    }

    private static PdoVariableLink In(string plcField, string boxPath, string pdoName) =>
        new($"{PlcTaskPath("Inputs")}^MAIN.{plcField}", $"{boxPath}^{pdoName}");

    private static PdoVariableLink Out(string plcField, string boxPath, string pdoName) =>
        new($"{PlcTaskPath("Outputs")}^MAIN.{plcField}", $"{boxPath}^{pdoName}");

    private static string PlcTaskPath(string direction) =>
        $"TIPC^{DefaultPlcProjectName}^{DefaultPlcProjectName} Instance^PlcTask {direction}";

    private static void AddLink(
        ICollection<PdoVariableLink> links,
        ICollection<string> missing,
        IReadOnlyDictionary<string, string> entries,
        string plcField,
        string boxPath,
        string index,
        bool input,
        bool required)
    {
        if (entries.TryGetValue(index, out var pdoName))
        {
            links.Add(input ? In(plcField, boxPath, pdoName) : Out(plcField, boxPath, pdoName));
        }
        else if (required)
        {
            missing.Add(index);
        }
        else
        {
            missing.Add(index);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadPdoEntries(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        return doc.Descendants()
            .Where(element => element.Name.LocalName == "Entry")
            .Select(element => new
            {
                Key = $"{NormalizeIndex(Text(element, "Index"))}:{Text(element, "SubIndex", "0")}",
                Name = PdoVariablePathSegment(element),
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !entry.Key.StartsWith("0x0:", StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> DefaultPdoEntries() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["0x6041:0"] = "Transmit PDO mapping 0^Status Word",
            ["0x6064:0"] = "Transmit PDO mapping 0^ActualPosition",
            ["0x606c:0"] = "Transmit PDO mapping 0^ActualVelocity",
            ["0x6077:0"] = "Transmit PDO mapping 0^Torque Actual",
            ["0x6061:0"] = "Transmit PDO mapping 0^ModeOfOperationDisplay",
            ["0x6040:0"] = "Receive PDO mapping 0^Control Word",
            ["0x607a:0"] = "Receive PDO mapping 0^TargetPosition",
            ["0x60ff:0"] = "Receive PDO mapping 0^TargetVelocity",
            ["0x6060:0"] = "Receive PDO mapping 0^ModeOfOperation",
        };

    private static string PdoVariablePathSegment(XElement entry)
    {
        var entryName = Text(entry, "Name");
        var pdoName = entry.Parent is { } parent &&
            (parent.Name.LocalName.Equals("TxPdo", StringComparison.OrdinalIgnoreCase) ||
             parent.Name.LocalName.Equals("RxPdo", StringComparison.OrdinalIgnoreCase))
            ? Text(parent, "Name")
            : string.Empty;

        return string.IsNullOrWhiteSpace(pdoName) ? entryName : $"{pdoName}^{entryName}";
    }

    private static string NormalizeIndex(string value)
    {
        if (value.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            return $"0x{value[2..]}";
        }

        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value : $"0x{value}";
    }

    private static string Text(XContainer container, string name, string fallback = "") =>
        container.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? fallback;
}

public sealed record TwinCatPreparationRequest(
    string StationDirectory,
    bool Activate,
    string ProgId = "TcXaeShell.DTE.15.0");

public sealed record TwinCatPreparationReport(
    bool Ok,
    bool Activated,
    string Message,
    EtherCatScanReport? ScanReport,
    Ti5PdoLinkPlan? LinkPlan,
    TwinCatProjectProbeReport? ProjectReport = null);

public sealed class TwinCatPreparationService
{
    private readonly Func<string, EtherCatScanReport> scanner;
    private readonly Func<PreflightReport> preflight;
    private readonly ITwinCatProjectPreparer projectPreparer;
    private readonly string repositoryRoot;

    public TwinCatPreparationService()
        : this(new EtherCatScanProbe(), new SystemProbe())
    {
    }

    public TwinCatPreparationService(EtherCatScanProbe scanner, SystemProbe systemProbe)
        : this(scanner.Scan, systemProbe.CheckPrerequisites, new TwinCatProjectProbe())
    {
    }

    public TwinCatPreparationService(
        Func<string, EtherCatScanReport> scanner,
        Func<PreflightReport> preflight,
        ITwinCatProjectPreparer? projectPreparer = null,
        string? repositoryRoot = null)
    {
        this.scanner = scanner;
        this.preflight = preflight;
        this.projectPreparer = projectPreparer ?? new TwinCatProjectProbe();
        this.repositoryRoot = repositoryRoot ?? Environment.CurrentDirectory;
    }

    public TwinCatPreparationReport Prepare(TwinCatPreparationRequest request)
    {
        _ = StationConfigLoader.Load(request.StationDirectory);
        var preflightReport = preflight();
        if (!preflightReport.Ok)
        {
            return new TwinCatPreparationReport(false, false, "Prerequisite check failed.", null, null);
        }

        var scan = scanner(request.ProgId);
        if (!scan.Ok || !scan.Ti5Found)
        {
            if (request.Activate)
            {
                var refreshed = projectPreparer.RefreshLatest(new TwinCatProjectPreparationRequest(
                    repositoryRoot,
                    request.ProgId,
                    OutputRoot: null,
                    Activate: true));
                return refreshed.Ok
                    ? new TwinCatPreparationReport(
                        true,
                        refreshed.Activated,
                        refreshed.Activated
                            ? "Latest generated TwinCAT project refreshed with current PLC templates, existing PDO links preserved, configuration activated, TwinCAT restarted, and PLC runtime started."
                            : "Latest generated TwinCAT project refreshed with current PLC templates, but activation was not reported.",
                        scan,
                        refreshed.LinkPlan,
                        refreshed)
                    : new TwinCatPreparationReport(
                        false,
                        false,
                        $"Online scan failed and latest project refresh failed: {refreshed.Error}",
                        scan,
                        null,
                        refreshed);
            }

            return new TwinCatPreparationReport(false, false, scan.Error.Length > 0 ? scan.Error : "Ti5 slave was not found.", scan, null);
        }

        var ti5 = scan.Boxes.First(box => box.IsTi5);
        var plan = TwinCatPdoLinkPlanner.BuildTi5Plan(ti5);
        if (!request.Activate)
        {
            return new TwinCatPreparationReport(true, false, "TwinCAT preparation dry run completed. Activation was not requested.", scan, plan);
        }

        var projectReport = projectPreparer.Prepare(new TwinCatProjectPreparationRequest(
            repositoryRoot,
            request.ProgId,
            OutputRoot: null,
            Activate: true));
        if (!projectReport.Ok)
        {
            return new TwinCatPreparationReport(false, false, $"TwinCAT project activation failed: {projectReport.Error}", scan, plan, projectReport);
        }

        return new TwinCatPreparationReport(
            true,
            projectReport.Activated,
            projectReport.Activated
                ? "TwinCAT project generated, PDOs linked, configuration activated, TwinCAT restarted, and PLC runtime started."
                : "TwinCAT project generated and PDOs linked, but activation was not reported.",
            scan,
            projectReport.LinkPlan ?? plan,
            projectReport);
    }
}
