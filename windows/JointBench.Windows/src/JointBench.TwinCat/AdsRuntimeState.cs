namespace JointBench.TwinCat;

public sealed record AdsRuntimeStateReport(
    AdsConnectionOptions Ads,
    bool Ok,
    string Message,
    string Detail,
    ActuatorState? State)
{
    public static AdsRuntimeStateReport Healthy(AdsConnectionOptions ads) =>
        FromState(
            ads,
            new ActuatorState(
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                24.0,
                25.0,
                Statusword: 0x0040,
                Controlword: 0,
                WatchdogOk: true,
                FollowingErrorDegrees: 0.0));

    public static AdsRuntimeStateReport FromState(AdsConnectionOptions ads, ActuatorState state)
    {
        var statusword = state.Statusword ?? 0;
        var controlword = state.Controlword ?? 0;
        var detail = $"statusword=0x{statusword:X4}, controlword=0x{controlword:X4}, error={state.FaultCode}, watchdog={state.WatchdogOk}, enabled={state.Enabled}, position={state.ActualPositionDegrees:F3}deg";

        if (statusword == 0)
        {
            return new AdsRuntimeStateReport(
                ads,
                false,
                "Ti5 PDO statusword is zero; check EtherCAT OP state, Ti5 power/STO, and PDO links.",
                detail,
                state);
        }

        if ((statusword & 0x0008) != 0)
        {
            return new AdsRuntimeStateReport(ads, false, "Ti5 statusword fault bit is set.", detail, state);
        }

        if (state.FaultCode != 0)
        {
            return new AdsRuntimeStateReport(ads, false, $"Ti5 or PLC reported error code {state.FaultCode}.", detail, state);
        }

        if (state.WatchdogOk is false)
        {
            return new AdsRuntimeStateReport(ads, false, "ADS command watchdog is not healthy.", detail, state);
        }

        return new AdsRuntimeStateReport(ads, true, "Ti5 runtime state is readable and not faulted.", detail, state);
    }

    public static AdsRuntimeStateReport FromException(AdsConnectionOptions ads, Exception exc) =>
        new(ads, false, "Failed to read Ti5 runtime state.", exc.Message, null);
}

public sealed class AdsRuntimeStateProbe
{
    private readonly Func<IAdsSymbolClient> clientFactory;

    public AdsRuntimeStateProbe()
        : this(() => new BeckhoffAdsSymbolClient())
    {
    }

    public AdsRuntimeStateProbe(Func<IAdsSymbolClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public AdsRuntimeStateReport Check(AdsConnectionOptions options)
    {
        try
        {
            using var client = clientFactory();
            client.ConnectAsync(options, CancellationToken.None).GetAwaiter().GetResult();
            var state = ReadState(client, options).GetAwaiter().GetResult();
            return AdsRuntimeStateReport.FromState(options, state);
        }
        catch (Exception exc)
        {
            return AdsRuntimeStateReport.FromException(options, exc);
        }
    }

    private static async Task<ActuatorState> ReadState(IAdsSymbolClient client, AdsConnectionOptions options)
    {
        Task<object?> ReadAsync(string key, Type type) =>
            client.ReadAsync($"{options.SymbolPrefix}.{key}", type, CancellationToken.None);

        var faultCode = Convert.ToInt32(await ReadAsync("nFaultCode", typeof(int)));
        var errorCode = Convert.ToInt32(await ReadAsync("nErrorCode", typeof(int)));
        return new ActuatorState(
            0.0,
            Convert.ToDouble(await ReadAsync("fTargetPositionDeg", typeof(double))),
            Convert.ToDouble(await ReadAsync("fActualPositionDeg", typeof(double))),
            Convert.ToDouble(await ReadAsync("fActualVelocityDps", typeof(double))),
            Convert.ToDouble(await ReadAsync("fCurrentA", typeof(double))),
            24.0,
            Convert.ToDouble(await ReadAsync("fTemperatureC", typeof(double))),
            faultCode != 0 ? faultCode : errorCode,
            Convert.ToBoolean(await ReadAsync("bOperationEnabled", typeof(bool))),
            Statusword: Convert.ToInt32(await ReadAsync("nStatusword", typeof(int))),
            Controlword: Convert.ToInt32(await ReadAsync("nControlword", typeof(int))),
            WatchdogOk: Convert.ToBoolean(await ReadAsync("bWatchdogOk", typeof(bool))),
            FollowingErrorDegrees: Convert.ToDouble(await ReadAsync("fFollowingErrorDeg", typeof(double))));
    }
}
