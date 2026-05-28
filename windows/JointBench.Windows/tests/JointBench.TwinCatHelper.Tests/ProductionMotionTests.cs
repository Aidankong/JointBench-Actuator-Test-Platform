using JointBench.TwinCat;

namespace JointBench.TwinCatHelper.Tests;

public sealed class ProductionMotionTests
{
    [Fact]
    public void AnalyzerMatchesExpectedStepMetrics()
    {
        var config = TestConfig.ForTarget(1.0, durationSeconds: 2.5, sampleRateHz: 10);
        var samples = Enumerable.Range(0, 26)
            .Select(index =>
            {
                var timestamp = index / 10.0;
                var position = index < 3 ? 0.0 : Math.Min(1.0, (index - 2) / 8.0);
                return ActuatorState.Sample(timestamp, 1.0, position, currentA: 0.5, temperatureC: 30.0);
            })
            .ToList();

        var metrics = StepResponseAnalyzer.Analyze(samples, config);
        var judgment = StepResponseAnalyzer.Judge(metrics, config);

        Assert.Equal("PASS", judgment.Result);
        Assert.InRange(metrics.ResponseDelaySeconds!.Value, 0.5, 0.7);
        Assert.InRange(metrics.RiseTimeSeconds!.Value, 0.6, 1.0);
        Assert.Equal(0.0, metrics.OvershootPercent, precision: 6);
        Assert.True(Math.Abs(metrics.SteadyStateErrorDegrees!.Value) < 0.05);
    }

    [Fact]
    public async Task SequenceRunsFiveDegreeOnlyAfterOneDegreePasses()
    {
        var io = new FakeAdsSymbolClient();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault()),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));

        var result = await runner.RunAsync(
            ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English),
            CancellationToken.None);

        Assert.Equal("PASS", result.OverallResult);
        Assert.Equal(["EnableOnly", "PositionStep1Deg", "PositionStep5Deg"], result.StageResults.Select(stage => stage.StageName));
        Assert.True(result.StageResults[1].Result == "PASS");
        Assert.True(result.StageResults[2].Result == "PASS");
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 1.0));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 5.0));
    }

    [Fact]
    public async Task SequenceSkipsFiveDegreeWhenOneDegreeFails()
    {
        var io = new FakeAdsSymbolClient { ForceActualPositionDegrees = 0.0 };
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault()),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));

        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.SimplifiedChinese);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.NotEqual("PASS", result.OverallResult);
        Assert.Equal(["EnableOnly", "PositionStep1Deg"], result.StageResults.Select(stage => stage.StageName));
        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 5.0));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".bStop") && Equals(write.Value, true));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".bEnable") && Equals(write.Value, false));
    }

    [Fact]
    public async Task TargetAboveFiveDegreesIsRejectedBeforeAdsStart()
    {
        var io = new FakeAdsSymbolClient();
        var adapter = new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault());

        await Assert.ThrowsAsync<SafetyLimitException>(() => adapter.SendPositionCommandAsync(6.0, CancellationToken.None));

        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".bStart"));
    }

    [Fact]
    public async Task PositionCommandPulsesStartLowHighLowForPlcEdgeDetection()
    {
        var io = new FakeAdsSymbolClient();
        var adapter = new AdsMotionAdapter(
            io,
            AdsConnectionOptions.LocalDefault(),
            startPulseDuration: TimeSpan.Zero);

        await adapter.SendPositionCommandAsync(1.0, CancellationToken.None);

        var startWrites = io.Writes
            .Where(write => write.Symbol.EndsWith(".bStart"))
            .Select(write => Assert.IsType<bool>(write.Value))
            .ToList();
        Assert.Equal([false, true, false], startWrites);
    }

    [Fact]
    public async Task MotionAdapterBumpsCommandSequenceOnEnableStartSampleAndStop()
    {
        var io = new FakeAdsSymbolClient();
        var adapter = new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault());

        await adapter.ConnectAsync(CancellationToken.None);
        await adapter.SetEnableAsync(true, CancellationToken.None);
        await adapter.SendPositionCommandAsync(1.0, CancellationToken.None);
        await adapter.SampleAsync(0.01, 0.01, CancellationToken.None);
        await adapter.EmergencyStopAsync(CancellationToken.None);

        var sequenceWrites = io.Writes
            .Where(write => write.Symbol.EndsWith(".nCommandSequence"))
            .Select(write => Assert.IsType<int>(write.Value))
            .ToList();
        Assert.True(sequenceWrites.Count >= 5);
        Assert.Equal(sequenceWrites, sequenceWrites.OrderBy(value => value).ToList());
        Assert.Equal(sequenceWrites.Distinct().Count(), sequenceWrites.Count);
    }

    [Fact]
    public async Task AutoZeroUsesRawEncoderPositionInsteadOfAlreadyOffsetPosition()
    {
        var io = new FakeAdsSymbolClient();
        await io.WriteAsync("MAIN.nTi5ActualPosition", -1024, CancellationToken.None);
        await io.WriteAsync("MAIN.stJointBench.fActualPositionDeg", 0.0, CancellationToken.None);
        var config = new StationConfig(
            AdsConnectionOptions.LocalDefault(),
            SafetyLimits.DefaultTi5(),
            StationScaling.DefaultTi5() with { AutoZeroOnCheck = true },
            [TestConfig.ForTarget(1.0), TestConfig.ForTarget(5.0)],
            "MAIN.stJointBench",
            0x00522227,
            0x00009253,
            0x00010005);
        var configurator = new AdsRuntimeConfigurator(() => io, TimeSpan.Zero);

        var report = await configurator.ApplyAsync(config, CancellationToken.None);

        Assert.True(report.Ok);
        var zeroOffsetWrite = io.Writes.Last(write => write.Symbol == "MAIN.fTi5ZeroOffsetDeg");
        Assert.Equal(0.703125, Assert.IsType<double>(zeroOffsetWrite.Value), precision: 6);
    }

    [Fact]
    public async Task MotionAdapterAutoZeroUsesRawEncoderPositionBeforeEnable()
    {
        var io = new FakeAdsSymbolClient();
        await io.WriteAsync("MAIN.nTi5ActualPosition", -1024, CancellationToken.None);
        await io.WriteAsync("MAIN.stJointBench.fActualPositionDeg", 0.0, CancellationToken.None);
        var adapter = new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault());

        var report = await adapter.ApplyRuntimeConfigAsync(
            SafetyLimits.DefaultTi5(),
            StationScaling.DefaultTi5() with { AutoZeroOnCheck = true },
            CancellationToken.None);

        Assert.True(report.Ok);
        var zeroOffsetWrite = io.Writes.Last(write => write.Symbol == "MAIN.fTi5ZeroOffsetDeg");
        Assert.Equal(0.703125, Assert.IsType<double>(zeroOffsetWrite.Value), precision: 6);
    }

    [Fact]
    public async Task SequencePublishesProgressEventsDuringProductionRun()
    {
        var progress = new List<string>();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(new FakeAdsSymbolClient(), AdsConnectionOptions.LocalDefault()),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))),
            progress.Add);

        await runner.RunAsync(
            ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English),
            CancellationToken.None);

        Assert.Contains(progress, item => item.Contains("Enable-only stage started."));
        Assert.Contains(progress, item => item.Contains("Stage PositionStep1Deg started."));
        Assert.Contains(progress, item => item.Contains("Stage PositionStep5Deg finished"));
    }

    [Fact]
    public async Task EnableTimeoutIncludesLatestDriveState()
    {
        var io = new FakeAdsSymbolClient { AutoOperationEnabledOnEnable = false };
        await io.WriteAsync("MAIN.stJointBench.nStatusword", 0x0040, CancellationToken.None);
        await io.WriteAsync("MAIN.stJointBench.nControlword", 0x0006, CancellationToken.None);
        await io.WriteAsync("MAIN.stJointBench.nErrorCode", 0, CancellationToken.None);
        var adapter = new AdsMotionAdapter(
            io,
            AdsConnectionOptions.LocalDefault(),
            enableTimeout: TimeSpan.FromMilliseconds(40),
            enablePollInterval: TimeSpan.FromMilliseconds(1));

        var exc = await Assert.ThrowsAsync<TimeoutException>(() => adapter.SetEnableAsync(true, CancellationToken.None));

        Assert.Contains("statusword=0x0040", exc.Message);
        Assert.Contains("controlword=0x0006", exc.Message);
        Assert.Contains("error=0", exc.Message);
    }

    [Fact]
    public async Task SequenceAbortsWhenNegativeCurrentMagnitudeExceedsLimit()
    {
        var io = new FakeAdsSymbolClient();
        await io.WriteAsync("MAIN.stJointBench.fCurrentA", -4.0, CancellationToken.None);
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault()),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));

        var result = await runner.RunAsync(
            ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English),
            CancellationToken.None);

        Assert.Equal("FAIL", result.OverallResult);
        Assert.Equal("EnableOnly", result.StageResults[0].StageName);
        Assert.Equal("FAIL", result.StageResults[0].Result);
        Assert.Contains("Current -4.00A exceeded 3.00A.", result.StageResults[0].FailureReasons);
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".bStop") && Equals(write.Value, true));
    }

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jointbench-sequence-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
