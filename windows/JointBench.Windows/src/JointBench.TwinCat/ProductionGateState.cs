namespace JointBench.TwinCat;

public sealed record ProductionGateState(
    bool StationReady,
    bool EnvironmentOk,
    bool Ti5Ready,
    bool AdsOk)
{
    public static ProductionGateState Locked { get; } = new(
        StationReady: false,
        EnvironmentOk: false,
        Ti5Ready: false,
        AdsOk: false);

    public bool ReadyForMotion => StationReady && EnvironmentOk && Ti5Ready && AdsOk;

    public static ProductionGateState FromReadiness(StationReadinessReport report)
    {
        var ti5Ready = report.Checks.Any(check =>
            check.Name.Equals("ti5-scan", StringComparison.OrdinalIgnoreCase) &&
            !check.IsError);

        return new ProductionGateState(
            StationReady: report.Ready,
            EnvironmentOk: report.Preflight?.Ok == true,
            Ti5Ready: ti5Ready,
            AdsOk: report.AdsSymbols?.Ok == true);
    }

    public ProductionGateState WithAdsSymbolCheck(AdsSymbolCheckReport report) =>
        this with
        {
            AdsOk = report.Ok,
            StationReady = StationReady && report.Ok,
        };

    public ProductionGateState WithEngineeringScan(EtherCatScanReport _) => this;
}
