using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace JointBench.TwinCat;

public static class HardStoneHostCommands
{
    public const int None = 0;
    public const int Enable = 1;
    public const int MoveRelative = 2;
    public const int Stop = 3;
    public const int ResetFault = 4;
}

public sealed record HardStoneDebugOptions(
    string FirmwareElfPath,
    double CountsPerDegree,
    double MaxTargetAbsDegrees = 720.0,
    TimeSpan? EnableTimeout = null,
    TimeSpan? EnablePollInterval = null)
{
    public TimeSpan EffectiveEnableTimeout => EnableTimeout ?? TimeSpan.FromSeconds(8);

    public TimeSpan EffectiveEnablePollInterval => EnablePollInterval ?? TimeSpan.FromMilliseconds(20);
}

public sealed record HardStoneDebugWrite(string Symbol, int Value);

public interface IHardStoneDebugTransport : IDisposable
{
    Task ConnectAsync(HardStoneDebugOptions options, CancellationToken cancellationToken);

    Task<int> ReadInt32Async(string symbol, CancellationToken cancellationToken);

    Task WriteInt32Async(string symbol, int value, CancellationToken cancellationToken);
}

public sealed class FakeHardStoneDebugTransport : IHardStoneDebugTransport
{
    private readonly Dictionary<string, int> values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g_host_statusword"] = 0x0021,
        ["g_host_controlword"] = 0x0006,
        ["g_host_command_ack"] = 0,
        ["g_host_heartbeat_ack"] = 0,
        ["g_host_target_relative_counts"] = 0,
        ["g_host_command_error"] = 0,
        ["g_host_enabled"] = 0,
        ["g_host_watchdog_ok"] = 1,
        ["g_host_actual_position_counts"] = 0,
        ["g_host_target_position_counts"] = 0,
        ["g_host_actual_velocity_counts"] = 0,
        ["g_host_torque_actual"] = 0,
        ["g_host_mode_of_operation"] = 1,
        ["g_host_mode_display"] = 1,
        ["g_ec_last_vendor"] = 0x00522227,
        ["g_ec_last_product"] = 0x00009253,
        ["g_ec_last_revision"] = 0x00010005,
        ["g_ec_operational"] = 1,
        ["g_ec_ti5_slave_index"] = 1,
    };

    private int commandSequence;
    private int targetRelativeCounts;

    public List<HardStoneDebugWrite> Writes { get; } = [];

    public Task ConnectAsync(HardStoneDebugOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<int> ReadInt32Async(string symbol, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        values.TryGetValue(symbol, out var value);
        return Task.FromResult(value);
    }

    public Task WriteInt32Async(string symbol, int value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add(new HardStoneDebugWrite(symbol, value));
        values[symbol] = value;

        if (string.Equals(symbol, "g_host_command_sequence", StringComparison.OrdinalIgnoreCase))
        {
            commandSequence = value;
            values["g_host_command_ack"] = commandSequence;
            ApplyCommand();
        }
        else if (string.Equals(symbol, "g_host_heartbeat_sequence", StringComparison.OrdinalIgnoreCase))
        {
            values["g_host_heartbeat_ack"] = value;
        }
        else if (string.Equals(symbol, "g_host_target_relative_counts", StringComparison.OrdinalIgnoreCase))
        {
            targetRelativeCounts = value;
        }

        return Task.CompletedTask;
    }

    public void Cycle(double dtSeconds)
    {
        var actual = values["g_host_actual_position_counts"];
        var target = values["g_host_target_position_counts"];
        var maxStep = Math.Max(1, (int)Math.Round(dtSeconds * 20_000.0));
        var delta = target - actual;
        actual += Math.Abs(delta) <= maxStep ? delta : Math.Sign(delta) * maxStep;
        values["g_host_actual_velocity_counts"] = Math.Abs(dtSeconds) < double.Epsilon
            ? 0
            : (int)Math.Round((actual - values["g_host_actual_position_counts"]) / dtSeconds);
        values["g_host_actual_position_counts"] = actual;
        values["g_host_statusword"] = values["g_host_enabled"] == 1 ? 0x0027 : 0x0021;
        values["g_host_controlword"] = values["g_host_enabled"] == 1 ? 0x000F : 0x0006;
    }

    public void Dispose()
    {
    }

    private void ApplyCommand()
    {
        var command = values.TryGetValue("g_host_command_code", out var code) ? code : HardStoneHostCommands.None;
        if (command == HardStoneHostCommands.Enable)
        {
            values["g_host_enabled"] = 1;
            values["g_host_statusword"] = 0x0027;
            values["g_host_controlword"] = 0x000F;
            values["g_host_zero_position_counts"] = values["g_host_actual_position_counts"];
            values["g_host_target_position_counts"] = values["g_host_actual_position_counts"];
        }
        else if (command == HardStoneHostCommands.MoveRelative)
        {
            values["g_host_target_position_counts"] =
                values.GetValueOrDefault("g_host_zero_position_counts") + targetRelativeCounts;
        }
        else if (command == HardStoneHostCommands.Stop)
        {
            values["g_host_enabled"] = 0;
            values["g_host_controlword"] = 0x0002;
        }
        else if (command == HardStoneHostCommands.ResetFault)
        {
            values["g_host_command_error"] = 0;
        }
    }
}

public sealed class HardStoneDebugMotionAdapter : IMotionAdapter
{
    private readonly IHardStoneDebugTransport transport;
    private readonly HardStoneDebugOptions options;
    private int commandSequence;
    private int heartbeatSequence;
    private double targetPositionDegrees;

    public HardStoneDebugMotionAdapter(IHardStoneDebugTransport transport, HardStoneDebugOptions options)
    {
        this.transport = transport;
        this.options = options;
        if (options.CountsPerDegree <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CountsPerDegree must be positive.");
        }
    }

    public bool IsSimulation => transport is FakeHardStoneDebugTransport;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await transport.ConnectAsync(options, cancellationToken);
        await WriteCommandAsync(HardStoneHostCommands.ResetFault, cancellationToken);
    }

    public async Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken)
    {
        var vendor = await transport.ReadInt32Async("g_ec_last_vendor", cancellationToken);
        var product = await transport.ReadInt32Async("g_ec_last_product", cancellationToken);
        var revision = await transport.ReadInt32Async("g_ec_last_revision", cancellationToken);
        return new DeviceInfo(
            "Ti5 Harmonic Joint",
            "YS-F4Pro",
            "HardStone YS-F4Pro",
            "hardstone_swd",
            "OpenOCD SWD mailbox",
            string.Empty,
            0,
            "g_host_*",
            vendor,
            product,
            revision,
            "connected");
    }

    public Task<AdsRuntimeConfigurationReport> ApplyRuntimeConfigAsync(
        SafetyLimits safety,
        StationScaling scaling,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AdsRuntimeConfigurationReport.Applied(
            $"hardstone mailbox ready; counts_per_degree={options.CountsPerDegree:F3}"));
    }

    public async Task SetEnableAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            await EmergencyStopAsync(cancellationToken);
            return;
        }

        await WriteCommandAsync(HardStoneHostCommands.Enable, cancellationToken);
        var deadline = DateTimeOffset.UtcNow + options.EffectiveEnableTimeout;
        ActuatorState? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await SampleAsync(0.0, 0.0, cancellationToken);
            if (last.Enabled)
            {
                return;
            }

            if (!IsSimulation)
            {
                await Task.Delay(options.EffectiveEnablePollInterval, cancellationToken);
            }
        }

        throw new TimeoutException($"Timed out waiting for HardStone Ti5 enable. Last state: {FormatState(last)}");
    }

    public async Task SendPositionCommandAsync(double positionDegrees, CancellationToken cancellationToken)
    {
        if (Math.Abs(positionDegrees) > options.MaxTargetAbsDegrees)
        {
            throw new SafetyLimitException($"HardStone target {positionDegrees:F3}deg exceeds configured +/-{options.MaxTargetAbsDegrees:F3}deg motion limit.");
        }

        targetPositionDegrees = positionDegrees;
        var counts = checked((int)Math.Round(positionDegrees * options.CountsPerDegree));
        await transport.WriteInt32Async("g_host_target_relative_counts", counts, cancellationToken);
        await WriteCommandAsync(HardStoneHostCommands.MoveRelative, cancellationToken);
    }

    public async Task<ActuatorState> SampleAsync(double dtSeconds, double timestampSeconds, CancellationToken cancellationToken)
    {
        await transport.WriteInt32Async("g_host_heartbeat_sequence", ++heartbeatSequence, cancellationToken);
        if (transport is FakeHardStoneDebugTransport fake)
        {
            fake.Cycle(dtSeconds);
        }

        var actualCounts = await transport.ReadInt32Async("g_host_actual_position_counts", cancellationToken);
        var targetCounts = await transport.ReadInt32Async("g_host_target_position_counts", cancellationToken);
        var velocityCounts = await transport.ReadInt32Async("g_host_actual_velocity_counts", cancellationToken);
        var enabled = await transport.ReadInt32Async("g_host_enabled", cancellationToken) != 0;
        var statusword = await transport.ReadInt32Async("g_host_statusword", cancellationToken);
        var controlword = await transport.ReadInt32Async("g_host_controlword", cancellationToken);
        var commandAck = await transport.ReadInt32Async("g_host_command_ack", cancellationToken);
        var heartbeatAck = await transport.ReadInt32Async("g_host_heartbeat_ack", cancellationToken);
        var targetRelativeCounts = await transport.ReadInt32Async("g_host_target_relative_counts", cancellationToken);
        var error = await transport.ReadInt32Async("g_host_command_error", cancellationToken);
        var watchdog = await transport.ReadInt32Async("g_host_watchdog_ok", cancellationToken) != 0;
        var torque = await transport.ReadInt32Async("g_host_torque_actual", cancellationToken);
        var modeCommand = await transport.ReadInt32Async("g_host_mode_of_operation", cancellationToken);
        var modeDisplay = await transport.ReadInt32Async("g_host_mode_display", cancellationToken);
        var actualDegrees = actualCounts / options.CountsPerDegree;
        var targetDegrees = targetCounts / options.CountsPerDegree;

        return new ActuatorState(
            timestampSeconds,
            targetPositionDegrees,
            actualDegrees,
            velocityCounts / options.CountsPerDegree,
            torque,
            24.0,
            0.0,
            error,
            enabled,
            Protocol: "hardstone_swd",
            Statusword: statusword,
            Controlword: controlword,
            CommandSequence: commandSequence,
            WatchdogOk: watchdog,
            FollowingErrorDegrees: targetDegrees - actualDegrees,
            DebugCommandAck: commandAck,
            DebugHeartbeatAck: heartbeatAck,
            DebugTargetRelativeCounts: targetRelativeCounts,
            DebugTargetCounts: targetCounts,
            DebugActualCounts: actualCounts,
            ModeOfOperationCommand: modeCommand,
            ModeOfOperationDisplay: modeDisplay);
    }

    public Task EmergencyStopAsync(CancellationToken cancellationToken) =>
        WriteCommandAsync(HardStoneHostCommands.Stop, cancellationToken);

    public void Dispose() => transport.Dispose();

    private async Task WriteCommandAsync(int command, CancellationToken cancellationToken)
    {
        await transport.WriteInt32Async("g_host_command_code", command, cancellationToken);
        await transport.WriteInt32Async("g_host_command_sequence", ++commandSequence, cancellationToken);
    }

    private static string FormatState(ActuatorState? state)
    {
        if (state is null)
        {
            return "unavailable";
        }

        return $"statusword=0x{state.Statusword ?? 0:X4}, controlword=0x{state.Controlword ?? 0:X4}, error={state.FaultCode}, enabled={state.Enabled}, position={state.ActualPositionDegrees:F3}deg, mode_command={FormatNullable(state.ModeOfOperationCommand)}, mode_display={FormatNullable(state.ModeOfOperationDisplay)}, diagnosis={CiA402StateDiagnosis.Describe(state)}";
    }

    private static string FormatNullable(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
}

public sealed class HardStoneOpenOcdTransport : IHardStoneDebugTransport
{
    private static readonly string[] RequiredSymbols =
    [
        "g_host_command_sequence",
        "g_host_command_ack",
        "g_host_heartbeat_sequence",
        "g_host_heartbeat_ack",
        "g_host_command_code",
        "g_host_target_relative_counts",
        "g_host_command_error",
        "g_host_statusword",
        "g_host_controlword",
        "g_host_actual_position_counts",
        "g_host_target_position_counts",
        "g_host_actual_velocity_counts",
        "g_host_torque_actual",
        "g_host_mode_of_operation",
        "g_host_mode_display",
        "g_host_enabled",
        "g_host_watchdog_ok",
        "g_host_zero_position_counts",
        "g_ec_last_vendor",
        "g_ec_last_product",
        "g_ec_last_revision",
        "g_ec_operational",
        "g_ec_ti5_slave_index",
    ];

    private readonly int adapterSpeedKhz;
    private readonly int telnetPort;
    private readonly Dictionary<string, uint> symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim ioLock = new(1, 1);
    private HardStoneDebugSessionLock? sessionLock;
    private Process? process;
    private TcpClient? client;
    private NetworkStream? stream;

    public HardStoneOpenOcdTransport(int adapterSpeedKhz = 1000, int telnetPort = 4444)
    {
        this.adapterSpeedKhz = adapterSpeedKhz;
        this.telnetPort = telnetPort;
    }

    public async Task ConnectAsync(HardStoneDebugOptions options, CancellationToken cancellationToken)
    {
        sessionLock = HardStoneDebugSessionLock.Acquire(TimeSpan.FromSeconds(10));
        try
        {
            ResolveSymbols(options.FirmwareElfPath);
            process = StartOpenOcd();
            client = await ConnectTelnetAsync(cancellationToken);
            stream = client.GetStream();
            await ReadUntilPromptAsync(cancellationToken);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async Task<int> ReadInt32Async(string symbol, CancellationToken cancellationToken)
    {
        var address = SymbolAddress(symbol);
        var response = await SendCommandAsync($"mdw 0x{address:x8} 1", cancellationToken);
        var match = Regex.Match(response, @"0x[0-9a-fA-F]+:\s+([0-9a-fA-F]+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"OpenOCD did not return a value for {symbol}: {response.Trim()}");
        }

        return unchecked((int)uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public Task WriteInt32Async(string symbol, int value, CancellationToken cancellationToken)
    {
        var address = SymbolAddress(symbol);
        return SendCommandAsync($"mww 0x{address:x8} 0x{unchecked((uint)value):x8}", cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            if (stream is not null)
            {
                var bytes = Encoding.ASCII.GetBytes("shutdown\n");
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
        }

        try
        {
            client?.Dispose();
            if (process is { HasExited: false })
            {
                process.WaitForExit(1500);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
        }

        try
        {
            sessionLock?.Dispose();
            sessionLock = null;
        }
        catch
        {
        }

        process?.Dispose();
        ioLock.Dispose();
    }

    private void ResolveSymbols(string elfPath)
    {
        if (!File.Exists(elfPath))
        {
            throw new FileNotFoundException("HardStone firmware ELF was not found.", elfPath);
        }

        var start = new ProcessStartInfo(FindTool("arm-none-eabi-nm.exe", "arm-none-eabi-nm"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-a");
        start.ArgumentList.Add("-n");
        start.ArgumentList.Add(elfPath);
        using var nm = Process.Start(start) ?? throw new InvalidOperationException("Failed to start arm-none-eabi-nm.");
        var output = nm.StandardOutput.ReadToEnd();
        var error = nm.StandardError.ReadToEnd();
        nm.WaitForExit();
        if (nm.ExitCode != 0)
        {
            throw new InvalidOperationException($"arm-none-eabi-nm failed: {error}");
        }

        symbols.Clear();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = Regex.Split(line.Trim(), @"\s+");
            if (parts.Length >= 3 && RequiredSymbols.Contains(parts[2], StringComparer.OrdinalIgnoreCase))
            {
                symbols[parts[2]] = uint.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
        }

        var missing = RequiredSymbols.Where(symbol => !symbols.ContainsKey(symbol)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"HardStone firmware ELF is missing host mailbox symbols. Rebuild and flash the latest ti5-safe firmware. Missing: {string.Join(", ", missing)}");
        }
    }

    private Process StartOpenOcd()
    {
        var start = new ProcessStartInfo(FindTool("openocd.exe", "openocd"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("interface/cmsis-dap.cfg");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("target/stm32f4x.cfg");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"adapter speed {adapterSpeedKhz}");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"telnet_port {telnetPort}");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("init");
        var openOcd = Process.Start(start) ?? throw new InvalidOperationException("Failed to start OpenOCD.");
        openOcd.BeginOutputReadLine();
        openOcd.BeginErrorReadLine();
        return openOcd;
    }

    private async Task<TcpClient> ConnectTelnetAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
        Exception? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", telnetPort, cancellationToken);
                return tcp;
            }
            catch (Exception exc)
            {
                last = exc;
                await Task.Delay(100, cancellationToken);
            }

            if (process is { HasExited: true })
            {
                throw new InvalidOperationException("OpenOCD exited before the telnet port became ready.");
            }
        }

        throw new TimeoutException($"Timed out connecting to OpenOCD telnet port {telnetPort}. {last?.Message}");
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken) =>
        await SendCommandWithResponseAsync(command, cancellationToken);

    private async Task<string> SendCommandWithResponseAsync(string command, CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            throw new InvalidOperationException("OpenOCD is not connected.");
        }

        await ioLock.WaitAsync(cancellationToken);
        try
        {
            var bytes = Encoding.ASCII.GetBytes(command + "\n");
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return await ReadUntilPromptAsync(cancellationToken);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private async Task<string> ReadUntilPromptAsync(CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            throw new InvalidOperationException("OpenOCD is not connected.");
        }

        var buffer = new byte[1024];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                throw new InvalidOperationException("OpenOCD telnet connection closed.");
            }

            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            var text = builder.ToString();
            if (text.EndsWith("> ", StringComparison.Ordinal) || text.EndsWith(">", StringComparison.Ordinal))
            {
                return text;
            }
        }
    }

    private uint SymbolAddress(string symbol) =>
        symbols.TryGetValue(symbol, out var address)
            ? address
            : throw new KeyNotFoundException($"Unknown HardStone mailbox symbol: {symbol}");

    private static string FindTool(params string[] names)
    {
        var pathValues = new[]
            {
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in pathValues)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim(), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var knownRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xPacks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "STMicroelectronics"),
        };
        foreach (var root in knownRoots.Where(Directory.Exists))
        {
            foreach (var name in names)
            {
                var match = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return names[0];
    }
}
