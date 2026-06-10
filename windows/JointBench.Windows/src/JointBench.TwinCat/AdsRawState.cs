namespace JointBench.TwinCat;

public sealed record AdsRawStateReport(
    AdsConnectionOptions Ads,
    IReadOnlyDictionary<string, object?> Values);

public sealed class AdsRawStateProbe
{
    private readonly Func<IAdsSymbolClient> clientFactory;

    public AdsRawStateProbe()
        : this(() => new BeckhoffAdsSymbolClient())
    {
    }

    public AdsRawStateProbe(Func<IAdsSymbolClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public AdsRawStateReport Read(AdsConnectionOptions options)
    {
        using var client = clientFactory();
        client.ConnectAsync(options, CancellationToken.None).GetAwaiter().GetResult();
        var root = PlcRoot(options.SymbolPrefix);
        var symbols = new (string Name, Type Type)[]
        {
            ($"{root}.nTi5ActualPosition", typeof(int)),
            ($"{root}.nTi5TargetPosition", typeof(int)),
            ($"{root}.nTi5TargetVelocity", typeof(int)),
            ($"{root}.nTi5ActualTorqueOrCurrent", typeof(int)),
            ($"{root}.nTi5ModeOfOperationDisplay", typeof(int)),
            ($"{root}.fTi5CurrentScaleAPerUnit", typeof(double)),
            ($"{root}.fTi5MaxCurrentA", typeof(double)),
            ($"{root}.nTi5Controlword", typeof(int)),
            ($"{root}.nTi5ModeOfOperation", typeof(int)),
            ($"{root}.nTi5Statusword", typeof(int)),
            ($"{options.SymbolPrefix}.fTargetPositionDeg", typeof(double)),
            ($"{options.SymbolPrefix}.fActualPositionDeg", typeof(double)),
            ($"{options.SymbolPrefix}.fFollowingErrorDeg", typeof(double)),
            ($"{options.SymbolPrefix}.nControlword", typeof(int)),
            ($"{options.SymbolPrefix}.nStatusword", typeof(int)),
            ($"{options.SymbolPrefix}.bOperationEnabled", typeof(bool)),
            ($"{options.SymbolPrefix}.bWatchdogOk", typeof(bool)),
            ($"{options.SymbolPrefix}.bReady", typeof(bool)),
            ($"{options.SymbolPrefix}.bBusy", typeof(bool)),
            ($"{options.SymbolPrefix}.bDone", typeof(bool)),
            ($"{options.SymbolPrefix}.bError", typeof(bool)),
            ($"{options.SymbolPrefix}.nErrorCode", typeof(int)),
        };

        var values = new Dictionary<string, object?>();
        foreach (var symbol in symbols)
        {
            values[symbol.Name] = client.ReadAsync(symbol.Name, symbol.Type, CancellationToken.None).GetAwaiter().GetResult();
        }

        return new AdsRawStateReport(options, values);
    }

    private static string PlcRoot(string symbolPrefix)
    {
        var dot = symbolPrefix.IndexOf('.');
        return dot > 0 ? symbolPrefix[..dot] : "MAIN";
    }
}
