using JointBench.TwinCat;

namespace JointBench.TwinCatHelper.Tests;

public sealed class HelperAppTests
{
    [Fact]
    public void HelpCommandReturnsSuccess()
    {
        var output = new BufferOutput();
        var exitCode = new HelperApp(output).Run(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("check-prereqs", output.Text);
        Assert.Contains("check-station-ready", output.Text);
        Assert.Contains("prepare-twincat", output.Text);
        Assert.Contains("project-spike", output.Text);
        Assert.Contains("run-sequence", output.Text);
        Assert.Contains("--profile full|1deg", output.Text);
        Assert.Contains("read-hardstone-state", output.Text);
    }

    [Fact]
    public void ReadHardStoneStateCommandCanUseFakeTransport()
    {
        var station = CreateHardStoneStation();
        var output = new BufferOutput();

        var exitCode = new HelperApp(output).Run(["read-hardstone-state", "--station", station, "--fake"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("HardStone state: OK", output.Text);
        Assert.Contains("actual_counts=0", output.Text);
        Assert.Contains("target_counts=0", output.Text);
        Assert.Contains("vendor=0x00522227", output.Text);
    }

    [Fact]
    public void ReadHardStoneStateCommandPrintsServoEnableDiagnosis()
    {
        var station = CreateHardStoneStation();
        var output = new BufferOutput();
        var app = new HelperApp(
            output,
            new EsiService(),
            new SystemProbe(),
            new AdsSymbolValidator(),
            new AutomationProbe(),
            new EtherCatScanProbe(),
            hardStoneStateProbe: new HardStoneStateProbe(_ => new StuckServoEnableTransport()));

        var exitCode = app.Run(["read-hardstone-state", "--station", station]);

        Assert.Equal(0, exitCode);
        Assert.Contains("mode_command=8", output.Text);
        Assert.Contains("mode_display=0", output.Text);
        Assert.Contains("Diagnosis: Switched On but not Operation Enabled", output.Text);
        Assert.Contains("S-ON", output.Text);
        Assert.Contains("STO", output.Text);
    }

    [Fact]
    public void RealRunSequenceBlocksWhenStationReadinessFails()
    {
        var station = CreateHardStoneStation();
        var output = new BufferOutput();
        var app = new HelperApp(
            output,
            stationReadinessCheck: _ => FailedReadinessReport());

        var exitCode = app.Run(["run-sequence", "--station", station, "--confirm-motion", "--profile", "1deg"]);

        Assert.Equal(2, exitCode);
        Assert.Contains("Station readiness failed; run Check Station before motion.", output.Text);
        Assert.Contains("[error] hardstone-ti5", output.Text);
    }

    [Fact]
    public void FakeRunSequenceSkipsStationReadinessGate()
    {
        var station = CreateHardStoneStation();
        var output = new BufferOutput();
        var app = new HelperApp(
            output,
            stationReadinessCheck: _ => FailedReadinessReport());

        var exitCode = app.Run(["run-sequence", "--station", station, "--fake", "--profile", "1deg"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Production sequence: PASS", output.Text);
    }

    private sealed class BufferOutput : IOutput
    {
        private readonly StringWriter writer = new();

        public string Text => writer.ToString();

        public void WriteLine(string message) => writer.WriteLine(message);

        public void WriteError(string message) => writer.WriteLine(message);
    }

    private static StationReadinessReport FailedReadinessReport() =>
        new(
            DateTimeOffset.UtcNow,
            false,
            "Station readiness checks found issues.",
            [new CheckItem("hardstone-ti5", "error", "HardStone master did not report Ti5 EtherCAT OP.")],
            null,
            null,
            null,
            null);

    private static string CreateHardStoneStation()
    {
        var station = Path.Combine(Path.GetTempPath(), $"jointbench-hardstone-helper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(station);
        File.WriteAllText(Path.Combine(station, "bus.yaml"), """
            protocol: hardstone_swd
            hardstone:
              firmware_elf: fake.elf
              adapter_speed_khz: 1000
              counts_per_degree: 1000
            """);
        File.WriteAllText(Path.Combine(station, "device.yaml"), """
            device:
              name: Ti5 Harmonic Joint
            """);
        File.WriteAllText(Path.Combine(station, "safety.yaml"), """
            limits:
              min_position_deg: -30
              max_position_deg: 750
              max_current_a: 3
              max_temperature_c: 60
              max_following_error_deg: 2
            """);
        File.WriteAllText(Path.Combine(station, "tests.yaml"), """
            tests:
              - name: PositionStep1Deg
                target_position_deg: 1
              - name: LowSpeedForwardTwoTurns
                type: position_ramp
                start_position_deg: 0
                target_position_deg: 720
              - name: LowSpeedReverseTwoTurns
                type: position_ramp
                start_position_deg: 720
                target_position_deg: 0
            """);
        return station;
    }

    private sealed class StuckServoEnableTransport : IHardStoneDebugTransport
    {
        private readonly Dictionary<string, int> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["g_host_statusword"] = 0x0233,
            ["g_host_controlword"] = 0x000F,
            ["g_host_command_ack"] = 1,
            ["g_host_heartbeat_ack"] = 1,
            ["g_host_target_relative_counts"] = 0,
            ["g_host_command_error"] = 0,
            ["g_host_enabled"] = 0,
            ["g_host_watchdog_ok"] = 1,
            ["g_host_zero_position_counts"] = 0,
            ["g_host_actual_position_counts"] = 0,
            ["g_host_target_position_counts"] = 0,
            ["g_host_actual_velocity_counts"] = 0,
            ["g_host_torque_actual"] = 0,
            ["g_host_mode_of_operation"] = 8,
            ["g_host_mode_display"] = 0,
            ["g_ec_last_vendor"] = 0x00522227,
            ["g_ec_last_product"] = 0x00009253,
            ["g_ec_last_revision"] = 0x00010005,
            ["g_ec_operational"] = 1,
            ["g_ec_ti5_slave_index"] = 1,
        };

        public Task ConnectAsync(HardStoneDebugOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ReadInt32Async(string symbol, CancellationToken cancellationToken)
        {
            values.TryGetValue(symbol, out var value);
            return Task.FromResult(value);
        }

        public Task WriteInt32Async(string symbol, int value, CancellationToken cancellationToken)
        {
            values[symbol] = value;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
