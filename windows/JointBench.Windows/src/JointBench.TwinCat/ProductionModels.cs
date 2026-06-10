namespace JointBench.TwinCat;

public enum ReportLanguage
{
    English,
    SimplifiedChinese,
}

public enum ProductionRunProfile
{
    FullAcceptance,
    OneDegreeVerification,
}

public sealed class SafetyLimitException(string message) : InvalidOperationException(message);

public sealed record SafetyLimits(
    double MinPositionDegrees,
    double MaxPositionDegrees,
    double MaxCurrentA,
    double MaxTemperatureC,
    double MaxFollowingErrorDegrees,
    int CommunicationTimeoutMs = 500,
    double MaxSpeedDps = 30.0)
{
    public static SafetyLimits DefaultTi5() => new(-30.0, 750.0, 3.0, 60.0, 2.0);
}

public sealed record TestConfig(
    string Name,
    double StartPositionDegrees,
    double TargetPositionDegrees,
    double DurationSeconds,
    double SampleRateHz,
    double SettlingBandPercent,
    double MaxPositionAbsDegrees,
    double MaxCurrentA,
    double MaxTemperatureC,
    double MaxFollowingErrorDegrees,
    double MaxOvershootPercent,
    double MaxSettlingTimeSeconds,
    double MaxSteadyStateErrorDegrees,
    string MotionProfile = "position_step_response")
{
    public double SamplePeriodSeconds => 1.0 / SampleRateHz;

    public static TestConfig ForTarget(double targetPositionDegrees, double durationSeconds = 2.5, double sampleRateHz = 100.0)
    {
        var isOneDegree = Math.Abs(targetPositionDegrees) <= 1.0;
        return new TestConfig(
            Math.Abs(targetPositionDegrees) <= 1.0 ? "PositionStep1Deg" : "PositionStep5Deg",
            0.0,
            targetPositionDegrees,
            durationSeconds,
            sampleRateHz,
            2.0,
            6.0,
            3.0,
            60.0,
            2.0,
            10.0,
            isOneDegree ? 1.0 : 1.2,
            isOneDegree ? 0.2 : 0.5);
    }

    public static TestConfig ForLowSpeedRamp(
        string name,
        double startPositionDegrees,
        double targetPositionDegrees,
        double durationSeconds = 144.0,
        double sampleRateHz = 5.0)
    {
        return new TestConfig(
            name,
            startPositionDegrees,
            targetPositionDegrees,
            durationSeconds,
            sampleRateHz,
            2.0,
            750.0,
            3.0,
            60.0,
            2.0,
            10.0,
            durationSeconds + 2.0,
            1.0,
            "position_ramp");
    }
}

public sealed record ActuatorState(
    double TimestampSeconds,
    double TargetPositionDegrees,
    double ActualPositionDegrees,
    double ActualSpeedDps,
    double CurrentA,
    double VoltageV,
    double TemperatureC,
    int FaultCode = 0,
    bool Enabled = true,
    string ControlMode = "position",
    string Protocol = "twincat_ads",
    int? Statusword = null,
    int? Controlword = null,
    int? CommandSequence = null,
    bool? WatchdogOk = null,
    double? FollowingErrorDegrees = null,
    int? DebugCommandAck = null,
    int? DebugHeartbeatAck = null,
    int? DebugTargetRelativeCounts = null,
    int? DebugTargetCounts = null,
    int? DebugActualCounts = null,
    int? ModeOfOperationCommand = null,
    int? ModeOfOperationDisplay = null)
{
    public static ActuatorState Sample(double timestampSeconds, double targetDegrees, double actualDegrees, double currentA, double temperatureC) =>
        new(
            timestampSeconds,
            targetDegrees,
            actualDegrees,
            0.0,
            currentA,
            24.0,
            temperatureC,
            WatchdogOk: true,
            FollowingErrorDegrees: targetDegrees - actualDegrees);

    public IReadOnlyDictionary<string, object?> ToCsvRow(string testId, int sampleIndex) =>
        new Dictionary<string, object?>
        {
            ["test_id"] = testId,
            ["sample_index"] = sampleIndex,
            ["timestamp_s"] = TimestampSeconds,
            ["target_position_deg"] = TargetPositionDegrees,
            ["actual_position_deg"] = ActualPositionDegrees,
            ["actual_speed_dps"] = ActualSpeedDps,
            ["current_a"] = CurrentA,
            ["voltage_v"] = VoltageV,
            ["temperature_c"] = TemperatureC,
            ["fault_code"] = FaultCode,
            ["enabled"] = Enabled,
            ["control_mode"] = ControlMode,
            ["protocol"] = Protocol,
            ["statusword"] = Statusword,
            ["controlword"] = Controlword,
            ["command_sequence"] = CommandSequence,
            ["watchdog_ok"] = WatchdogOk,
            ["following_error_deg"] = FollowingErrorDegrees,
            ["debug_command_ack"] = DebugCommandAck,
            ["debug_heartbeat_ack"] = DebugHeartbeatAck,
            ["debug_target_relative_counts"] = DebugTargetRelativeCounts,
            ["debug_target_counts"] = DebugTargetCounts,
            ["debug_actual_counts"] = DebugActualCounts,
            ["mode_command"] = ModeOfOperationCommand,
            ["mode_display"] = ModeOfOperationDisplay,
        };
}

public sealed record StepResponseMetrics(
    double? ResponseDelaySeconds,
    double? RiseTimeSeconds,
    double? SettlingTimeSeconds,
    double OvershootPercent,
    double? SteadyStateErrorDegrees,
    double PeakCurrentA,
    double AverageCurrentA,
    double MaxTemperatureC,
    double? JitterDegrees);

public sealed record StepJudgment(string Result, IReadOnlyList<string> FailureReasons)
{
    public static StepJudgment Pass() => new("PASS", []);
}

public sealed record DeviceInfo(
    string DeviceId,
    string SerialNumber,
    string AdapterType,
    string Protocol,
    string TransportMode,
    string AmsNetId,
    int AmsPort,
    string AdsSymbolPrefix,
    int VendorId,
    int ProductCode,
    int RevisionNumber,
    string TwinCatRouteStatus)
{
    public static DeviceInfo Ti5Default(AdsConnectionOptions options) =>
        new(
            "Ti5 Harmonic Joint",
            "TwinCAT-ADS",
            "TwinCAT ADS",
            "twincat_ads",
            "TwinCAT ADS",
            options.AmsNetId,
            options.Port,
            options.SymbolPrefix,
            0x00522227,
            0x00009253,
            0x00010005,
            "connected");
}

public sealed record StageResult(string StageName, string Result, IReadOnlyList<string> FailureReasons)
{
    public static StageResult Pass(string stageName) => new(stageName, "PASS", []);

    public static StageResult Fail(string stageName, IEnumerable<string> failureReasons) =>
        new(stageName, "FAIL", failureReasons.ToList());

    public static StageResult Aborted(string stageName, IEnumerable<string> failureReasons) =>
        new(stageName, "ABORTED", failureReasons.ToList());
}

public sealed record TestConfigSnapshot(
    AdsConnectionOptions Ads,
    SafetyLimits Safety,
    IReadOnlyList<TestConfig> Tests,
    StationScaling Scaling,
    string Protocol = "twincat_ads",
    HardStoneStationOptions? HardStone = null);

public sealed record ProductionSequenceRequest(
    string OutputRoot,
    ReportLanguage Language,
    AdsConnectionOptions Ads,
    SafetyLimits Safety,
    IReadOnlyList<TestConfig> Tests)
{
    public StationScaling Scaling { get; init; } = StationScaling.DefaultTi5();

    public string Protocol { get; init; } = "twincat_ads";

    public HardStoneStationOptions? HardStone { get; init; }

    public IReadOnlyList<CheckItem> PreRunChecks { get; init; } = [];

    public HardStoneStateSnapshot? PreRunState { get; init; }

    public static ProductionSequenceRequest ForDefaultAcceptance(string outputRoot, ReportLanguage language) =>
        new(
            outputRoot,
            language,
            AdsConnectionOptions.LocalDefault(),
            SafetyLimits.DefaultTi5(),
            [
                TestConfig.ForTarget(1.0, 2.5, 100.0),
                TestConfig.ForLowSpeedRamp("LowSpeedForwardTwoTurns", 0.0, 720.0),
                TestConfig.ForLowSpeedRamp("LowSpeedReverseTwoTurns", 720.0, 0.0),
            ]);

    public ProductionSequenceRequest WithProfile(ProductionRunProfile profile)
    {
        if (profile == ProductionRunProfile.FullAcceptance)
        {
            return this;
        }

        if (profile != ProductionRunProfile.OneDegreeVerification)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown production run profile.");
        }

        var oneDegreeTest = Tests.FirstOrDefault(test =>
            string.Equals(test.Name, "PositionStep1Deg", StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(test.MotionProfile, "position_ramp", StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(test.TargetPositionDegrees) <= 1.0));
        if (oneDegreeTest is null)
        {
            throw new InvalidOperationException("The 1deg verification profile requires a configured PositionStep1Deg test.");
        }

        return this with { Tests = [oneDegreeTest] };
    }
}

public sealed record ProductionSequenceResult(
    string TestId,
    string OverallResult,
    ReportLanguage Language,
    string OutputDirectory,
    DeviceInfo Device,
    IReadOnlyList<StageResult> StageResults,
    IReadOnlyList<ActuatorState> Samples,
    TestConfigSnapshot ConfigSnapshot,
    IReadOnlyList<string> Events)
{
    public IReadOnlyList<CheckItem> PreRunChecks { get; init; } = [];

    public HardStoneStateSnapshot? PreRunState { get; init; }

    public static ProductionSequenceResult Create(
        string testId,
        string overallResult,
        ReportLanguage language,
        string outputDirectory,
        DeviceInfo device,
        IReadOnlyList<StageResult> stageResults,
        IReadOnlyList<ActuatorState> samples,
        TestConfigSnapshot configSnapshot,
        IReadOnlyList<string> events) =>
        new(testId, overallResult, language, outputDirectory, device, stageResults, samples, configSnapshot, events);
}

public sealed record TestOutputArtifacts(
    string RawDataCsvPath,
    string EventsLogPath,
    string ConfigSnapshotPath,
    string MarkdownReportPath,
    string HtmlReportPath);
