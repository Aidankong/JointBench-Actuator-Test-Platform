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
            checkAdsSymbols: options => new AdsSymbolCheckReport(options.AmsNetId, options.Port, options.SymbolPrefix, true, []));

        var report = service.Check(station);

        Assert.True(report.Ready);
        Assert.Contains(report.Checks, check => check.Name == "twincat-prepare" && check.Status == "ok");
        Assert.Contains(report.Checks, check => check.Name == "ads-symbols" && check.Status == "ok");
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
