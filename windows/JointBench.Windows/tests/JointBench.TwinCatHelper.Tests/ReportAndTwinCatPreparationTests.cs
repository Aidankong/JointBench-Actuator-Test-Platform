using JointBench.TwinCat;

namespace JointBench.TwinCatHelper.Tests;

public sealed class ReportAndTwinCatPreparationTests
{
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
                [TestConfig.ForTarget(1.0)]),
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
                [TestConfig.ForTarget(1.0)]),
            []);

        var written = writer.Write(result);

        Assert.Contains("JointBench Test Report", File.ReadAllText(written.MarkdownReportPath));
        Assert.Contains("Configuration Snapshot", File.ReadAllText(written.HtmlReportPath));
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
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.stTi5In.nStatusword") && link.EtherCatVariablePath.Contains("Statusword"));
        Assert.Contains(plan.Links, link => link.PlcVariablePath.EndsWith("MAIN.stTi5Out.nControlword") && link.EtherCatVariablePath.Contains("Controlword"));
        Assert.Equal(0x00522227, plan.VendorId);
        Assert.Equal(0x00009253, plan.ProductCode);
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
              - name: PositionStep1Deg
                target_position_deg: 1
                duration_s: 2.5
                sample_rate_hz: 100
              - name: PositionStep5Deg
                target_position_deg: 5
                duration_s: 3
                sample_rate_hz: 100
            """);

        var config = StationConfigLoader.Load(station);

        Assert.Equal("127.0.0.1.1.1", config.Ads.AmsNetId);
        Assert.Equal("MAIN.stJointBench", config.Ads.SymbolPrefix);
        Assert.Equal(2, config.Tests.Count);
        Assert.True(config.MotionAllowed);
    }
}
