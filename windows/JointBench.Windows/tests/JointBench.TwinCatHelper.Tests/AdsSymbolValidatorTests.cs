using JointBench.TwinCat;

namespace JointBench.TwinCatHelper.Tests;

public sealed class AdsSymbolValidatorTests
{
    [Fact]
    public void RequiredSymbolsIncludeWatchdogAndFollowingErrorFields()
    {
        var names = AdsSymbolValidator.RequiredSymbols.Select(symbol => symbol.Name).ToHashSet();

        Assert.Contains("nCommandSequence", names);
        Assert.Contains("bWatchdogOk", names);
        Assert.Contains("fFollowingErrorDeg", names);
    }

    [Fact]
    public void Ti5IdentityMatchesExpectedEsiValues()
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

        Assert.True(box.IsTi5);
    }
}
