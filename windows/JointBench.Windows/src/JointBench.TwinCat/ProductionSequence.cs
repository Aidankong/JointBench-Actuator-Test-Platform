namespace JointBench.TwinCat;

public sealed class ProductionTestSequenceRunner
{
    private readonly AdsMotionAdapter adapter;
    private readonly TestReportWriter reportWriter;
    private readonly Action<string>? progress;

    public ProductionTestSequenceRunner(AdsMotionAdapter adapter, TestReportWriter reportWriter)
        : this(adapter, reportWriter, null)
    {
    }

    public ProductionTestSequenceRunner(AdsMotionAdapter adapter, TestReportWriter reportWriter, Action<string>? progress)
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
            var enableFailure = SafetyFailure(enableSample, request.Tests[0]);
            if (enableFailure is not null)
            {
                stages.Add(StageResult.Fail("EnableOnly", [enableFailure]));
                await adapter.EmergencyStopAsync(cancellationToken);
                return Finish(request, testId, outputDirectory, device, stages, samples, events);
            }

            stages.Add(StageResult.Pass("EnableOnly"));
            Event($"Enable-only stage passed. statusword=0x{enableSample.Statusword ?? 0:X4}, position={enableSample.ActualPositionDegrees:F3}deg.");

            foreach (var config in request.Tests)
            {
                var stage = await RunStepAsync(config, samples, Event, cancellationToken);
                stages.Add(stage);
                if (config.Name == "PositionStep1Deg" && stage.Result != "PASS")
                {
                    Event("5deg stage skipped because 1deg did not pass.");
                    break;
                }
            }

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

    private ProductionSequenceResult Finish(
        ProductionSequenceRequest request,
        string testId,
        string outputDirectory,
        DeviceInfo device,
        IReadOnlyList<StageResult> stages,
        IReadOnlyList<ActuatorState> samples,
        IReadOnlyList<string> events)
    {
        var overall = stages.Any(stage => stage.Result is "ABORTED") ? "ABORTED" :
            stages.Any(stage => stage.Result is "FAIL" or "INVALID") ? "FAIL" :
            stages.Count >= 3 ? "PASS" : "FAIL";
        var result = ProductionSequenceResult.Create(
            testId,
            overall,
            request.Language,
            outputDirectory,
            device,
            stages,
            samples,
            new TestConfigSnapshot(request.Ads, request.Safety, request.Tests, request.Scaling),
            events);
        reportWriter.Write(result);
        return result;
    }

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

        if (state.TemperatureC > config.MaxTemperatureC)
        {
            return $"Temperature {state.TemperatureC:F1}C exceeded {config.MaxTemperatureC:F1}C.";
        }

        return null;
    }
}
