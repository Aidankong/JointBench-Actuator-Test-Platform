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
}
