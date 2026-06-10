namespace JointBench.TwinCat;

public sealed record HardStoneStateSnapshot(
    bool Ok,
    string Message,
    int Ti5SlaveIndex,
    int EtherCatOperational,
    int VendorId,
    int ProductCode,
    int RevisionNumber,
    int Statusword,
    int Controlword,
    int CommandCode,
    int CommandSequence,
    int CommandAck,
    int HeartbeatSequence,
    int HeartbeatAck,
    int TargetRelativeCounts,
    int CommandError,
    bool Enabled,
    bool WatchdogOk,
    int ZeroPositionCounts,
    int ActualPositionCounts,
    int TargetPositionCounts,
    int ActualVelocityCounts,
    int TorqueActual,
    int ModeOfOperationCommand,
    int ModeOfOperationDisplay,
    double ActualPositionDegrees,
    double TargetPositionDegrees,
    double FollowingErrorDegrees);

public sealed class HardStoneStateProbe
{
    private readonly Func<int, IHardStoneDebugTransport> transportFactory;

    public HardStoneStateProbe()
        : this(speed => new HardStoneOpenOcdTransport(speed))
    {
    }

    public HardStoneStateProbe(Func<int, IHardStoneDebugTransport> transportFactory)
    {
        this.transportFactory = transportFactory;
    }

    public HardStoneStateSnapshot Read(StationConfig config, bool fake = false)
    {
        if (!string.Equals(config.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("HardStone state can only be read for protocol hardstone_swd.");
        }

        var hardStone = config.HardStone ?? throw new InvalidOperationException("HardStone station options are missing.");
        using var transport = fake ? new FakeHardStoneDebugTransport() : transportFactory(hardStone.AdapterSpeedKHz);
        transport.ConnectAsync(
            new HardStoneDebugOptions(
                hardStone.FirmwareElfPath,
                hardStone.CountsPerDegree,
                Math.Max(Math.Abs(config.Safety.MinPositionDegrees), Math.Abs(config.Safety.MaxPositionDegrees))),
            CancellationToken.None).GetAwaiter().GetResult();

        var ti5Index = Read(transport, "g_ec_ti5_slave_index");
        var operational = Read(transport, "g_ec_operational");
        var vendor = Read(transport, "g_ec_last_vendor");
        var product = Read(transport, "g_ec_last_product");
        var revision = Read(transport, "g_ec_last_revision");
        var statusword = Read(transport, "g_host_statusword");
        var controlword = Read(transport, "g_host_controlword");
        var commandCode = Read(transport, "g_host_command_code");
        var commandSequence = Read(transport, "g_host_command_sequence");
        var commandAck = Read(transport, "g_host_command_ack");
        var heartbeatSequence = Read(transport, "g_host_heartbeat_sequence");
        var heartbeatAck = Read(transport, "g_host_heartbeat_ack");
        var targetRelativeCounts = Read(transport, "g_host_target_relative_counts");
        var commandError = Read(transport, "g_host_command_error");
        var enabled = Read(transport, "g_host_enabled") != 0;
        var watchdog = Read(transport, "g_host_watchdog_ok") != 0;
        var zeroCounts = Read(transport, "g_host_zero_position_counts");
        var actualCounts = Read(transport, "g_host_actual_position_counts");
        var targetCounts = Read(transport, "g_host_target_position_counts");
        var velocityCounts = Read(transport, "g_host_actual_velocity_counts");
        var torque = Read(transport, "g_host_torque_actual");
        var modeCommand = Read(transport, "g_host_mode_of_operation");
        var modeDisplay = Read(transport, "g_host_mode_display");
        var actualDegrees = actualCounts / hardStone.CountsPerDegree;
        var targetDegrees = targetCounts / hardStone.CountsPerDegree;
        var ok = ti5Index > 0 && operational != 0 && commandError == 0;

        return new HardStoneStateSnapshot(
            ok,
            ok ? "HardStone Ti5 state is readable." : "HardStone Ti5 state reports an issue.",
            ti5Index,
            operational,
            vendor,
            product,
            revision,
            statusword,
            controlword,
            commandCode,
            commandSequence,
            commandAck,
            heartbeatSequence,
            heartbeatAck,
            targetRelativeCounts,
            commandError,
            enabled,
            watchdog,
            zeroCounts,
            actualCounts,
            targetCounts,
            velocityCounts,
            torque,
            modeCommand,
            modeDisplay,
            actualDegrees,
            targetDegrees,
            targetDegrees - actualDegrees);
    }

    private static int Read(IHardStoneDebugTransport transport, string symbol) =>
        transport.ReadInt32Async(symbol, CancellationToken.None).GetAwaiter().GetResult();
}
