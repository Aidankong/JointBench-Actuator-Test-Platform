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
    public void AnalyzerAllowsHardwareStepWhenFinalErrorPassesButSettlingTimeIsUnavailable()
    {
        var config = TestConfig.ForTarget(1.0, durationSeconds: 2.5, sampleRateHz: 100);
        var samples = Enumerable.Range(0, 252)
            .Select(index =>
            {
                var timestamp = index / 100.0;
                var position = index < 4 ? 0.0 : 0.945;
                return ActuatorState.Sample(timestamp, 1.0, position, currentA: 0.0, temperatureC: 0.0);
            })
            .ToList();

        var metrics = StepResponseAnalyzer.Analyze(samples, config);
        var judgment = StepResponseAnalyzer.Judge(metrics, config);

        Assert.Null(metrics.SettlingTimeSeconds);
        Assert.Equal("PASS", judgment.Result);
    }

    [Fact]
    public async Task SequenceRunsTwoTurnRampOnlyAfterOneDegreePasses()
    {
        var io = new FakeAdsSymbolClient();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));

        var result = await runner.RunAsync(
            ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English),
            CancellationToken.None);

        Assert.Equal("PASS", result.OverallResult);
        Assert.Equal(["EnableOnly", "PositionStep1Deg", "LowSpeedForwardTwoTurns", "LowSpeedReverseTwoTurns"], result.StageResults.Select(stage => stage.StageName));
        Assert.True(result.StageResults[1].Result == "PASS");
        Assert.True(result.StageResults[2].Result == "PASS");
        Assert.True(result.StageResults[3].Result == "PASS");
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 1.0));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 720.0));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 0.0));
    }

    [Fact]
    public async Task OneDegreeVerificationProfileRunsOnlyEnableAndOneDegreeStage()
    {
        var io = new FakeAdsSymbolClient();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English)
            .WithProfile(ProductionRunProfile.OneDegreeVerification);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal("PASS", result.OverallResult);
        Assert.Equal(["EnableOnly", "PositionStep1Deg"], result.StageResults.Select(stage => stage.StageName));
        Assert.Equal(["PositionStep1Deg"], result.ConfigSnapshot.Tests.Select(test => test.Name));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 1.0));
        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 720.0));
    }

    [Fact]
    public async Task SequenceReportIncludesPreRunReadinessChecks()
    {
        var io = new FakeAdsSymbolClient();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English)
            .WithProfile(ProductionRunProfile.OneDegreeVerification) with
        {
            PreRunChecks =
            [
                new CheckItem("station-config", "ok", "Station config is motion-ready."),
                new CheckItem("hardstone-ti5", "ok", "HardStone master reports Ti5 EtherCAT OP.", "index=1, op=1"),
            ],
        };

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(2, result.PreRunChecks.Count);
        Assert.Contains(result.Events, line => line.Contains("Pre-run check [ok] hardstone-ti5"));
        Assert.Contains("Pre-run Checks", File.ReadAllText(Path.Combine(result.OutputDirectory, "report.md")));
        Assert.Contains("hardstone-ti5", File.ReadAllText(Path.Combine(result.OutputDirectory, "report.md")));
        Assert.Contains("Pre-run Checks", File.ReadAllText(Path.Combine(result.OutputDirectory, "report.html")));
    }

    [Fact]
    public void FullAcceptanceProfileKeepsConfiguredSequence()
    {
        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English)
            .WithProfile(ProductionRunProfile.FullAcceptance);

        Assert.Equal(
            ["PositionStep1Deg", "LowSpeedForwardTwoTurns", "LowSpeedReverseTwoTurns"],
            request.Tests.Select(test => test.Name));
    }

    [Fact]
    public void OneDegreeVerificationProfileRequiresConfiguredOneDegreeStage()
    {
        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English) with
        {
            Tests =
            [
                TestConfig.ForLowSpeedRamp("LowSpeedForwardTwoTurns", 0.0, 720.0),
                TestConfig.ForLowSpeedRamp("LowSpeedReverseTwoTurns", 720.0, 0.0),
            ],
        };

        var exc = Assert.Throws<InvalidOperationException>(() => request.WithProfile(ProductionRunProfile.OneDegreeVerification));

        Assert.Contains("PositionStep1Deg", exc.Message);
    }

    [Fact]
    public async Task SequenceSkipsTwoTurnRampsWhenOneDegreeFails()
    {
        var io = new FakeAdsSymbolClient { ForceActualPositionDegrees = 0.0 };
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));

        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.SimplifiedChinese);
        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.NotEqual("PASS", result.OverallResult);
        Assert.Equal(["EnableOnly", "PositionStep1Deg"], result.StageResults.Select(stage => stage.StageName));
        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 720.0));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".bStop") && Equals(write.Value, true));
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".bEnable") && Equals(write.Value, false));
    }

    [Fact]
    public async Task SequenceStopsRemainingStagesWhenTwoTurnRampAborts()
    {
        var io = new FakeAdsSymbolClient();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var unsafeRamp = TestConfig.ForLowSpeedRamp("LowSpeedForwardTwoTurns", 0.0, 10.0, durationSeconds: 2.0, sampleRateHz: 5.0) with
        {
            MaxPositionAbsDegrees = 1.0,
        };
        var reverseRamp = TestConfig.ForLowSpeedRamp("LowSpeedReverseTwoTurns", 99.0, 0.0, durationSeconds: 2.0, sampleRateHz: 5.0);
        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English) with
        {
            Tests = [TestConfig.ForTarget(1.0, 2.5, 100.0), unsafeRamp, reverseRamp],
        };

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal("ABORTED", result.OverallResult);
        Assert.Equal(["EnableOnly", "PositionStep1Deg", "LowSpeedForwardTwoTurns"], result.StageResults.Select(stage => stage.StageName));
        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 99.0));
    }

    [Fact]
    public async Task LowSpeedRampWaitsWhenActualPositionLagsCommand()
    {
        var io = new FakeAdsSymbolClient { ForceActualPositionDegrees = 0.0 };
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var request = ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English) with
        {
            Tests =
            [
                TestConfig.ForTarget(1.0, 0.5, 10.0) with { MaxSteadyStateErrorDegrees = 2.0 },
                TestConfig.ForLowSpeedRamp("LowSpeedForwardTwoTurns", 0.0, 10.0, durationSeconds: 1.0, sampleRateHz: 5.0),
            ],
        };

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.NotEqual("PASS", result.OverallResult);
        var rampTargets = io.Writes
            .Where(write => write.Symbol.EndsWith(".fTargetPositionDeg"))
            .Select(write => Assert.IsType<double>(write.Value))
            .Where(value => value > 1.0)
            .ToList();
        Assert.NotEmpty(rampTargets);
        Assert.All(rampTargets, value => Assert.InRange(value, 0.0, 2.1));
    }

    [Fact]
    public async Task SequenceClearsStaleTargetBeforeEnableOnlyCheck()
    {
        var io = new FakeAdsSymbolClient { ForceActualPositionDegrees = 0.0 };
        await io.WriteAsync("MAIN.stJointBench.fTargetPositionDeg", 4.0, CancellationToken.None);
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                io,
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var request = new ProductionSequenceRequest(
            TempDir(),
            ReportLanguage.English,
            AdsConnectionOptions.LocalDefault(),
            SafetyLimits.DefaultTi5(),
            []);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal("PASS", result.OverallResult);
        Assert.Equal("PASS", result.StageResults.Single().Result);
        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 0.0));
    }


    [Fact]
    public async Task TargetAboveDefaultFiveDegreeLimitIsRejectedBeforeAdsStart()
    {
        var io = new FakeAdsSymbolClient();
        var adapter = new AdsMotionAdapter(io, AdsConnectionOptions.LocalDefault());

        await Assert.ThrowsAsync<SafetyLimitException>(() => adapter.SendPositionCommandAsync(6.0, CancellationToken.None));

        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".bStart"));
    }

    [Fact]
    public async Task ConfiguredTwoTurnTargetIsAllowedButBeyondConfiguredLimitIsRejected()
    {
        var io = new FakeAdsSymbolClient();
        var adapter = new AdsMotionAdapter(
            io,
            AdsConnectionOptions.LocalDefault(),
            startPulseDuration: TimeSpan.Zero,
            maxTargetAbsDegrees: 720.0);

        await adapter.SendPositionCommandAsync(720.0, CancellationToken.None);
        await Assert.ThrowsAsync<SafetyLimitException>(() => adapter.SendPositionCommandAsync(721.0, CancellationToken.None));

        Assert.Contains(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 720.0));
        Assert.DoesNotContain(io.Writes, write => write.Symbol.EndsWith(".fTargetPositionDeg") && Equals(write.Value, 721.0));
    }

    [Fact]
    public async Task PositionCommandRepeatsStartPulseForPlcAndDriveSetpointLatch()
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
        Assert.Equal(9, startWrites.Count);
        Assert.Equal([false, true, false], startWrites.Take(3).ToArray());
        Assert.Equal([false, true, false], startWrites.Skip(3).Take(3).ToArray());
        Assert.Equal([false, true, false], startWrites.Skip(6).Take(3).ToArray());
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
        Assert.Contains(io.Writes, write => write.Symbol == "MAIN.stJointBench.fTargetPositionDeg" && Equals(write.Value, 0.0));
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
    public async Task RawStateProbeReadsPlcPdoAndAdsSurfaceValues()
    {
        var io = new FakeAdsSymbolClient();
        await io.WriteAsync("MAIN.nTi5ActualPosition", 123, CancellationToken.None);
        await io.WriteAsync("MAIN.nTi5TargetPosition", 456, CancellationToken.None);
        await io.WriteAsync("MAIN.nTi5TargetVelocity", 789, CancellationToken.None);
        await io.WriteAsync("MAIN.nTi5ActualTorqueOrCurrent", 7, CancellationToken.None);
        await io.WriteAsync("MAIN.nTi5ModeOfOperationDisplay", 1, CancellationToken.None);
        await io.WriteAsync("MAIN.fTi5CurrentScaleAPerUnit", 0.1, CancellationToken.None);
        await io.WriteAsync("MAIN.fTi5MaxCurrentA", 3.0, CancellationToken.None);
        await io.WriteAsync("MAIN.nTi5Controlword", 0x003F, CancellationToken.None);
        await io.WriteAsync("MAIN.nTi5ModeOfOperation", 1, CancellationToken.None);
        var probe = new AdsRawStateProbe(() => io);

        var report = probe.Read(AdsConnectionOptions.LocalDefault());

        Assert.Equal(123, report.Values["MAIN.nTi5ActualPosition"]);
        Assert.Equal(456, report.Values["MAIN.nTi5TargetPosition"]);
        Assert.Equal(789, report.Values["MAIN.nTi5TargetVelocity"]);
        Assert.Equal(7, report.Values["MAIN.nTi5ActualTorqueOrCurrent"]);
        Assert.Equal(1, report.Values["MAIN.nTi5ModeOfOperationDisplay"]);
        Assert.Equal(0.1, report.Values["MAIN.fTi5CurrentScaleAPerUnit"]);
        Assert.Equal(3.0, report.Values["MAIN.fTi5MaxCurrentA"]);
        Assert.Equal(0x003F, report.Values["MAIN.nTi5Controlword"]);
        Assert.Equal(1, report.Values["MAIN.nTi5ModeOfOperation"]);
        Assert.True((bool)report.Values["MAIN.stJointBench.bWatchdogOk"]!);
    }

    [Fact]
    public async Task SequencePublishesProgressEventsDuringProductionRun()
    {
        var progress = new List<string>();
        var runner = new ProductionTestSequenceRunner(
            new AdsMotionAdapter(
                new FakeAdsSymbolClient(),
                AdsConnectionOptions.LocalDefault(),
                startPulseDuration: TimeSpan.Zero,
                maxTargetAbsDegrees: 750.0),
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))),
            progress.Add);

        await runner.RunAsync(
            ProductionSequenceRequest.ForDefaultAcceptance(TempDir(), ReportLanguage.English),
            CancellationToken.None);

        Assert.Contains(progress, item => item.Contains("Enable-only stage started."));
        Assert.Contains(progress, item => item.Contains("Stage PositionStep1Deg started."));
        Assert.Contains(progress, item => item.Contains("Stage LowSpeedForwardTwoTurns started."));
        Assert.Contains(progress, item => item.Contains("Stage LowSpeedReverseTwoTurns finished"));
    }

    [Fact]
    public async Task CancelledSequenceRequestsEmergencyStopAndWritesAbortedReport()
    {
        var adapter = new CancellingMotionAdapter();
        var runner = new ProductionTestSequenceRunner(
            adapter,
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        using var cts = new CancellationTokenSource();
        adapter.CancelOnMotionSample = cts;

        var result = await runner.RunAsync(
            new ProductionSequenceRequest(
                TempDir(),
                ReportLanguage.English,
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0, durationSeconds: 2.5, sampleRateHz: 10.0)]),
            cts.Token);

        Assert.Equal("ABORTED", result.OverallResult);
        Assert.True(adapter.EmergencyStopCalled);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "report.md")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "events.log")));
    }

    [Fact]
    public async Task SequenceRunnerCanUseHardStoneMotionAdapterWithoutAds()
    {
        var io = new FakeHardStoneDebugTransport();
        var adapter = new HardStoneDebugMotionAdapter(
            io,
            new HardStoneDebugOptions("fake.elf", CountsPerDegree: 1000.0, MaxTargetAbsDegrees: 720.0));
        var runner = new ProductionTestSequenceRunner(
            adapter,
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var request = new ProductionSequenceRequest(
            TempDir(),
            ReportLanguage.English,
            AdsConnectionOptions.LocalDefault(),
            SafetyLimits.DefaultTi5(),
            [
                TestConfig.ForTarget(1.0, durationSeconds: 1.0, sampleRateHz: 10.0) with { MaxSteadyStateErrorDegrees = 0.2 },
            ]);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal("PASS", result.OverallResult);
        Assert.Contains(io.Writes, write => write.Symbol == "g_host_command_code" && write.Value == HardStoneHostCommands.Enable);
        Assert.Contains(io.Writes, write => write.Symbol == "g_host_command_code" && write.Value == HardStoneHostCommands.MoveRelative);
        Assert.Contains(io.Writes, write => write.Symbol == "g_host_target_relative_counts" && write.Value == 1000);
        Assert.All(result.Samples, sample => Assert.Equal("hardstone_swd", sample.Protocol));
    }

    [Fact]
    public async Task HardStoneSequenceEventsIncludeMailboxDiagnostics()
    {
        var adapter = new HardStoneDebugMotionAdapter(
            new FakeHardStoneDebugTransport(),
            new HardStoneDebugOptions("fake.elf", CountsPerDegree: 1000.0, MaxTargetAbsDegrees: 720.0));
        var runner = new ProductionTestSequenceRunner(
            adapter,
            new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero))));
        var request = new ProductionSequenceRequest(
            TempDir(),
            ReportLanguage.English,
            AdsConnectionOptions.LocalDefault(),
            SafetyLimits.DefaultTi5(),
            [TestConfig.ForTarget(1.0, durationSeconds: 0.2, sampleRateHz: 10.0) with { MaxSteadyStateErrorDegrees = 0.2 }]);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Contains(result.Events, line => line.Contains("mailbox: command_ack="));
        Assert.Contains(result.Events, line => line.Contains("EnableOnly mailbox: command_ack="));
        Assert.Contains(result.Events, line => line.Contains("PostStop mailbox: command_ack="));
        Assert.Contains(result.Events, line => line.Contains("actual_counts="));
        Assert.Contains(result.Events, line => line.Contains("target_counts="));
        Assert.Contains(result.Events, line => line.Contains("target_relative_counts=1000"));
        Assert.Contains(result.Samples, sample => sample.DebugActualCounts is not null && sample.DebugTargetCounts is not null);
        Assert.False(result.Samples[^1].Enabled);
    }

    [Fact]
    public async Task HardStoneStateProbeReadsRawMailboxSnapshot()
    {
        var io = new FakeHardStoneDebugTransport();
        await io.WriteInt32Async("g_host_actual_position_counts", 2000, CancellationToken.None);
        await io.WriteInt32Async("g_host_target_position_counts", 2500, CancellationToken.None);
        await io.WriteInt32Async("g_host_command_ack", 7, CancellationToken.None);
        await io.WriteInt32Async("g_host_heartbeat_ack", 9, CancellationToken.None);
        await io.WriteInt32Async("g_host_target_relative_counts", 1000, CancellationToken.None);
        await io.WriteInt32Async("g_host_mode_of_operation", 1, CancellationToken.None);
        await io.WriteInt32Async("g_host_mode_display", 1, CancellationToken.None);
        var probe = new HardStoneStateProbe(_ => io);

        var snapshot = probe.Read(HardStoneStationConfig());

        Assert.True(snapshot.Ok);
        Assert.Equal(1, snapshot.Ti5SlaveIndex);
        Assert.Equal(1, snapshot.EtherCatOperational);
        Assert.Equal(0x00522227, snapshot.VendorId);
        Assert.Equal(2000, snapshot.ActualPositionCounts);
        Assert.Equal(2500, snapshot.TargetPositionCounts);
        Assert.Equal(2.0, snapshot.ActualPositionDegrees);
        Assert.Equal(2.5, snapshot.TargetPositionDegrees);
        Assert.Equal(0.5, snapshot.FollowingErrorDegrees);
        Assert.Equal(7, snapshot.CommandAck);
        Assert.Equal(9, snapshot.HeartbeatAck);
        Assert.Equal(1000, snapshot.TargetRelativeCounts);
        Assert.Equal(1, snapshot.ModeOfOperationCommand);
        Assert.Equal(1, snapshot.ModeOfOperationDisplay);
    }

    [Fact]
    public async Task HardStoneDebugSessionLockRejectsConcurrentUseAndReleasesCleanly()
    {
        var name = $@"Local\JointBenchHardStoneDebugTest-{Guid.NewGuid():N}";
        using (HardStoneDebugSessionLock.Acquire(name, TimeSpan.Zero))
        {
            var exc = await Task.Run(() =>
                Assert.Throws<InvalidOperationException>(() =>
                    HardStoneDebugSessionLock.Acquire(name, TimeSpan.FromMilliseconds(10))));
            Assert.Contains("HardStone debug link is already in use", exc.Message);
        }

        using var reacquired = HardStoneDebugSessionLock.Acquire(name, TimeSpan.FromMilliseconds(100));
        Assert.NotNull(reacquired);
    }

    [Fact]
    public async Task HardStoneMotionAdapterRejectsTargetsOutsideConfiguredLimit()
    {
        var adapter = new HardStoneDebugMotionAdapter(
            new FakeHardStoneDebugTransport(),
            new HardStoneDebugOptions("fake.elf", CountsPerDegree: 1000.0, MaxTargetAbsDegrees: 5.0));

        await Assert.ThrowsAsync<SafetyLimitException>(() => adapter.SendPositionCommandAsync(6.0, CancellationToken.None));
    }

    [Fact]
    public async Task HardStoneEnableTimeoutIdentifiesServoEnableGate()
    {
        var adapter = new HardStoneDebugMotionAdapter(
            new StuckServoEnableHardStoneTransport(),
            new HardStoneDebugOptions(
                "fake.elf",
                CountsPerDegree: 1000.0,
                EnableTimeout: TimeSpan.FromMilliseconds(30),
                EnablePollInterval: TimeSpan.FromMilliseconds(1)));

        await adapter.ConnectAsync(CancellationToken.None);

        var exc = await Assert.ThrowsAsync<TimeoutException>(() => adapter.SetEnableAsync(true, CancellationToken.None));

        Assert.Contains("statusword=0x0233", exc.Message);
        Assert.Contains("controlword=0x000F", exc.Message);
        Assert.Contains("mode_command=8", exc.Message);
        Assert.Contains("mode_display=0", exc.Message);
        Assert.Contains("S-ON", exc.Message);
        Assert.Contains("STO", exc.Message);
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

    private static StationConfig HardStoneStationConfig() =>
        new(
            AdsConnectionOptions.LocalDefault(),
            SafetyLimits.DefaultTi5(),
            StationScaling.DefaultTi5(),
            [
                TestConfig.ForTarget(1.0),
                TestConfig.ForLowSpeedRamp("LowSpeedForwardTwoTurns", 0.0, 720.0),
                TestConfig.ForLowSpeedRamp("LowSpeedReverseTwoTurns", 720.0, 0.0),
            ],
            "g_host_*",
            0x00522227,
            0x00009253,
            0x00010005,
            "hardstone_swd",
            new HardStoneStationOptions("fake.elf", 1000, 1000.0));

    private sealed class CancellingMotionAdapter : IMotionAdapter
    {
        private bool motionSampleSeen;

        public CancellationTokenSource? CancelOnMotionSample { get; set; }

        public bool EmergencyStopCalled { get; private set; }

        public bool IsSimulation => false;

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DeviceInfo.Ti5Default(AdsConnectionOptions.LocalDefault()));

        public Task<AdsRuntimeConfigurationReport> ApplyRuntimeConfigAsync(
            SafetyLimits safety,
            StationScaling scaling,
            CancellationToken cancellationToken) =>
            Task.FromResult(AdsRuntimeConfigurationReport.Applied("ok"));

        public Task SetEnableAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendPositionCommandAsync(double positionDegrees, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<ActuatorState> SampleAsync(double dtSeconds, double timestampSeconds, CancellationToken cancellationToken)
        {
            if (timestampSeconds > 0.0 && !motionSampleSeen)
            {
                motionSampleSeen = true;
                CancelOnMotionSample?.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return ActuatorState.Sample(timestampSeconds, 1.0, timestampSeconds > 0 ? 0.2 : 0.0, 0.2, 30.0);
        }

        public Task EmergencyStopAsync(CancellationToken cancellationToken)
        {
            EmergencyStopCalled = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class StuckServoEnableHardStoneTransport : IHardStoneDebugTransport
    {
        private readonly Dictionary<string, int> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["g_host_statusword"] = 0x0221,
            ["g_host_controlword"] = 0x0002,
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
            ["g_host_mode_of_operation"] = 8,
            ["g_host_mode_display"] = 0,
            ["g_ec_last_vendor"] = 0x00522227,
            ["g_ec_last_product"] = 0x00009253,
            ["g_ec_last_revision"] = 0x00010005,
            ["g_ec_operational"] = 1,
            ["g_ec_ti5_slave_index"] = 1,
        };

        private int commandCode;

        public Task ConnectAsync(HardStoneDebugOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ReadInt32Async(string symbol, CancellationToken cancellationToken)
        {
            values.TryGetValue(symbol, out var value);
            return Task.FromResult(value);
        }

        public Task WriteInt32Async(string symbol, int value, CancellationToken cancellationToken)
        {
            values[symbol] = value;
            if (string.Equals(symbol, "g_host_command_code", StringComparison.OrdinalIgnoreCase))
            {
                commandCode = value;
            }
            else if (string.Equals(symbol, "g_host_command_sequence", StringComparison.OrdinalIgnoreCase))
            {
                values["g_host_command_ack"] = value;
                if (commandCode == HardStoneHostCommands.Enable)
                {
                    values["g_host_statusword"] = 0x0233;
                    values["g_host_controlword"] = 0x000F;
                }
            }
            else if (string.Equals(symbol, "g_host_heartbeat_sequence", StringComparison.OrdinalIgnoreCase))
            {
                values["g_host_heartbeat_ack"] = value;
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
