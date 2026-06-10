using JointBench.TwinCat;
using AdsState = TwinCAT.Ads.AdsState;
using StateInfo = TwinCAT.Ads.StateInfo;

namespace JointBench.TwinCatHelper.Tests;

public sealed class ReportAndTwinCatPreparationTests
{
    [Fact]
    public void HardStoneFirmwareAppliesTi5EsiStartupInitCommands()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "data", "ethercat-firmware-candidates", "ti5-safe", "Src", "soem.c"));

        Assert.Contains("write8(slvcnt, 0x6060, 00, TI5_PROFILE_POSITION_MODE)", source);
        Assert.Contains("write16(slvcnt, 0x2003, 00, 0)", source);
    }

    [Fact]
    public void HardStoneFirmwareRunsProcessDataAtTi5Sync0Cycle()
    {
        var root = FindRepositoryRoot();
        var soemSource = File.ReadAllText(Path.Combine(root, "data", "ethercat-firmware-candidates", "ti5-safe", "Src", "soem.c"));
        var timerSource = File.ReadAllText(Path.Combine(root, "data", "ethercat-firmware-candidates", "ti5-safe", "Src", "bsp", "GeneralTIM", "bsp_GeneralTIM.c"));

        Assert.Contains("#define SYNC0TIME 1000000", soemSource);
        Assert.Contains("htim4.Init.Prescaler = 83", timerSource);
        Assert.Contains("htim4.Init.Period = 999", timerSource);
    }

    [Fact]
    public void ReportWriterCreatesLocalizedArtifacts()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "PASS",
            ReportLanguage.SimplifiedChinese,
            output,
            DeviceInfo.Ti5Default(AdsConnectionOptions.LocalDefault()),
            [StageResult.Pass("EnableOnly"), StageResult.Pass("PositionStep1Deg")],
            [ActuatorState.Sample(0, 1, 0, 0.2, 30), ActuatorState.Sample(1, 1, 1, 0.3, 31)],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5()),
            []);

        var written = writer.Write(result);

        Assert.True(File.Exists(written.RawDataCsvPath));
        Assert.True(File.Exists(written.EventsLogPath));
        Assert.True(File.Exists(written.ConfigSnapshotPath));
        Assert.True(File.Exists(written.MarkdownReportPath));
        Assert.True(File.Exists(written.HtmlReportPath));
        Assert.Contains("JointBench 测试报告", File.ReadAllText(written.MarkdownReportPath));
        Assert.Contains("配置快照", File.ReadAllText(written.HtmlReportPath));
    }

    [Fact]
    public void ReportWriterCanGenerateEnglishReport()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "PASS",
            ReportLanguage.English,
            output,
            DeviceInfo.Ti5Default(AdsConnectionOptions.LocalDefault()),
            [StageResult.Pass("EnableOnly")],
            [ActuatorState.Sample(0, 0, 0, 0.2, 30)],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5()),
            []);

        var written = writer.Write(result);

        Assert.Contains("JointBench Test Report", File.ReadAllText(written.MarkdownReportPath));
        Assert.Contains("Configuration Snapshot", File.ReadAllText(written.HtmlReportPath));
    }

    [Fact]
    public void ReportWriterIncludesHardStoneBackendMetadata()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var hardStone = new HardStoneStationOptions(
            @"C:\repo\data\ethercat-firmware-candidates\ti5-safe\build-gcc\ysf4_ti5_ethercat_master.elf",
            1000,
            1456.3556);
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "PASS",
            ReportLanguage.English,
            output,
            new DeviceInfo(
                "Ti5 Harmonic Joint",
                "YS-F4Pro",
                "HardStone YS-F4Pro",
                "hardstone_swd",
                "OpenOCD SWD mailbox",
                string.Empty,
                0,
                "g_host_*",
                0x00522227,
                0x00009253,
                0x00010005,
                "connected"),
            [StageResult.Pass("EnableOnly")],
            [ActuatorState.Sample(0, 0, 0, 0.2, 30) with { Protocol = "hardstone_swd" }],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5(),
                "hardstone_swd",
                hardStone),
            []);

        var written = writer.Write(result);

        var markdown = File.ReadAllText(written.MarkdownReportPath);
        var snapshot = File.ReadAllText(written.ConfigSnapshotPath);
        Assert.Contains("Protocol: hardstone_swd", markdown);
        Assert.Contains("Transport: OpenOCD SWD mailbox", markdown);
        Assert.Contains("protocol: hardstone_swd", snapshot);
        Assert.Contains("firmware_elf:", snapshot);
        Assert.Contains("counts_per_degree: 1456.3556", snapshot);
    }

    [Fact]
    public void ReportWriterSummarizesFinalStoppedHardStoneState()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var finalSample = ActuatorState.Sample(2.5, 1.0, 1.0, 0.0, 24.0) with
        {
            Protocol = "hardstone_swd",
            Enabled = false,
            FaultCode = 0,
            Statusword = 0x0021,
            Controlword = 0x0006,
            CommandSequence = 4,
            WatchdogOk = true,
            FollowingErrorDegrees = 0.0,
            DebugCommandAck = 4,
            DebugHeartbeatAck = 54,
            DebugTargetRelativeCounts = 1456,
            DebugTargetCounts = 1456,
            DebugActualCounts = 1456,
        };
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "PASS",
            ReportLanguage.English,
            output,
            new DeviceInfo(
                "Ti5 Harmonic Joint",
                "YS-F4Pro",
                "HardStone YS-F4Pro",
                "hardstone_swd",
                "OpenOCD SWD mailbox",
                string.Empty,
                0,
                "g_host_*",
                0x00522227,
                0x00009253,
                0x00010005,
                "connected"),
            [StageResult.Pass("EnableOnly"), StageResult.Pass("PositionStep1Deg")],
            [ActuatorState.Sample(0, 1, 0, 0.0, 24.0) with { Protocol = "hardstone_swd" }, finalSample],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5(),
                "hardstone_swd",
                new HardStoneStationOptions("firmware.elf", 1000, 1456.3556)),
            []);

        var written = writer.Write(result);

        var markdown = File.ReadAllText(written.MarkdownReportPath);
        var html = File.ReadAllText(written.HtmlReportPath);
        Assert.Contains("## Final State", markdown);
        Assert.Contains("Enabled: False", markdown);
        Assert.Contains("Fault/Error Code: 0", markdown);
        Assert.Contains("Statusword: 0x0021", markdown);
        Assert.Contains("Controlword: 0x0006", markdown);
        Assert.Contains("Command Ack: 4", markdown);
        Assert.Contains("Heartbeat Ack: 54", markdown);
        Assert.Contains("Target Relative Counts: 1456", markdown);
        Assert.Contains("Target Counts: 1456", markdown);
        Assert.Contains("Actual Counts: 1456", markdown);
        Assert.Contains("<h2>Final State</h2>", html);
        Assert.Contains("<td>False</td>", html);
        Assert.Contains("<td>0x0021</td>", html);
        Assert.Contains("<td>0x0006</td>", html);
    }

    [Fact]
    public void ReportWriterIncludesFinalCiA402DiagnosisAndModeFeedback()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var finalSample = ActuatorState.Sample(8.0, 0.0, 0.0, 0.0, 24.0) with
        {
            Protocol = "hardstone_swd",
            Enabled = false,
            Statusword = 0x0233,
            Controlword = 0x000F,
            ModeOfOperationCommand = 8,
            ModeOfOperationDisplay = 0,
        };
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "ABORTED",
            ReportLanguage.English,
            output,
            DeviceInfo.Ti5Default(AdsConnectionOptions.LocalDefault()),
            [StageResult.Aborted("EnableOnly", ["Timed out waiting for enable."])],
            [finalSample],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5(),
                "hardstone_swd"),
            []);

        var written = writer.Write(result);

        var markdown = File.ReadAllText(written.MarkdownReportPath);
        var html = File.ReadAllText(written.HtmlReportPath);
        var csv = File.ReadAllText(written.RawDataCsvPath);
        Assert.Contains("Mode Command: 8", markdown);
        Assert.Contains("Mode Display: 0", markdown);
        Assert.Contains("Diagnosis: Switched On but not Operation Enabled", markdown);
        Assert.Contains("S-ON", markdown);
        Assert.Contains("<td>8</td>", html);
        Assert.Contains("Switched On but not Operation Enabled", html);
        Assert.Contains("mode_command,mode_display", csv);
        Assert.Contains(",8,0", csv);
    }

    [Fact]
    public void ReportWriterSummarizesMotionRangeAndFinalPosition()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "PASS",
            ReportLanguage.English,
            output,
            DeviceInfo.Ti5Default(AdsConnectionOptions.LocalDefault()),
            [StageResult.Pass("EnableOnly"), StageResult.Pass("PositionStep1Deg")],
            [
                ActuatorState.Sample(0.0, 1.0, 0.0, 0.0, 24.0),
                ActuatorState.Sample(1.0, 1.0, 0.8, 0.1, 24.0),
                ActuatorState.Sample(2.0, 1.0, 1.2, 0.0, 24.0) with { Enabled = false },
            ],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5()),
            []);

        var written = writer.Write(result);

        var markdown = File.ReadAllText(written.MarkdownReportPath);
        var html = File.ReadAllText(written.HtmlReportPath);
        Assert.Contains("## Motion Summary", markdown);
        Assert.Contains("Sample Count: 3", markdown);
        Assert.Contains("Actual Position Range: 0..1.2 deg", markdown);
        Assert.Contains("Actual Travel: 1.2 deg", markdown);
        Assert.Contains("Final Target: 1 deg", markdown);
        Assert.Contains("Final Actual: 1.2 deg", markdown);
        Assert.Contains("<h2>Motion Summary</h2>", html);
        Assert.Contains("<td>1.2 deg</td>", html);
    }

    [Fact]
    public void ReportWriterIncludesPreRunHardStoneStateSnapshot()
    {
        var output = Path.Combine(Path.GetTempPath(), $"jointbench-report-test-{Guid.NewGuid():N}");
        var writer = new TestReportWriter(new FixedClock(new DateTimeOffset(2026, 5, 26, 8, 0, 0, TimeSpan.Zero)));
        var preRunState = new HardStoneStateSnapshot(
            true,
            "HardStone Ti5 state is readable.",
            1,
            1,
            0x00522227,
            0x00009253,
            0x00010005,
            0x0208,
            0x0000,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            true,
            0,
            -102699,
            -102699,
            0,
            0,
            1,
            1,
            -70.5178,
            -70.5178,
            0.0);
        var result = ProductionSequenceResult.Create(
            "JB20260526-080000",
            "PASS",
            ReportLanguage.English,
            output,
            DeviceInfo.Ti5Default(AdsConnectionOptions.LocalDefault()),
            [StageResult.Pass("EnableOnly")],
            [ActuatorState.Sample(0.0, 0.0, 0.0, 0.0, 24.0)],
            new TestConfigSnapshot(
                AdsConnectionOptions.LocalDefault(),
                SafetyLimits.DefaultTi5(),
                [TestConfig.ForTarget(1.0)],
                StationScaling.DefaultTi5(),
                "hardstone_swd"),
            ["   0.000s  Test initialized."]) with
        {
            PreRunState = preRunState,
        };

        var written = writer.Write(result);

        var markdown = File.ReadAllText(written.MarkdownReportPath);
        var html = File.ReadAllText(written.HtmlReportPath);
        Assert.Contains("## Pre-run State", markdown);
        Assert.Contains("EtherCAT OP: 1", markdown);
        Assert.Contains("Enabled: False", markdown);
        Assert.Contains("Error: 0", markdown);
        Assert.Contains("Statusword: 0x0208", markdown);
        Assert.Contains("Mode Command: 1", markdown);
        Assert.Contains("Mode Display: 1", markdown);
        Assert.Contains("Actual Counts: -102699", markdown);
        Assert.Contains("Actual Position: -70.5178 deg", markdown);
        Assert.Contains("<h2>Pre-run State</h2>", html);
        Assert.Contains("<td>0x0208</td>", html);
    }

    [Fact]
    public void TwinCatPreparationBuildsExpectedTi5PdoLinkPlan()
    {
        var box = new EtherCatBoxInfo(
            1,
            1,
            "Drive 1 (Ti5Robot_JointMotor)",
            "TIID^Device_1_EtherCAT^Drive 1 (Ti5Robot_JointMotor)",
            9099,
            "Ti5Robot_JointMotor",
            0x00522227,
            0x00009253,
            0x00010005,
            0,
            1001,
            0,
            @"C:\TwinCAT\3.1\Config\Io\EtherCAT\Ti5Robot_JointMotor_2.0.xml",
            @"C:\Temp\box.xml");

        var plan = TwinCatPdoLinkPlanner.BuildTi5Plan(box);

        Assert.All(plan.Links, link => Assert.StartsWith("TIPC^", link.PlcVariablePath));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5Statusword") && link.EtherCatVariablePath.Contains("Status"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5ModeOfOperationDisplay") && link.EtherCatVariablePath.Contains("ModeOfOperationDisplay"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5Controlword") && link.EtherCatVariablePath.Contains("Control"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5TargetVelocity") && link.EtherCatVariablePath.Contains("TargetVelocity"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.StartsWith("TIPC^JointBenchPlc^JointBenchPlc Instance^PlcTask Inputs^"));
        Assert.Equal(0x00522227, plan.VendorId);
        Assert.Equal(0x00009253, plan.ProductCode);
    }

    [Fact]
    public void TwinCatPreparationUsesActualScannedPdoEntryNames()
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"jointbench-ti5-box-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xmlPath, """
            <TreeItem>
              <EtherCAT>
                <Slave>
                  <ProcessData>
                    <TxPdo>
                      <Entry><Index>#x6041</Index><SubIndex>0</SubIndex><Name>Status Word</Name></Entry>
                      <Entry><Index>#x6064</Index><SubIndex>0</SubIndex><Name>ActualPosition</Name></Entry>
                      <Entry><Index>#x606c</Index><SubIndex>0</SubIndex><Name>ActualVelocity</Name></Entry>
                      <Entry><Index>#x6077</Index><SubIndex>0</SubIndex><Name>Torque Actual</Name></Entry>
                      <Entry><Index>#x6061</Index><SubIndex>0</SubIndex><Name>ModeOfOperationDisplay</Name></Entry>
                    </TxPdo>
                    <RxPdo>
                      <Entry><Index>#x6040</Index><SubIndex>0</SubIndex><Name>Control Word</Name></Entry>
                      <Entry><Index>#x607a</Index><SubIndex>0</SubIndex><Name>TargetPosition</Name></Entry>
                      <Entry><Index>#x60ff</Index><SubIndex>0</SubIndex><Name>TargetVelocity</Name></Entry>
                      <Entry><Index>#x6071</Index><SubIndex>0</SubIndex><Name>TargetTorque</Name></Entry>
                      <Entry><Index>#x6060</Index><SubIndex>0</SubIndex><Name>ModeOfOperation</Name></Entry>
                    </RxPdo>
                  </ProcessData>
                </Slave>
              </EtherCAT>
            </TreeItem>
            """);
        var box = new EtherCatBoxInfo(
            1,
            1,
            "Drive 1 (Ti5Robot_JointMotor)",
            "TIID^Device_1_EtherCAT^Drive 1 (Ti5Robot_JointMotor)",
            9099,
            "Ti5Robot_JointMotor",
            0x00522227,
            0x00009253,
            0x00010005,
            0,
            1001,
            0,
            @"C:\TwinCAT\3.1\Config\Io\EtherCAT\Ti5Robot_JointMotor_2.0.xml",
            xmlPath);

        var plan = TwinCatPdoLinkPlanner.BuildTi5Plan(box);

        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5Statusword") && link.EtherCatVariablePath.EndsWith("^Status Word"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5ActualPosition") && link.EtherCatVariablePath.EndsWith("^ActualPosition"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5ModeOfOperationDisplay") && link.EtherCatVariablePath.EndsWith("^ModeOfOperationDisplay"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5Controlword") && link.EtherCatVariablePath.EndsWith("^Control Word"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.nTi5TargetVelocity") && link.EtherCatVariablePath.EndsWith("^TargetVelocity"));
        Assert.Contains("0x603F:0", plan.MissingEntries);
        Assert.Contains("temperature", string.Join(";", plan.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StationConfigLoaderReadsIgnoredStationDirectoryShape()
    {
        var station = Path.Combine(Path.GetTempPath(), $"jointbench-station-{Guid.NewGuid():N}");
        Directory.CreateDirectory(station);
        File.WriteAllText(Path.Combine(station, "bus.yaml"), """
            protocol: twincat_ads
            ads:
              ams_net_id: 127.0.0.1.1.1
              ams_port: 851
              host: localhost
              timeout_ms: 1000
            """);
        File.WriteAllText(Path.Combine(station, "device.yaml"), """
            device:
              name: Ti5 Harmonic Joint
              vendor_id: 0x00522227
              product_code: 0x00009253
              revision_number: 0x00010005
            ads:
              symbol_prefix: MAIN.stJointBench
            scaling:
              encoder_counts_per_rev: 524288
              gear_ratio: 2.0
              position_direction: -1
              zero_offset_deg: 12.5
              current_scale_a_per_unit: 0.01
              temperature_scale_c_per_unit: 0.5
              auto_zero_on_check: true
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
                type: position_step_response
                target_position_deg: 1
                duration_s: 2.5
                sample_rate_hz: 100
              - name: LowSpeedForwardTwoTurns
                type: position_ramp
                start_position_deg: 0
                target_position_deg: 720
                duration_s: 36
                sample_rate_hz: 10
                max_settling_time_s: 38
                max_steady_state_error_deg: 1
              - name: LowSpeedReverseTwoTurns
                type: position_ramp
                start_position_deg: 720
                target_position_deg: 0
                duration_s: 36
                sample_rate_hz: 10
                max_settling_time_s: 38
                max_steady_state_error_deg: 1
            """);

        var config = StationConfigLoader.Load(station);

        Assert.Equal("127.0.0.1.1.1", config.Ads.AmsNetId);
        Assert.Equal("MAIN.stJointBench", config.Ads.SymbolPrefix);
        Assert.Equal(2.0, config.Scaling.GearRatio);
        Assert.Equal(-1, config.Scaling.PositionDirection);
        Assert.Equal(12.5, config.Scaling.ZeroOffsetDegrees);
        Assert.True(config.Scaling.AutoZeroOnCheck);
        Assert.Equal(3, config.Tests.Count);
        Assert.Equal(["PositionStep1Deg", "LowSpeedForwardTwoTurns", "LowSpeedReverseTwoTurns"], config.Tests.Select(test => test.Name));
        Assert.Equal("position_ramp", config.Tests[1].MotionProfile);
        Assert.Equal(720.0, config.Tests[1].TargetPositionDegrees);
        Assert.True(config.MotionAllowed);
    }

    [Fact]
    public void StationConfigLoaderReadsHardStoneBackendOptions()
    {
        var station = Path.Combine(Path.GetTempPath(), $"jointbench-station-{Guid.NewGuid():N}");
        Directory.CreateDirectory(station);
        File.WriteAllText(Path.Combine(station, "bus.yaml"), """
            protocol: hardstone_swd
            hardstone:
              firmware_elf: data/ethercat-firmware-candidates/ti5-safe/build-gcc/ysf4_ti5_ethercat_master.elf
              adapter_speed_khz: 1000
              counts_per_degree: 1456
            """);
        File.WriteAllText(Path.Combine(station, "device.yaml"), """
            device:
              name: Ti5 Harmonic Joint
            scaling:
              encoder_counts_per_rev: 524288
              gear_ratio: 1.0
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

        var config = StationConfigLoader.Load(station);

        Assert.Equal("hardstone_swd", config.Protocol);
        var hardStone = Assert.IsType<HardStoneStationOptions>(config.HardStone);
        Assert.Equal(1000, hardStone.AdapterSpeedKHz);
        Assert.Equal(1456, hardStone.CountsPerDegree);
        Assert.EndsWith(@"ysf4_ti5_ethercat_master.elf", hardStone.FirmwareElfPath);
        Assert.True(config.MotionAllowed);
    }

    [Fact]
    public void StationConfigLoaderReadsSplitOneAndFiveDegreeTestFiles()
    {
        var station = Path.Combine(Path.GetTempPath(), $"jointbench-station-{Guid.NewGuid():N}");
        Directory.CreateDirectory(station);
        File.WriteAllText(Path.Combine(station, "bus.yaml"), """
            protocol: twincat_ads
            ads:
              ams_net_id: 127.0.0.1.1.1
              ams_port: 851
            """);
        File.WriteAllText(Path.Combine(station, "device.yaml"), """
            device:
              name: Ti5 Harmonic Joint
            ads:
              symbol_prefix: MAIN.stJointBench
            """);
        File.WriteAllText(Path.Combine(station, "safety.yaml"), """
            limits:
              min_position_deg: -6
              max_position_deg: 6
              max_current_a: 3
              max_temperature_c: 60
              max_following_error_deg: 2
            """);
        File.WriteAllText(Path.Combine(station, "test_1deg.yaml"), """
            test:
              target_position_deg: 1
              duration_s: 2.5
              sample_rate_hz: 100
            pass_fail:
              max_settling_time_s: 1.0
              max_steady_state_error_deg: 0.2
            """);
        File.WriteAllText(Path.Combine(station, "test_5deg.yaml"), """
            test:
              target_position_deg: 5
              duration_s: 3
              sample_rate_hz: 100
            pass_fail:
              max_settling_time_s: 1.2
              max_steady_state_error_deg: 0.5
            """);

        var config = StationConfigLoader.Load(station);

        Assert.Equal(["PositionStep1Deg", "PositionStep5Deg"], config.Tests.Select(test => test.Name));
        Assert.Equal(2.5, config.Tests[0].DurationSeconds);
        Assert.Equal(0.5, config.Tests[1].MaxSteadyStateErrorDegrees);
    }

    [Fact]
    public void TwinCatProjectTemplateSetUsesRepositoryPouTemplates()
    {
        var templates = TwinCatProjectTemplateSet.FromRepositoryRoot(Environment.CurrentDirectory);

        Assert.EndsWith(@"twincat\src\ST_JointBenchAds.TcDUT", templates.PouTemplatePaths[0]);
        Assert.EndsWith(@"twincat\src\ST_Ti5CiA402PdoInput.TcDUT", templates.PouTemplatePaths[1]);
        Assert.EndsWith(@"twincat\src\ST_Ti5CiA402PdoOutput.TcDUT", templates.PouTemplatePaths[2]);
        Assert.EndsWith(@"twincat\src\FB_JointBenchAxis.TcPOU", templates.PouTemplatePaths[3]);
        Assert.EndsWith(@"twincat\src\MAIN.TcPOU", templates.PouTemplatePaths[4]);
        Assert.All(templates.PouTemplatePaths, path => Assert.True(File.Exists(path), path));
        Assert.Equal("Standard PLC Template", templates.PlcTemplateName);
    }

    [Fact]
    public void AxisTemplateUsesTi5DefaultCyclicPositionMode()
    {
        var template = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "twincat", "src", "FB_JointBenchAxis.TcPOU"));

        Assert.Contains("stDriveOut.nModeOfOperation := 8", template);
        Assert.Contains("Cyclic Synchronous Position Mode", template);
        Assert.Contains("nTargetVelocity", template);
    }

    [Fact]
    public void TwinCatProjectProbeReportCanCarryScanAndLinkDiagnostics()
    {
        var linkPlan = new Ti5PdoLinkPlan(
            0x00522227,
            0x00009253,
            0x00010005,
            "TIID^Device_1_EtherCAT^Drive 1 (Ti5Robot_JointMotor)",
            [new PdoVariableLink("TIPC^JointBenchPlc^JointBenchPlc Instance^PlcTask Inputs^MAIN.nTi5Statusword", "TIID^Device_1_EtherCAT^Drive 1 (Ti5Robot_JointMotor)^Status Word")],
            [],
            []);
        var report = new TwinCatProjectProbeReport(
            true,
            string.Empty,
            @"C:\Temp\jointbench",
            "JointBenchProjectProbe",
            "JointBenchPlc",
            ["MAIN.TcPOU"],
            true,
            linkPlan,
            ["TIPC^JointBenchPlc^JointBenchPlc Instance^PlcTask Inputs^MAIN.nTi5Statusword <= TIID^Device_1_EtherCAT^Drive 1 (Ti5Robot_JointMotor)^Status Word"]);

        Assert.True(report.Ok);
        Assert.True(report.PlcBuildSucceeded);
        Assert.NotNull(report.LinkPlan);
        Assert.Single(report.LinkedVariables);
    }

    [Fact]
    public void TwinCatPreparationActivateRunsProjectPreparer()
    {
        var station = CreateMinimalStation();
        var box = Ti5Box();
        var linkPlan = TwinCatPdoLinkPlanner.BuildTi5Plan(box);
        var preparer = new CapturingProjectPreparer(new TwinCatProjectProbeReport(
            true,
            string.Empty,
            @"C:\Temp\jointbench",
            "JointBenchProjectProbe",
            "JointBenchPlc",
            ["MAIN.TcPOU"],
            true,
            linkPlan,
            ["linked"],
            ActivationRequested: true,
            Activated: true));
        var service = new TwinCatPreparationService(
            scanner: _ => new EtherCatScanReport(true, "", "", null, [], [box], Ti5Found: true),
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            projectPreparer: preparer,
            repositoryRoot: @"C:\Repo");

        var report = service.Prepare(new TwinCatPreparationRequest(station, Activate: true));

        Assert.True(report.Ok);
        Assert.True(report.Activated);
        Assert.NotNull(report.ProjectReport);
        Assert.NotNull(preparer.Request);
        Assert.True(preparer.Request.Activate);
        Assert.Equal(@"C:\Repo", preparer.Request.RepositoryRoot);
    }

    [Fact]
    public void TwinCatPreparationDryRunDoesNotRunProjectPreparer()
    {
        var station = CreateMinimalStation();
        var box = Ti5Box();
        var preparer = new CapturingProjectPreparer(new TwinCatProjectProbeReport(
            true,
            string.Empty,
            @"C:\Temp\jointbench",
            "JointBenchProjectProbe",
            "JointBenchPlc",
            [],
            true,
            TwinCatPdoLinkPlanner.BuildTi5Plan(box),
            []));
        var service = new TwinCatPreparationService(
            scanner: _ => new EtherCatScanReport(true, "", "", null, [], [box], Ti5Found: true),
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            projectPreparer: preparer);

        var report = service.Prepare(new TwinCatPreparationRequest(station, Activate: false));

        Assert.True(report.Ok);
        Assert.False(report.Activated);
        Assert.Null(preparer.Request);
    }

    [Fact]
    public void TwinCatPreparationActivateRefreshesLatestProjectWhenOnlineScanUnavailable()
    {
        var station = CreateMinimalStation();
        var preparer = new CapturingProjectPreparer(new TwinCatProjectProbeReport(
            true,
            string.Empty,
            @"C:\Temp\jointbench",
            "JointBenchProjectProbe",
            "JointBenchPlc",
            ["MAIN.TcPOU"],
            true,
            null,
            ["existing links preserved"],
            ActivationRequested: true,
            Activated: true));
        var service = new TwinCatPreparationService(
            scanner: _ => new EtherCatScanReport(false, "No EtherCAT master was found.", "", null, [], [], Ti5Found: false),
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            projectPreparer: preparer,
            repositoryRoot: @"C:\Repo");

        var report = service.Prepare(new TwinCatPreparationRequest(station, Activate: true));

        Assert.True(report.Ok);
        Assert.True(report.Activated);
        Assert.NotNull(preparer.RefreshRequest);
        Assert.Null(preparer.Request);
    }

    [Fact]
    public void TwinCatRuntimeDiagnosticsExtractsLicenseViolations()
    {
        var errors = TwinCatRuntimeDiagnostics.ExtractStartupErrors(
            [
                new TwinCatRuntimeEvent(DateTimeOffset.UtcNow, "TwinCAT System", "Error", "TwinCAT System Message: Source: License Server; Message: License Violation: License 'TC3 PLC' not found, Requested by 'JointBenchPlc Instance'"),
                new TwinCatRuntimeEvent(DateTimeOffset.UtcNow, "TcSysSrv", "Error", "Error: >> ADS ERROR: no license found << checking TwinCAT Licenses!"),
                new TwinCatRuntimeEvent(DateTimeOffset.UtcNow, "TcSysSrv", "Information", "TcRTime Server started: TcRTime."),
            ]);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains("TC3 PLC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("no license", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlcRuntimeControllerStartsInvalidPort()
    {
        var client = new FakePlcRuntimeStateClient(new StateInfo(AdsState.Invalid, 0));

        var state = TwinCatPlcRuntimeController.EnsureRun(client, TimeSpan.Zero);

        Assert.Equal(AdsState.Run, state.AdsState);
        Assert.Contains(client.WrittenStates, written => written.AdsState == AdsState.Run);
    }

    private sealed class FakePlcRuntimeStateClient(StateInfo initialState) : IPlcRuntimeStateClient
    {
        private StateInfo state = initialState;

        public List<StateInfo> WrittenStates { get; } = [];

        public StateInfo ReadState() => state;

        public void WriteControl(StateInfo stateInfo)
        {
            WrittenStates.Add(stateInfo);
            state = new StateInfo(AdsState.Run, stateInfo.DeviceState);
        }
    }

    private static EtherCatBoxInfo Ti5Box() =>
        new(
            1,
            1,
            "Drive 1 (Ti5Robot_JointMotor)",
            "TIID^Device_1_EtherCAT^Drive 1 (Ti5Robot_JointMotor)",
            9099,
            "Ti5Robot_JointMotor",
            0x00522227,
            0x00009253,
            0x00010005,
            0,
            1001,
            0,
            @"C:\TwinCAT\3.1\Config\Io\EtherCAT\Ti5Robot_JointMotor_2.0.xml",
            @"C:\Temp\box.xml");

    private static string CreateMinimalStation()
    {
        var station = Path.Combine(Path.GetTempPath(), $"jointbench-station-{Guid.NewGuid():N}");
        Directory.CreateDirectory(station);
        File.WriteAllText(Path.Combine(station, "bus.yaml"), """
            ads:
              ams_net_id: 127.0.0.1.1.1
              ams_port: 851
            """);
        File.WriteAllText(Path.Combine(station, "device.yaml"), """
            device:
              vendor_id: 0x00522227
              product_code: 0x00009253
              revision_number: 0x00010005
            ads:
              symbol_prefix: MAIN.stJointBench
            """);
        File.WriteAllText(Path.Combine(station, "safety.yaml"), """
            limits:
              min_position_deg: -6
              max_position_deg: 6
              max_current_a: 3
              max_temperature_c: 60
              max_following_error_deg: 2
            """);
        File.WriteAllText(Path.Combine(station, "tests.yaml"), """
            tests:
              - target_position_deg: 1
              - target_position_deg: 5
            """);
        return station;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "twincat", "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing twincat/src was not found.");
    }

    private sealed class CapturingProjectPreparer : ITwinCatProjectPreparer
    {
        private readonly TwinCatProjectProbeReport report;

        public CapturingProjectPreparer(TwinCatProjectProbeReport report)
        {
            this.report = report;
        }

        public TwinCatProjectPreparationRequest? Request { get; private set; }

        public TwinCatProjectPreparationRequest? RefreshRequest { get; private set; }

        public TwinCatProjectProbeReport Prepare(TwinCatProjectPreparationRequest request)
        {
            Request = request;
            return report;
        }

        public TwinCatProjectProbeReport RefreshLatest(TwinCatProjectPreparationRequest request)
        {
            RefreshRequest = request;
            return report;
        }
    }
}
