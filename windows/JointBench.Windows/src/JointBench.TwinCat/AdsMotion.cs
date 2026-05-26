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
            symbols[ReplaceSuffix(symbol, "bOperationEnabled")] = Convert.ToBoolean(value);
            symbols[ReplaceSuffix(symbol, "bReady")] = Convert.ToBoolean(value);
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
    private int commandSequence;
    private double targetPositionDegrees;

    public AdsMotionAdapter(IAdsSymbolClient client, AdsConnectionOptions options)
    {
        this.client = client;
        this.options = options;
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

    public async Task SetEnableAsync(bool enabled, CancellationToken cancellationToken)
    {
        await BumpCommandSequenceAsync(cancellationToken);
        await WriteAsync("bStop", false, cancellationToken);
        await WriteAsync("bEnable", enabled, cancellationToken);
        if (enabled)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await BumpCommandSequenceAsync(cancellationToken);
                if (Convert.ToBoolean(await ReadAsync("bOperationEnabled", typeof(bool), cancellationToken)))
                {
                    OperationEnabled = true;
                    return;
                }

                await Task.Delay(20, cancellationToken);
            }

            throw new TimeoutException("Timed out waiting for bOperationEnabled=True.");
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
        await WriteAsync("bStart", true, cancellationToken);
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

    private string Symbol(string key) => $"{options.SymbolPrefix}.{key}";
}
