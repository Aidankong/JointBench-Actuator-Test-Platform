using TwinCAT.Ads;

namespace JointBench.TwinCat;

public sealed record AdsWriteRecord(string Symbol, object Value);

public interface IAdsSymbolClient : IDisposable
{
    Task ConnectAsync(AdsConnectionOptions options, CancellationToken cancellationToken);

    Task<object?> ReadAsync(string symbol, Type type, CancellationToken cancellationToken);

    Task WriteAsync(string symbol, object value, CancellationToken cancellationToken);
}

public sealed class BeckhoffAdsSymbolClient : IAdsSymbolClient
{
    private readonly AdsClient client = new();

    public Task ConnectAsync(AdsConnectionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Connect(options.AmsNetId, options.Port);
        return Task.CompletedTask;
    }

    public Task<object?> ReadAsync(string symbol, Type type, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object?>(client.ReadValue(symbol, type));
    }

    public Task WriteAsync(string symbol, object value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.WriteValue(symbol, value);
        return Task.CompletedTask;
    }

    public void Dispose() => client.Dispose();
}

public sealed class FakeAdsSymbolClient : IAdsSymbolClient
{
    private readonly Dictionary<string, object> symbols = new(StringComparer.OrdinalIgnoreCase);
    private double targetPositionDegrees;
    private double actualPositionDegrees;

    public FakeAdsSymbolClient()
    {
        SetDefaults();
    }

    public List<AdsWriteRecord> Writes { get; } = [];

    public double? ForceActualPositionDegrees { get; set; }

    public bool AutoOperationEnabledOnEnable { get; set; } = true;

    public Task ConnectAsync(AdsConnectionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<object?> ReadAsync(string symbol, Type type, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = symbols.TryGetValue(symbol, out var exact) ? exact : ValueBySuffix(symbol);
        return Task.FromResult(ConvertValue(value, type));
    }

    public Task WriteAsync(string symbol, object value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add(new AdsWriteRecord(symbol, value));
        symbols[symbol] = value;

        if (symbol.EndsWith(".fTargetPositionDeg", StringComparison.OrdinalIgnoreCase))
        {
            targetPositionDegrees = Convert.ToDouble(value);
        }
        else if (symbol.EndsWith(".bEnable", StringComparison.OrdinalIgnoreCase))
        {
            if (AutoOperationEnabledOnEnable)
            {
                symbols[ReplaceSuffix(symbol, "bOperationEnabled")] = Convert.ToBoolean(value);
                symbols[ReplaceSuffix(symbol, "bReady")] = Convert.ToBoolean(value);
            }
        }
        else if (symbol.EndsWith(".nCommandSequence", StringComparison.OrdinalIgnoreCase))
        {
            symbols[symbol] = Convert.ToInt32(value);
        }

        return Task.CompletedTask;
    }

    public void Cycle(double dtSeconds)
    {
        var desired = ForceActualPositionDegrees ?? targetPositionDegrees;
        var delta = desired - actualPositionDegrees;
        var maxStep = Math.Max(0.02, dtSeconds * 20.0);
        actualPositionDegrees += Math.Abs(delta) <= maxStep ? delta : Math.Sign(delta) * maxStep;
    }

    public void Dispose()
    {
    }

    private void SetDefaults()
    {
        symbols["MAIN.stJointBench.sDeviceName"] = "Ti5 Harmonic Joint";
        symbols["MAIN.stJointBench.nVendorId"] = 0x00522227;
        symbols["MAIN.stJointBench.nProductCode"] = 0x00009253;
        symbols["MAIN.stJointBench.nRevision"] = 0x00010005;
        symbols["MAIN.stJointBench.bOperationEnabled"] = false;
        symbols["MAIN.stJointBench.bWatchdogOk"] = true;
        symbols["MAIN.stJointBench.bError"] = false;
        symbols["MAIN.stJointBench.nFaultCode"] = 0;
        symbols["MAIN.stJointBench.nErrorCode"] = 0;
        symbols["MAIN.stJointBench.nStatusword"] = 0x0027;
        symbols["MAIN.stJointBench.nControlword"] = 0x000F;
        symbols["MAIN.stJointBench.fCurrentA"] = 0.5;
        symbols["MAIN.stJointBench.fTemperatureC"] = 30.0;
    }

    private object? ValueBySuffix(string symbol)
    {
        if (symbol.EndsWith(".fActualPositionDeg", StringComparison.OrdinalIgnoreCase))
        {
            return ForceActualPositionDegrees ?? actualPositionDegrees;
        }

        if (symbol.EndsWith(".fActualVelocityDps", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }

        if (symbol.EndsWith(".fFollowingErrorDeg", StringComparison.OrdinalIgnoreCase))
        {
            return targetPositionDegrees - (ForceActualPositionDegrees ?? actualPositionDegrees);
        }

        var suffixDefaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [".bWatchdogOk"] = true,
            [".bOperationEnabled"] = false,
            [".fCurrentA"] = 0.5,
            [".fTemperatureC"] = 30.0,
            [".nStatusword"] = 0x0027,
            [".nControlword"] = 0x000F,
            [".nFaultCode"] = 0,
            [".nErrorCode"] = 0,
        };
        return suffixDefaults.FirstOrDefault(item => symbol.EndsWith(item.Key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static object? ConvertValue(object? value, Type type)
    {
        if (value is null)
        {
            return type == typeof(string) ? string.Empty : Activator.CreateInstance(type);
        }

        if (type == typeof(bool))
        {
            return Convert.ToBoolean(value);
        }

        if (type == typeof(int))
        {
            return Convert.ToInt32(value);
        }

        if (type == typeof(double))
        {
            return Convert.ToDouble(value);
        }

        return value.ToString();
    }

    private static string ReplaceSuffix(string symbol, string suffix)
    {
        var dot = symbol.LastIndexOf('.');
        return dot < 0 ? suffix : symbol[..(dot + 1)] + suffix;
    }
}

public sealed class AdsMotionAdapter
{
    private readonly IAdsSymbolClient client;
    private readonly AdsConnectionOptions options;
    private readonly TimeSpan enableTimeout;
    private readonly TimeSpan enablePollInterval;
    private readonly TimeSpan startPulseDuration;
    private int commandSequence;
    private double targetPositionDegrees;

    public AdsMotionAdapter(
        IAdsSymbolClient client,
        AdsConnectionOptions options,
        TimeSpan? enableTimeout = null,
        TimeSpan? enablePollInterval = null,
        TimeSpan? startPulseDuration = null)
    {
        this.client = client;
        this.options = options;
        this.enableTimeout = enableTimeout ?? TimeSpan.FromSeconds(8);
        this.enablePollInterval = enablePollInterval ?? TimeSpan.FromMilliseconds(20);
        this.startPulseDuration = startPulseDuration ?? TimeSpan.FromMilliseconds(30);
    }

    public bool OperationEnabled { get; private set; }

    public bool IsSimulation => client is FakeAdsSymbolClient;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await client.ConnectAsync(options, cancellationToken);
        await BumpCommandSequenceAsync(cancellationToken);
    }

    public async Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken)
    {
        return new DeviceInfo(
            Convert.ToString(await ReadAsync("sDeviceName", typeof(string), cancellationToken)) ?? "Ti5 Harmonic Joint",
            "TwinCAT-ADS",
            "TwinCAT ADS",
            "twincat_ads",
            "TwinCAT ADS",
            options.AmsNetId,
            options.Port,
            options.SymbolPrefix,
            Convert.ToInt32(await ReadAsync("nVendorId", typeof(int), cancellationToken)),
            Convert.ToInt32(await ReadAsync("nProductCode", typeof(int), cancellationToken)),
            Convert.ToInt32(await ReadAsync("nRevision", typeof(int), cancellationToken)),
            "connected");
    }

    public async Task<AdsRuntimeConfigurationReport> ApplyRuntimeConfigAsync(
        SafetyLimits safety,
        StationScaling scaling,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = PlcRoot(options.SymbolPrefix);
            await client.WriteAsync($"{root}.fTi5MinPositionDeg", safety.MinPositionDegrees, cancellationToken);
            await client.WriteAsync($"{root}.fTi5MaxPositionDeg", safety.MaxPositionDegrees, cancellationToken);
            await client.WriteAsync($"{root}.fTi5MaxVelocityDps", safety.MaxSpeedDps, cancellationToken);
            await client.WriteAsync($"{root}.fTi5MaxCurrentA", safety.MaxCurrentA, cancellationToken);
            await client.WriteAsync($"{root}.fTi5MaxTemperatureC", safety.MaxTemperatureC, cancellationToken);
            await client.WriteAsync($"{root}.fTi5MaxFollowingErrorDeg", safety.MaxFollowingErrorDegrees, cancellationToken);
            await client.WriteAsync($"{root}.nTi5EncoderCountsPerRev", scaling.EncoderCountsPerRev, cancellationToken);
            await client.WriteAsync($"{root}.fTi5GearRatio", scaling.GearRatio, cancellationToken);
            await client.WriteAsync($"{root}.nTi5PositionDirection", scaling.PositionDirection, cancellationToken);
            await client.WriteAsync($"{root}.fTi5CurrentScaleAPerUnit", scaling.CurrentScaleAPerUnit, cancellationToken);
            await client.WriteAsync($"{root}.fTi5TemperatureScaleCPerUnit", scaling.TemperatureScaleCPerUnit, cancellationToken);

            var zeroOffset = scaling.ZeroOffsetDegrees;
            if (scaling.AutoZeroOnCheck)
            {
                zeroOffset -= await ReadRawPositionDegreesAsync(root, scaling, cancellationToken);
            }

            await client.WriteAsync($"{root}.fTi5ZeroOffsetDeg", zeroOffset, cancellationToken);
            await PulseResetFaultAsync(cancellationToken);
            var detail = scaling.AutoZeroOnCheck
                ? $"auto-zero applied; zero_offset_deg={zeroOffset:F6}"
                : $"zero_offset_deg={zeroOffset:F6}";
            return AdsRuntimeConfigurationReport.Applied(detail);
        }
        catch (Exception exc)
        {
            return AdsRuntimeConfigurationReport.Failed(FormatRuntimeConfigFailure(exc));
        }
    }

    public async Task SetEnableAsync(bool enabled, CancellationToken cancellationToken)
    {
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bStop", false, cancellationToken);
        await WriteAsync("bEnable", enabled, cancellationToken);
        if (enabled)
        {
            ActuatorState? lastState = null;
            var deadline = DateTimeOffset.UtcNow + enableTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await BumpCommandSequenceAsync(cancellationToken);
                lastState = await ReadCurrentStateAsync(0.0, cancellationToken);
                if (Convert.ToBoolean(await ReadAsync("bOperationEnabled", typeof(bool), cancellationToken)))
                {
                    OperationEnabled = true;
                    return;
                }

                await Task.Delay(enablePollInterval, cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for bOperationEnabled=True. Last state: {FormatState(lastState)}");
        }

        OperationEnabled = false;
    }

    public async Task SendPositionCommandAsync(double positionDegrees, CancellationToken cancellationToken)
    {
        if (Math.Abs(positionDegrees) > 5.0)
        {
            throw new SafetyLimitException("TwinCAT ADS V1 first motion is limited to +/-5 deg.");
        }

        targetPositionDegrees = positionDegrees;
        await WriteAsync("fTargetPositionDeg", positionDegrees, cancellationToken);
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bStart", false, cancellationToken);
        await DelayStartPulseAsync(cancellationToken);
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bStart", true, cancellationToken);
        await DelayStartPulseAsync(cancellationToken);
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bStart", false, cancellationToken);
    }

    public async Task<ActuatorState> SampleAsync(double dtSeconds, double timestampSeconds, CancellationToken cancellationToken)
    {
        await BumpCommandSequenceAsync(cancellationToken);
        if (client is FakeAdsSymbolClient fake)
        {
            fake.Cycle(dtSeconds);
        }

        return new ActuatorState(
            timestampSeconds,
            targetPositionDegrees,
            Convert.ToDouble(await ReadAsync("fActualPositionDeg", typeof(double), cancellationToken)),
            Convert.ToDouble(await ReadAsync("fActualVelocityDps", typeof(double), cancellationToken)),
            Convert.ToDouble(await ReadAsync("fCurrentA", typeof(double), cancellationToken)),
            24.0,
            Convert.ToDouble(await ReadAsync("fTemperatureC", typeof(double), cancellationToken)),
            Convert.ToInt32(await ReadAsync("nFaultCode", typeof(int), cancellationToken)) != 0
                ? Convert.ToInt32(await ReadAsync("nFaultCode", typeof(int), cancellationToken))
                : Convert.ToInt32(await ReadAsync("nErrorCode", typeof(int), cancellationToken)),
            Convert.ToBoolean(await ReadAsync("bOperationEnabled", typeof(bool), cancellationToken)),
            Statusword: Convert.ToInt32(await ReadAsync("nStatusword", typeof(int), cancellationToken)),
            Controlword: Convert.ToInt32(await ReadAsync("nControlword", typeof(int), cancellationToken)),
            CommandSequence: commandSequence,
            WatchdogOk: Convert.ToBoolean(await ReadAsync("bWatchdogOk", typeof(bool), cancellationToken)),
            FollowingErrorDegrees: Convert.ToDouble(await ReadAsync("fFollowingErrorDeg", typeof(double), cancellationToken)));
    }

    public async Task EmergencyStopAsync(CancellationToken cancellationToken)
    {
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bStop", true, cancellationToken);
        await WriteAsync("bEnable", false, cancellationToken);
        OperationEnabled = false;
    }

    private Task<object?> ReadAsync(string key, Type type, CancellationToken cancellationToken) =>
        client.ReadAsync(Symbol(key), type, cancellationToken);

    private Task WriteAsync(string key, object value, CancellationToken cancellationToken) =>
        client.WriteAsync(Symbol(key), value, cancellationToken);

    private async Task BumpCommandSequenceAsync(CancellationToken cancellationToken)
    {
        commandSequence++;
        await WriteAsync("nCommandSequence", commandSequence, cancellationToken);
    }

    private async Task PulseResetFaultAsync(CancellationToken cancellationToken)
    {
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bResetFault", true, cancellationToken);
        await Task.Delay(60, cancellationToken);
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bResetFault", false, cancellationToken);
    }

    private Task DelayStartPulseAsync(CancellationToken cancellationToken) =>
        startPulseDuration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(startPulseDuration, cancellationToken);

    private async Task<double> ReadRawPositionDegreesAsync(
        string root,
        StationScaling scaling,
        CancellationToken cancellationToken)
    {
        if (scaling.EncoderCountsPerRev <= 0 || scaling.GearRatio <= 0.0)
        {
            throw new InvalidOperationException("Station scaling is invalid; encoder_counts_per_rev and gear_ratio must be positive.");
        }

        var rawCounts = Convert.ToDouble(await client.ReadAsync($"{root}.nTi5ActualPosition", typeof(int), cancellationToken));
        var countsPerDegree = scaling.EncoderCountsPerRev * scaling.GearRatio / 360.0;
        return rawCounts / countsPerDegree * scaling.PositionDirection;
    }

    private async Task<ActuatorState> ReadCurrentStateAsync(double timestampSeconds, CancellationToken cancellationToken) =>
        new(
            timestampSeconds,
            targetPositionDegrees,
            Convert.ToDouble(await ReadAsync("fActualPositionDeg", typeof(double), cancellationToken)),
            Convert.ToDouble(await ReadAsync("fActualVelocityDps", typeof(double), cancellationToken)),
            Convert.ToDouble(await ReadAsync("fCurrentA", typeof(double), cancellationToken)),
            24.0,
            Convert.ToDouble(await ReadAsync("fTemperatureC", typeof(double), cancellationToken)),
            Convert.ToInt32(await ReadAsync("nFaultCode", typeof(int), cancellationToken)) != 0
                ? Convert.ToInt32(await ReadAsync("nFaultCode", typeof(int), cancellationToken))
                : Convert.ToInt32(await ReadAsync("nErrorCode", typeof(int), cancellationToken)),
            Convert.ToBoolean(await ReadAsync("bOperationEnabled", typeof(bool), cancellationToken)),
            Statusword: Convert.ToInt32(await ReadAsync("nStatusword", typeof(int), cancellationToken)),
            Controlword: Convert.ToInt32(await ReadAsync("nControlword", typeof(int), cancellationToken)),
            CommandSequence: commandSequence,
            WatchdogOk: Convert.ToBoolean(await ReadAsync("bWatchdogOk", typeof(bool), cancellationToken)),
            FollowingErrorDegrees: Convert.ToDouble(await ReadAsync("fFollowingErrorDeg", typeof(double), cancellationToken)));

    private static string FormatState(ActuatorState? state)
    {
        if (state is null)
        {
            return "unavailable";
        }

        return $"statusword=0x{state.Statusword ?? 0:X4}, controlword=0x{state.Controlword ?? 0:X4}, error={state.FaultCode}, watchdog={state.WatchdogOk}, enabled={state.Enabled}, position={state.ActualPositionDegrees:F3}deg";
    }

    private static string FormatRuntimeConfigFailure(Exception exc)
    {
        var message = exc.Message;
        if (message.Contains("Symbol could not be found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("0x710", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC runtime config symbols are missing. Run Prepare TwinCAT with Activate once to deploy the latest JointBench PLC template.";
        }

        return message;
    }

    private string Symbol(string key) => $"{options.SymbolPrefix}.{key}";

    private static string PlcRoot(string symbolPrefix)
    {
        var dot = symbolPrefix.IndexOf('.');
        return dot > 0 ? symbolPrefix[..dot] : "MAIN";
    }
}
