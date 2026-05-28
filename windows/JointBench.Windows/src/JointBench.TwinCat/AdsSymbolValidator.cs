using TwinCAT.Ads;

namespace JointBench.TwinCat;

public sealed class AdsSymbolValidator
{
    public static readonly IReadOnlyList<AdsSymbolSpec> RequiredSymbols =
    [
        new("bEnable", "BOOL"),
        new("bStart", "BOOL"),
        new("bStop", "BOOL"),
        new("bResetFault", "BOOL"),
        new("fTargetPositionDeg", "LREAL"),
        new("nCommandSequence", "DINT"),
        new("bReady", "BOOL"),
        new("bBusy", "BOOL"),
        new("bDone", "BOOL"),
        new("bError", "BOOL"),
        new("bOperationEnabled", "BOOL"),
        new("bWatchdogOk", "BOOL"),
        new("fActualPositionDeg", "LREAL"),
        new("fActualVelocityDps", "LREAL"),
        new("fFollowingErrorDeg", "LREAL"),
        new("fCurrentA", "LREAL"),
        new("fTemperatureC", "LREAL"),
        new("nStatusword", "DINT"),
        new("nControlword", "DINT"),
        new("nFaultCode", "DINT"),
        new("nErrorCode", "DINT"),
        new("sDeviceName", "STRING"),
        new("nVendorId", "DINT"),
        new("nProductCode", "DINT"),
        new("nRevision", "DINT"),
    ];

    public AdsSymbolCheckReport Check(AdsConnectionOptions options)
    {
        var results = new List<AdsSymbolResult>();

        try
        {
            using var client = new AdsClient();
            client.Connect(options.AmsNetId, options.Port);

            var state = client.ReadState();
            if (state.AdsState != AdsState.Run)
            {
                results.Add(new AdsSymbolResult(
                    "<ads-state>",
                    "Run",
                    false,
                    $"PLC ADS state is {state.AdsState}; download/login/start the PLC application before checking symbols."));
                return new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, false, results);
            }

            foreach (var spec in RequiredSymbols)
            {
                var symbolName = $"{options.SymbolPrefix}.{spec.Name}";
                uint handle = 0;
                try
                {
                    handle = client.CreateVariableHandle(symbolName);
                    results.Add(new AdsSymbolResult(symbolName, spec.ExpectedType, true, "symbol handle created"));
                }
                catch (Exception exc)
                {
                    results.Add(new AdsSymbolResult(symbolName, spec.ExpectedType, false, exc.Message));
                }
                finally
                {
                    if (handle != 0)
                    {
                        try
                        {
                            client.DeleteVariableHandle(handle);
                        }
                        catch
                        {
                            // Cleanup failure should not hide the symbol validation result.
                        }
                    }
                }
            }

            return new AdsSymbolCheckReport(
                options.AmsNetId,
                options.Port,
                options.SymbolPrefix,
                results.All(result => result.Ok),
                results);
        }
        catch (Exception exc)
        {
            results.Add(new AdsSymbolResult("<connection>", "ADS", false, exc.Message));
            return new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, false, results);
        }
    }
}
