namespace JointBench.TwinCat;

public sealed record CheckItem(string Name, string Status, string Message, string? Detail = null)
{
    public bool IsError => Status.Equals("error", StringComparison.OrdinalIgnoreCase);
}

public sealed record PreflightReport(DateTimeOffset GeneratedAtUtc, IReadOnlyList<CheckItem> Checks)
{
    public bool Ok => Checks.All(check => !check.IsError);
}

public sealed record EsiSummary(
    string VendorName,
    string VendorId,
    string DeviceType,
    string ProductCode,
    string RevisionNumber)
{
    public string Label =>
        $"{VendorName} {DeviceType} (vendor {VendorId}, product {ProductCode}, revision {RevisionNumber})";
}

public sealed record EsiInstallResult(string SourcePath, string TargetPath, bool DryRun, EsiSummary Summary);

public sealed record AdsConnectionOptions(string AmsNetId, int Port, string SymbolPrefix);

public sealed record AdsSymbolSpec(string Name, string ExpectedType);

public sealed record AdsSymbolResult(string Name, string ExpectedType, bool Ok, string Message);

public sealed record AdsSymbolCheckReport(
    string AmsNetId,
    int Port,
    string SymbolPrefix,
    bool Ok,
    IReadOnlyList<AdsSymbolResult> Symbols);

public sealed record AutomationSmokeResult(
    string ProgId,
    bool Ok,
    string Name,
    string Version,
    string? OpenedSolution,
    string Error);

public sealed record EtherCatMasterInfo(
    int Index,
    int ItemSubType,
    string ItemSubTypeName,
    string DeviceDescription,
    string DeviceName,
    string DeviceData);

public sealed record EtherCatBoxInfo(
    int MasterIndex,
    int BoxIndex,
    string Name,
    string PathName,
    int ItemSubType,
    string ItemSubTypeName,
    int VendorId,
    int ProductCode,
    int RevisionNo,
    int SerialNo,
    int PhysAddr,
    int AutoIncAddr,
    string EsiFile,
    string XmlPath)
{
    public bool IsTi5 =>
        VendorId == 0x00522227 &&
        ProductCode == 0x00009253 &&
        RevisionNo == 0x00010005;
}

public sealed record EtherCatScanReport(
    bool Ok,
    string Error,
    string TempRoot,
    string? FoundDevicesXmlPath,
    IReadOnlyList<EtherCatMasterInfo> Masters,
    IReadOnlyList<EtherCatBoxInfo> Boxes,
    bool Ti5Found);
