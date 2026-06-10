namespace JointBench.TwinCat;

public sealed class ProductionTestSequenceRunner
{
    private readonly IMotionAdapter adapter;
    private readonly TestReportWriter reportWriter;
    private readonly Action<string>? progress;

    public ProductionTestSequenceRunner(IMotionAdapter adapter, TestReportWriter reportWriter)
        : this(adapter, reportWriter, null)
    {
    }

    public ProductionTestSequenceRunner(IMotionAdapter adapter, TestReportWriter reportWriter, Action<string>? progress)
    {
        this.adapter = adapter;
        this.reportWriter = reportWriter;
        this.progress = progress;
    }

    public async Task<ProductionSequenceResult> RunAsync(ProductionSequenceRequest request, CancellationToken cancellationToken)
    {
        var testId = reportWriter.Clock.NowLocal().ToString("'JB'yyyyMMdd-HHmmss");
        var outputDirectory = Path.Combine(request.OutputRoot, testId);
        var events = new List<string>();
        var samples = new List<ActuatorState>();
        var stages = new List<StageResult>();
        var start = DateTimeOffset.UtcNow;

        void Event(string message)
        {
            var line = $"{(DateTimeOffset.UtcNow - start).TotalSeconds,8:F3}s  {message}";
            events.Add(line);
            progress?.Invoke(line);
        }

        try
        {
            Event($"Test {testId} initialized.");
            foreach (var check in request.PreRunChecks)
            {
                Event($"Pre-run check [{check.Status}] {check.Name}: {check.Message}{FormatCheckDetail(check.Detail)}");
            }

            if (request.PreRunState is { } preRunState)
            {
                Event(
                    $"Pre-run state: op={preRunState.EtherCatOperational}, enabled={preRunState.Enabled}, error={preRunState.CommandError}, statusword=0x{preRunState.Statusword:X4}, actual={preRunState.ActualPositionDegrees:F4}deg.");
            }

            await adapter.ConnectAsync(cancellationToken);
            Event("ADS adapter connected.");
            var runtimeConfig = await adapter.ApplyRuntimeConfigAsync(request.Safety, request.Scaling, cancellationToken);
            Event($"{runtimeConfig.Message} {runtimeConfig.Detail}");
            if (!runtimeConfig.Ok)
            {
                throw new InvalidOperationException($"{runtimeConfig.Message} {runtimeConfig.Detail}");
            }

            var device = await adapter.ReadDeviceInfoAsync(cancellationToken);
            Event($"Device info read: {device.DeviceId}.");

            Event("Enable-only stage started.");
            Event("Waiting for bOperationEnabled=True.");
            await adapter.SetEnableAsync(true, cancellationToken);
            var enableSample = await adapter.SampleAsync(0.01, 0.0, cancellationToken);
            samples.Add(enableSample);
            var enableFailure = SafetyFailure(enableSample, request.Tests.FirstOrDefault() ?? TestConfig.ForTarget(1.0));
            if (enableFailure is not null)
            {
                stages.Add(StageResult.Fail("EnableOnly", [enableFailure]));
                await adapter.EmergencyStopAsync(cancellationToken);
                return Finish(request, testId, outputDirectory, device, stages, samples, events);
            }

            stages.Add(StageResult.Pass("EnableOnly"));
            Event($"Enable-only stage passed. statusword=0x{enableSample.Statusword ?? 0:X4}, position={enableSample.ActualPositionDegrees:F3}deg.");
            if (MailboxDetail(enableSample) is { } enableMailboxDetail)
            {
                Event($"EnableOnly mailbox: {enableMailboxDetail}");
            }

            foreach (var config in request.Tests)
            {
                var stage = await RunMotionStageAsync(config, samples, Event, cancellationToken);
                stages.Add(stage);
                if (config.Name == "PositionStep1Deg" && stage.Result != "PASS")
                {
                    Event("Remaining motion stages skipped because 1deg did not pass.");
                    break;
                }

                if (stage.Result != "PASS")
                {
                    Event("Remaining motion stages skipped because the previous stage did not pass.");
                    break;
                }
            }

            await adapter.EmergencyStopAsync(cancellationToken);
            Event("Stop requested after sequence.");
            await RecordPostStopSampleAsync(samples, Event, cancellationToken);
            return Finish(request, testId, outputDirectory, device, stages, samples, events);
        }
        catch (Exception exc)
        {
            Event($"ERROR: {exc.Message}");
            try
            {
                await adapter.EmergencyStopAsync(CancellationToken.None);
                Event("Emergency stop requested after exception.");
            }
            catch (Exception stopExc)
            {
                Event($"Emergency stop failed after exception: {stopExc.Message}");
            }

            var device = DeviceInfo.Ti5Default(request.Ads);
            stages.Add(StageResult.Aborted(stages.Count == 0 ? "EnableOnly" : stages[^1].StageName, [exc.Message]));
            return Finish(request, testId, outputDirectory, device, stages, samples, events);
        }
    }

    private Task<StageResult> RunMotionStageAsync(
        TestConfig config,
        List<ActuatorState> allSamples,
        Action<string> eventSink,
        CancellationToken cancellationToken)
    {
        return string.Equals(config.MotionProfile, "position_ramp", StringComparison.OrdinalIgnoreCase)
            ? RunRampAsync(config, allSamples, eventSink, cancellationToken)
            : RunStepAsync(config, allSamples, eventSink, cancellationToken);
    }

    private async Task<StageResult> RunStepAsync(
        TestConfig config,
        List<ActuatorState> allSamples,
        Action<string> eventSink,
        CancellationToken cancellationToken)
    {
        var stageSamples = new List<ActuatorState>();
        var failureReasons = new List<string>();
        var aborted = false;

        eventSink($"Stage {config.Name} started.");
        await adapter.SendPositionCommandAsync(config.TargetPositionDegrees, cancellationToken);
        var sampleCount = (int)(config.DurationSeconds * config.SampleRateHz) + 1;
        for (var index = 0; index < sampleCount; index++)
        {
            var timestamp = index * config.SamplePeriodSeconds;
            var state = await adapter.SampleAsync(config.SamplePeriodSeconds, timestamp, cancellationToken);
            stageSamples.Add(state);
            allSamples.Add(state);

            var safetyFailure = SafetyFailure(state, config);
            if (safetyFailure is not null)
            {
                aborted = true;
                failureReasons.Add(safetyFailure);
                eventSink($"Safety abort: {safetyFailure}");
                await adapter.EmergencyStopAsync(cancellationToken);
                break;
            }

            if (index == 0 || index == sampleCount - 1 || index % Math.Max(1, (int)(config.SampleRateHz / 2.0)) == 0)
            {
                eventSink($"{config.Name}: target={config.TargetPositionDegrees:F3}deg actual={state.ActualPositionDegrees:F3}deg current={state.CurrentA:F2}A temp={state.TemperatureC:F1}C.");
                if (MailboxDetail(state) is { } mailboxDetail)
                {
                    eventSink($"{config.Name} mailbox: {mailboxDetail}");
                }
            }

            if (!adapter.IsSimulation)
            {
                await Task.Delay(TimeSpan.FromSeconds(config.SamplePeriodSeconds), cancellationToken);
            }
        }

        var metrics = StepResponseAnalyzer.Analyze(stageSamples, config);
        var judgment = StepResponseAnalyzer.Judge(metrics, config, aborted, failureReasons);
        eventSink($"Stage {config.Name} finished with {judgment.Result}.");
        return new StageResult(config.Name, judgment.Result, judgment.FailureReasons);
    }

    private async Task<StageResult> RunRampAsync(
        TestConfig config,
        List<ActuatorState> allSamples,
        Action<string> eventSink,
        CancellationToken cancellationToken)
    {
        var stageSamples = new List<ActuatorState>();
        var failureReasons = new List<string>();
        var aborted = false;

        eventSink($"Stage {config.Name} started.");
        var alignment = await AlignRampStartAsync(config, allSamples, eventSink, cancellationToken);
        if (alignment is not null)
        {
            return alignment;
        }

        var sampleCount = Math.Max(2, (int)(config.DurationSeconds * config.SampleRateHz) + 1);
        var commandTarget = config.StartPositionDegrees;
        var nominalStepDegrees = Math.Abs(config.TargetPositionDegrees - config.StartPositionDegrees) / (sampleCount - 1);
        var advanceToleranceDegrees = RampAdvanceToleranceDegrees(config);
        for (var index = 0; index < sampleCount; index++)
        {
            var timestamp = index * config.SamplePeriodSeconds;
            if (index > 0)
            {
                var previousState = stageSamples.Count > 0 ? stageSamples[^1] : null;
                var commandError = previousState is null
                    ? 0.0
                    : Math.Abs(commandTarget - previousState.ActualPositionDegrees);
                if (commandError <= advanceToleranceDegrees)
                {
                    commandTarget = MoveTowards(commandTarget, config.TargetPositionDegrees, nominalStepDegrees);
                }
            }

            await adapter.SendPositionCommandAsync(commandTarget, cancellationToken);

            var state = await adapter.SampleAsync(config.SamplePeriodSeconds, timestamp, cancellationToken);
            stageSamples.Add(state);
            allSamples.Add(state);

            var safetyFailure = SafetyFailure(state, config);
            if (safetyFailure is not null)
            {
                aborted = true;
                failureReasons.Add(safetyFailure);
                eventSink($"Safety abort: {safetyFailure}");
                await adapter.EmergencyStopAsync(cancellationToken);
                break;
            }

            if (index == 0 || index == sampleCount - 1 || index % Math.Max(1, (int)config.SampleRateHz) == 0)
            {
                eventSink($"{config.Name}: target={commandTarget:F3}deg actual={state.ActualPositionDegrees:F3}deg current={state.CurrentA:F2}A temp={state.TemperatureC:F1}C.");
                if (MailboxDetail(state) is { } mailboxDetail)
                {
                    eventSink($"{config.Name} mailbox: {mailboxDetail}");
                }
            }

            if (!adapter.IsSimulation)
            {
                await Task.Delay(TimeSpan.FromSeconds(config.SamplePeriodSeconds), cancellationToken);
            }
        }

        var judgment = JudgeRamp(config, stageSamples, aborted, failureReasons);
        eventSink($"Stage {config.Name} finished with {judgment.Result}.");
        return new StageResult(config.Name, judgment.Result, judgment.FailureReasons);
    }

    private async Task<StageResult?> AlignRampStartAsync(
        TestConfig config,
        List<ActuatorState> allSamples,
        Action<string> eventSink,
        CancellationToken cancellationToken)
    {
        await adapter.SendPositionCommandAsync(config.StartPositionDegrees, cancellationToken);
        var tolerance = RampStartAlignmentToleranceDegrees(config);
        var samplePeriod = Math.Max(0.05, config.SamplePeriodSeconds);
        var maxSamples = Math.Max(1, (int)(Math.Min(12.0, config.MaxSettlingTimeSeconds) / samplePeriod));
        for (var index = 0; index < maxSamples; index++)
        {
            var state = await adapter.SampleAsync(samplePeriod, 0.0, cancellationToken);
            allSamples.Add(state);
            var safetyFailure = SafetyFailure(state, config);
            if (safetyFailure is not null)
            {
                eventSink($"Safety abort while aligning {config.Name}: {safetyFailure}");
                await adapter.EmergencyStopAsync(cancellationToken);
                return StageResult.Aborted(config.Name, [safetyFailure]);
            }

            var error = Math.Abs(config.StartPositionDegrees - state.ActualPositionDegrees);
            if (error <= tolerance)
            {
                eventSink($"{config.Name}: start aligned at {state.ActualPositionDegrees:F3}deg.");
                return null;
            }

            if (!adapter.IsSimulation)
            {
                await Task.Delay(TimeSpan.FromSeconds(samplePeriod), cancellationToken);
            }
        }

        var message = $"{config.Name} could not align to start {config.StartPositionDegrees:F3}deg before ramp.";
        eventSink(message);
        await adapter.EmergencyStopAsync(cancellationToken);
        return StageResult.Aborted(config.Name, [message]);
    }

    private async Task RecordPostStopSampleAsync(
        List<ActuatorState> allSamples,
        Action<string> eventSink,
        CancellationToken cancellationToken)
    {
        var state = await adapter.SampleAsync(0.0, allSamples.Count > 0 ? allSamples[^1].TimestampSeconds : 0.0, cancellationToken);
        allSamples.Add(state);
        eventSink($"Post-stop state: statusword=0x{state.Statusword ?? 0:X4}, controlword=0x{state.Controlword ?? 0:X4}, enabled={state.Enabled}, position={state.ActualPositionDegrees:F3}deg.");
        if (MailboxDetail(state) is { } mailboxDetail)
        {
            eventSink($"PostStop mailbox: {mailboxDetail}");
        }
    }

    private ProductionSequenceResult Finish(
        ProductionSequenceRequest request,
        string testId,
        string outputDirectory,
        DeviceInfo device,
        IReadOnlyList<StageResult> stages,
        IReadOnlyList<ActuatorState> samples,
        IReadOnlyList<string> events)
    {
        var expectedStageCount = request.Tests.Count + 1;
        var overall = stages.Any(stage => stage.Result is "ABORTED") ? "ABORTED" :
            stages.Any(stage => stage.Result is "FAIL" or "INVALID") ? "FAIL" :
            stages.Count >= expectedStageCount ? "PASS" : "FAIL";
        var result = ProductionSequenceResult.Create(
            testId,
            overall,
            request.Language,
            outputDirectory,
            device,
            stages,
            samples,
            new TestConfigSnapshot(request.Ads, request.Safety, request.Tests, request.Scaling, request.Protocol, request.HardStone),
            events) with
        {
            PreRunChecks = request.PreRunChecks,
            PreRunState = request.PreRunState,
        };
        reportWriter.Write(result);
        return result;
    }

    private static string FormatCheckDetail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})";

    private static string? SafetyFailure(ActuatorState state, TestConfig config)
    {
        if (state.WatchdogOk is false)
        {
            return "ADS watchdog reported unhealthy command updates.";
        }

        if (state.FaultCode != 0)
        {
            return $"Device fault code {state.FaultCode}.";
        }

        if (Math.Abs(state.ActualPositionDegrees) > config.MaxPositionAbsDegrees)
        {
            return $"Position {state.ActualPositionDegrees:F2}deg exceeded +/-{config.MaxPositionAbsDegrees:F2}deg.";
        }

        if (Math.Abs(state.CurrentA) > config.MaxCurrentA)
        {
            return $"Current {state.CurrentA:F2}A exceeded {config.MaxCurrentA:F2}A.";
        }

        var followingError = state.FollowingErrorDegrees ?? state.TargetPositionDegrees - state.ActualPositionDegrees;
        if (Math.Abs(followingError) > config.MaxFollowingErrorDegrees)
        {
            return $"Following error {followingError:F3}deg exceeded {config.MaxFollowingErrorDegrees:F3}deg.";
        }

        if (state.TemperatureC > config.MaxTemperatureC)
        {
            return $"Temperature {state.TemperatureC:F1}C exceeded {config.MaxTemperatureC:F1}C.";
        }

        return null;
    }

    private static StepJudgment JudgeRamp(
        TestConfig config,
        IReadOnlyList<ActuatorState> samples,
        bool aborted,
        IReadOnlyList<string> safetyFailures)
    {
        if (aborted)
        {
            return new StepJudgment("ABORTED", safetyFailures);
        }

        if (samples.Count == 0)
        {
            return new StepJudgment("INVALID", ["Ramp stage did not collect any samples."]);
        }

        var final = samples[^1];
        var finalError = Math.Abs(config.TargetPositionDegrees - final.ActualPositionDegrees);
        if (finalError > config.MaxSteadyStateErrorDegrees)
        {
            return new StepJudgment(
                "FAIL",
                [$"Final position error {finalError:F3}deg exceeded {config.MaxSteadyStateErrorDegrees:F3}deg."]);
        }

        return StepJudgment.Pass();
    }

    private static double RampStartAlignmentToleranceDegrees(TestConfig config) =>
        Math.Max(0.05, Math.Min(0.2, Math.Min(config.MaxSteadyStateErrorDegrees, config.MaxFollowingErrorDegrees * 0.5)));

    private static double RampAdvanceToleranceDegrees(TestConfig config) =>
        Math.Max(0.05, Math.Min(0.5, Math.Min(config.MaxSteadyStateErrorDegrees, config.MaxFollowingErrorDegrees * 0.5)));

    private static double MoveTowards(double current, double target, double maxStep)
    {
        if (maxStep <= 0.0)
        {
            return target;
        }

        var delta = target - current;
        return Math.Abs(delta) <= maxStep ? target : current + Math.Sign(delta) * maxStep;
    }

    private static string? MailboxDetail(ActuatorState state)
    {
        if (!string.Equals(state.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"command_ack={state.DebugCommandAck}, heartbeat_ack={state.DebugHeartbeatAck}, target_relative_counts={state.DebugTargetRelativeCounts}, target_counts={state.DebugTargetCounts}, actual_counts={state.DebugActualCounts}";
    }
}
