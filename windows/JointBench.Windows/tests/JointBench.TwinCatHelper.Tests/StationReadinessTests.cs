using JointBench.TwinCat;

namespace JointBench.TwinCatHelper.Tests;

public sealed class StationReadinessTests
{
    [Fact]
    public void AppStateStoreRemembersLastEsiPath()
    {
        var root = Directory.CreateTempSubdirectory("jointbench-state-test-").FullName;
        try
        {
            var statePath = Path.Combine(root, "state.json");
            var store = new JointBenchAppStateStore(statePath);

            store.Save(new JointBenchAppState(LastEsiPath: @"C:\esi\Ti5.xml"));

            Assert.Equal(@"C:\esi\Ti5.xml", new JointBenchAppStateStore(statePath).Load().LastEsiPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EsiAutoImportInstallsAndRemembersSelectedEsi()
    {
        using var fixture = EsiFixture.Create();
        var statePath = Path.Combine(fixture.Root, "state.json");
        var target = Path.Combine(fixture.Root, "target");
        Directory.CreateDirectory(target);
        var importer = new EsiAutoImportService(new EsiService(), new JointBenchAppStateStore(statePath));

        var result = importer.ImportAndRemember(fixture.SourcePath, target);

        Assert.True(File.Exists(result.TargetPath));
        Assert.Equal(fixture.SourcePath, new JointBenchAppStateStore(statePath).Load().LastEsiPath);
    }

    [Fact]
    public void EsiAutoImportUsesLastRememberedPath()
    {
        using var fixture = EsiFixture.Create();
        var statePath = Path.Combine(fixture.Root, "state.json");
        var target = Path.Combine(fixture.Root, "target");
        Directory.CreateDirectory(target);
        var store = new JointBenchAppStateStore(statePath);
        store.Save(new JointBenchAppState(fixture.SourcePath));
        var importer = new EsiAutoImportService(new EsiService(), store);

        var report = importer.ImportLastUsed(target);

        Assert.True(report.Attempted);
        Assert.True(report.Ok);
        Assert.NotNull(report.InstallResult);
        Assert.True(File.Exists(report.InstallResult.TargetPath));
    }

    [Fact]
    public void StationReadinessCombinesPreflightEsiScanAndAds()
    {
        var station = CreateStation();
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
            "Ti5Robot_JointMotor_2.0.xml",
            "");
        var scan = new EtherCatScanReport(true, "", "", null, [], [box], Ti5Found: true);
        var linkPlan = TwinCatPdoLinkPlanner.BuildTi5Plan(box);
        var service = new StationReadinessService(
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            autoImportEsi: () => new EsiAutoImportReport(false, true, "No last ESI file has been selected.", null, null),
            prepareTwinCat: _ => new TwinCatPreparationReport(true, false, "dry run", scan, linkPlan),
            applyAdsRuntimeConfig: _ => AdsRuntimeConfigurationReport.Applied("ok"),
            checkAdsSymbols: options => new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, true, []),
            checkAdsRuntimeState: options => AdsRuntimeStateReport.Healthy(options));

        var report = service.Check(station);

        Assert.True(report.Ready);
        Assert.Contains(report.Checks, check => check.Name == "twincat-prepare" && check.Status == "ok");
        Assert.Contains(report.Checks, check => check.Name == "ads-symbols" && check.Status == "ok");
    }

    [Fact]
    public void StationReadinessAcceptsActiveTi5ConfigWhenOnlineScanIsUnavailable()
    {
        var station = CreateStation();
        var scan = new EtherCatScanReport(
            false,
            "No EtherCAT master was found.",
            "",
            null,
            [],
            [],
            Ti5Found: false);
        var service = new StationReadinessService(
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            autoImportEsi: () => new EsiAutoImportReport(false, true, "No last ESI file has been selected.", null, null),
            prepareTwinCat: _ => new TwinCatPreparationReport(false, false, "No EtherCAT master was found.", scan, null),
            applyAdsRuntimeConfig: _ => AdsRuntimeConfigurationReport.Applied("ok"),
            checkAdsSymbols: options => new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, true, []),
            checkAdsRuntimeState: options => AdsRuntimeStateReport.Healthy(options),
            inspectActiveConfig: () => new ActiveTwinCatConfigReport(true, true, "Active TwinCAT configuration contains Ti5.", @"C:\TwinCAT\3.1\Boot\CurrentConfig.tszip"));

        var report = service.Check(station);

        Assert.True(report.Ready);
        Assert.Contains(report.Checks, check => check.Name == "twincat-active-config" && check.Status == "ok");
        Assert.Contains(report.Checks, check => check.Name == "ti5-scan" && check.Status == "ok");
    }

    [Fact]
    public void StationReadinessFailsWhenRuntimeStatuswordIsZero()
    {
        var station = CreateStation();
        var scan = new EtherCatScanReport(false, "No EtherCAT master was found.", "", null, [], [], Ti5Found: false);
        var service = new StationReadinessService(
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            autoImportEsi: () => new EsiAutoImportReport(false, true, "No last ESI file has been selected.", null, null),
            prepareTwinCat: _ => new TwinCatPreparationReport(false, false, "No EtherCAT master was found.", scan, null),
            applyAdsRuntimeConfig: _ => AdsRuntimeConfigurationReport.Applied("ok"),
            checkAdsSymbols: options => new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, true, []),
            checkAdsRuntimeState: options => AdsRuntimeStateReport.FromState(
                options,
                new ActuatorState(
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    24.0,
                    0.0,
                    Statusword: 0,
                    Controlword: 0,
                    WatchdogOk: true,
                    FollowingErrorDegrees: 0.0)),
            inspectActiveConfig: () => new ActiveTwinCatConfigReport(true, true, "Active TwinCAT configuration contains Ti5.", @"C:\TwinCAT\3.1\Boot\CurrentConfig.tszip"));

        var report = service.Check(station);

        Assert.False(report.Ready);
        Assert.Contains(report.Checks, check => check.Name == "drive-state" && check.Status == "error");
    }

    [Fact]
    public void StationReadinessAppliesRuntimeConfigBeforeDriveStateCheck()
    {
        var station = CreateStation();
        var scan = new EtherCatScanReport(false, "No EtherCAT master was found.", "", null, [], [], Ti5Found: false);
        var calls = new List<string>();
        var service = new StationReadinessService(
            preflight: () => new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]),
            autoImportEsi: () => new EsiAutoImportReport(false, true, "No last ESI file has been selected.", null, null),
            prepareTwinCat: _ => new TwinCatPreparationReport(false, false, "No EtherCAT master was found.", scan, null),
            applyAdsRuntimeConfig: _ =>
            {
                calls.Add("config");
                return AdsRuntimeConfigurationReport.Applied("runtime config applied");
            },
            checkAdsSymbols: options => new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, true, []),
            checkAdsRuntimeState: options =>
            {
                calls.Add("state");
                return AdsRuntimeStateReport.Healthy(options);
            },
            inspectActiveConfig: () => new ActiveTwinCatConfigReport(true, true, "Active TwinCAT configuration contains Ti5.", @"C:\TwinCAT\3.1\Boot\CurrentConfig.tszip"));

        var report = service.Check(station);

        Assert.True(report.Ready);
        Assert.Equal(["config", "state"], calls);
        Assert.Contains(report.Checks, check => check.Name == "runtime-config" && check.Status == "ok");
    }

    [Fact]
    public void ProductionGateAcceptsActiveConfigTi5WhenEngineeringScanIsUnavailable()
    {
        var report = ReadyReportWithActiveConfigFallback();

        var gate = ProductionGateState.FromReadiness(report);

        Assert.True(gate.EnvironmentOk);
        Assert.True(gate.Ti5Ready);
        Assert.True(gate.AdsOk);
        Assert.True(gate.ReadyForMotion);
    }

    [Fact]
    public void EngineeringScanFailureDoesNotClearProductionReadiness()
    {
        var gate = ProductionGateState.FromReadiness(ReadyReportWithActiveConfigFallback());
        var failedEngineeringScan = new EtherCatScanReport(
            false,
            "No EtherCAT master was found.",
            @"C:\Temp\jointbench-scan",
            null,
            [],
            [],
            Ti5Found: false);

        gate = gate.WithEngineeringScan(failedEngineeringScan);

        Assert.True(gate.ReadyForMotion);
    }

    [Fact]
    public void ActiveConfigProbeFindsTi5InCurrentConfigArchive()
    {
        var root = Directory.CreateTempSubdirectory("jointbench-active-config-").FullName;
        var archivePath = Path.Combine(root, "CurrentConfig.tszip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("JointBenchProjectProbe.tsproj");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("""
                    <Project>
                      <EtherCAT VendorId="#x00522227" ProductCode="#x00009253" RevisionNo="#x00010005" Type="Ti5Robot_JointMotor" />
                    </Project>
                    """);
            }

            var report = TwinCatActiveConfigProbe.InspectArchive(archivePath);

            Assert.True(report.Ok);
            Assert.True(report.Ti5Found);
            Assert.Equal(archivePath, report.SourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateStation()
    {
        var station = Directory.CreateTempSubdirectory("jointbench-station-ready-").FullName;
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

    private static StationReadinessReport ReadyReportWithActiveConfigFallback()
    {
        var activeConfigScan = new EtherCatScanReport(
            false,
            "No EtherCAT master was found.",
            "",
            null,
            [],
            [],
            Ti5Found: false);
        var preflight = new PreflightReport(DateTimeOffset.UtcNow, [new CheckItem("windows", "ok", "ok")]);
        var ads = new AdsSymbolCheckReport("127.0.0.1.1.1", 851, "MAIN.stJointBench", true, []);
        var preparation = new TwinCatPreparationReport(false, false, "No EtherCAT master was found.", activeConfigScan, null);
        var checks = new[]
        {
            new CheckItem("station-config", "ok", "Station config is motion-ready."),
            new CheckItem("preflight", "ok", "Prerequisites passed."),
            new CheckItem("esi-auto-import", "ok", "Last ESI file imported."),
            new CheckItem("twincat-active-config", "ok", "Active TwinCAT configuration contains Ti5.", @"C:\TwinCAT\3.1\Boot\CurrentConfig.tszip"),
            new CheckItem("twincat-prepare", "ok", "Active TwinCAT configuration is already prepared with Ti5."),
            new CheckItem("ti5-scan", "ok", "Ti5 found in active TwinCAT configuration."),
            new CheckItem("ads-symbols", "ok", "ADS symbols are available.", "127.0.0.1.1.1:851 MAIN.stJointBench"),
        };

        return new StationReadinessReport(
            DateTimeOffset.UtcNow,
            Ready: true,
            "Station readiness checks passed.",
            checks,
            preflight,
            new EsiAutoImportReport(true, true, "Last ESI file imported.", null, @"C:\Ti5.xml"),
            preparation,
            ads);
    }

    private sealed class EsiFixture : IDisposable
    {
        private EsiFixture(string root, string sourcePath)
        {
            Root = root;
            SourcePath = sourcePath;
        }

        public string Root { get; }

        public string SourcePath { get; }

        public static EsiFixture Create()
        {
            var root = Directory.CreateTempSubdirectory("jointbench-esi-auto-test-").FullName;
            var sourcePath = Path.Combine(root, "Ti5Robot_JointMotor_2.0.xml");
            File.WriteAllText(
                sourcePath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <EtherCATInfo>
                  <Vendor>
                    <Id>#x00522227</Id>
                    <Name>Ti5Robot</Name>
                  </Vendor>
                  <Descriptions>
                    <Devices>
                      <Device>
                        <Type ProductCode="#x00009253" RevisionNo="#x00010005">Ti5Robot_JointMotor</Type>
                      </Device>
                    </Devices>
                  </Descriptions>
                </EtherCATInfo>
                """);
            return new EsiFixture(root, sourcePath);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
