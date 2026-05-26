namespace JointBench.TwinCatHelper.Tests;

public sealed class EsiServiceTests
{
    [Fact]
    public void ReadsEsiSummary()
    {
        using var fixture = EsiFixture.Create();
        var summary = new EsiService().ReadSummary(fixture.SourcePath);

        Assert.Equal("Ti5Robot", summary.VendorName);
        Assert.Equal("#x00522227", summary.VendorId);
        Assert.Equal("Ti5Robot_JointMotor", summary.DeviceType);
        Assert.Equal("#x00009253", summary.ProductCode);
        Assert.Equal("#x00010005", summary.RevisionNumber);
    }

    [Fact]
    public void InstallsEsiToTargetDirectory()
    {
        using var fixture = EsiFixture.Create();
        var target = Path.Combine(fixture.Root, "target");
        Directory.CreateDirectory(target);

        var result = new EsiService().Install(fixture.SourcePath, target);

        Assert.False(result.DryRun);
        Assert.True(File.Exists(result.TargetPath));
        Assert.Equal(Path.Combine(target, Path.GetFileName(fixture.SourcePath)), result.TargetPath);
    }

    [Fact]
    public void RejectsNonEsiXml()
    {
        var root = Directory.CreateTempSubdirectory("jointbench-esi-test-").FullName;
        try
        {
            var path = Path.Combine(root, "not-esi.xml");
            File.WriteAllText(path, "<root />");

            var exception = Assert.Throws<InvalidOperationException>(() => new EsiService().ReadSummary(path));
            Assert.Contains("EtherCAT ESI", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            var root = Directory.CreateTempSubdirectory("jointbench-esi-test-").FullName;
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
