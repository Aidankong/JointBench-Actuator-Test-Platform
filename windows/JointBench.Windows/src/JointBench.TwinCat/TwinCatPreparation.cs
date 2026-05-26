namespace JointBench.TwinCat;

public sealed record PdoVariableLink(string PlcVariablePath, string EtherCatVariablePath);

public sealed record Ti5PdoLinkPlan(
    int VendorId,
    int ProductCode,
    int RevisionNumber,
    string BoxPath,
    IReadOnlyList<PdoVariableLink> Links);

public static class TwinCatPdoLinkPlanner
{
    public static Ti5PdoLinkPlan BuildTi5Plan(EtherCatBoxInfo box)
    {
        if (!box.IsTi5)
        {
            throw new InvalidOperationException(
                $"Expected Ti5 slave 0x00522227/0x00009253/0x00010005, got 0x{box.VendorId:X8}/0x{box.ProductCode:X8}/0x{box.RevisionNo:X8}.");
        }

        var boxPath = box.PathName;
        var links = new List<PdoVariableLink>
        {
            In("nStatusword", boxPath, "Statusword"),
            In("nActualPosition", boxPath, "Actual position"),
            In("nActualVelocity", boxPath, "Actual velocity"),
            In("nActualTorqueOrCurrent", boxPath, "Torque actual value"),
            In("nErrorCode", boxPath, "Error code"),
            Out("nControlword", boxPath, "Controlword"),
            Out("nModeOfOperation", boxPath, "Modes of operation"),
            Out("nTargetPosition", boxPath, "Target position"),
        };

        return new Ti5PdoLinkPlan(box.VendorId, box.ProductCode, box.RevisionNo, boxPath, links);
    }

    private static PdoVariableLink In(string plcField, string boxPath, string pdoName) =>
        new($"TIPC^PlcTask Inputs^MAIN.stTi5In.{plcField}", $"{boxPath}^{pdoName}");

    private static PdoVariableLink Out(string plcField, string boxPath, string pdoName) =>
        new($"TIPC^PlcTask Outputs^MAIN.stTi5Out.{plcField}", $"{boxPath}^{pdoName}");
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
    Ti5PdoLinkPlan? LinkPlan);

public sealed class TwinCatPreparationService
{
    private readonly EtherCatScanProbe scanner;
    private readonly SystemProbe systemProbe;

    public TwinCatPreparationService()
        : this(new EtherCatScanProbe(), new SystemProbe())
    {
    }

    public TwinCatPreparationService(EtherCatScanProbe scanner, SystemProbe systemProbe)
    {
        this.scanner = scanner;
        this.systemProbe = systemProbe;
    }

    public TwinCatPreparationReport Prepare(TwinCatPreparationRequest request)
    {
        _ = StationConfigLoader.Load(request.StationDirectory);
        var preflight = systemProbe.CheckPrerequisites();
        if (!preflight.Ok)
        {
            return new TwinCatPreparationReport(false, false, "Prerequisite check failed.", null, null);
        }

        var scan = scanner.Scan(request.ProgId);
        if (!scan.Ok || !scan.Ti5Found)
        {
            return new TwinCatPreparationReport(false, false, scan.Error.Length > 0 ? scan.Error : "Ti5 slave was not found.", scan, null);
        }

        var ti5 = scan.Boxes.First(box => box.IsTi5);
        var plan = TwinCatPdoLinkPlanner.BuildTi5Plan(ti5);
        if (!request.Activate)
        {
            return new TwinCatPreparationReport(true, false, "TwinCAT preparation dry run completed. Activation was not requested.", scan, plan);
        }

        return new TwinCatPreparationReport(
            true,
            false,
            "TwinCAT scan and link plan completed. Automatic activation is gated until project import/link execution is verified on this engineering station.",
            scan,
            plan);
    }
}
