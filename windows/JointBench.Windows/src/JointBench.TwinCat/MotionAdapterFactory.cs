namespace JointBench.TwinCat;

public static class MotionAdapterFactory
{
    public static IMotionAdapter Create(StationConfig station, bool fake = false)
    {
        var maxTargetAbsDegrees = Math.Max(Math.Abs(station.Safety.MinPositionDegrees), Math.Abs(station.Safety.MaxPositionDegrees));
        if (string.Equals(station.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase))
        {
            var hardStone = station.HardStone ?? throw new InvalidOperationException("HardStone station options are missing.");
            IHardStoneDebugTransport transport = fake
                ? new FakeHardStoneDebugTransport()
                : new HardStoneOpenOcdTransport(hardStone.AdapterSpeedKHz);
            return new HardStoneDebugMotionAdapter(
                transport,
                new HardStoneDebugOptions(
                    hardStone.FirmwareElfPath,
                    hardStone.CountsPerDegree,
                    maxTargetAbsDegrees));
        }

        IAdsSymbolClient client = fake ? new FakeAdsSymbolClient() : new BeckhoffAdsSymbolClient();
        return new AdsMotionAdapter(
            client,
            station.Ads,
            maxTargetAbsDegrees: maxTargetAbsDegrees);
    }
}
