namespace JointBench.TwinCat;

public sealed record AdsRuntimeConfigurationReport(bool Ok, string Message, string Detail)
{
    public static AdsRuntimeConfigurationReport Applied(string detail) =>
        new(true, "PLC runtime configuration applied.", detail);

    public static AdsRuntimeConfigurationReport Failed(string detail) =>
        new(false, "PLC runtime configuration could not be applied.", detail);
}

public sealed class AdsRuntimeConfigurator
{
    private readonly Func<IAdsSymbolClient> clientFactory;
    private readonly TimeSpan resetPulseDuration;

    public AdsRuntimeConfigurator()
        : this(() => new BeckhoffAdsSymbolClient(), TimeSpan.FromMilliseconds(60))
    {
    }

    public AdsRuntimeConfigurator(Func<IAdsSymbolClient> clientFactory, TimeSpan resetPulseDuration)
    {
        this.clientFactory = clientFactory;
        this.resetPulseDuration = resetPulseDuration;
    }

    public async Task<AdsRuntimeConfigurationReport> ApplyAsync(StationConfig config, CancellationToken cancellationToken)
    {
        try
        {
            using var client = clientFactory();
            await client.ConnectAsync(config.Ads, cancellationToken);
            var root = PlcRoot(config.Ads.SymbolPrefix);

            await WriteAsync(client, root, "fTi5MinPositionDeg", config.Safety.MinPositionDegrees, cancellationToken);
            await WriteAsync(client, root, "fTi5MaxPositionDeg", config.Safety.MaxPositionDegrees, cancellationToken);
            await WriteAsync(client, root, "fTi5MaxVelocityDps", config.Safety.MaxSpeedDps, cancellationToken);
            await WriteAsync(client, root, "fTi5MaxCurrentA", config.Safety.MaxCurrentA, cancellationToken);
            await WriteAsync(client, root, "fTi5MaxTemperatureC", config.Safety.MaxTemperatureC, cancellationToken);
            await WriteAsync(client, root, "fTi5MaxFollowingErrorDeg", config.Safety.MaxFollowingErrorDegrees, cancellationToken);
            await WriteAsync(client, root, "nTi5EncoderCountsPerRev", config.Scaling.EncoderCountsPerRev, cancellationToken);
            await WriteAsync(client, root, "fTi5GearRatio", config.Scaling.GearRatio, cancellationToken);
            await WriteAsync(client, root, "nTi5PositionDirection", config.Scaling.PositionDirection, cancellationToken);
            await WriteAsync(client, root, "fTi5CurrentScaleAPerUnit", config.Scaling.CurrentScaleAPerUnit, cancellationToken);
            await WriteAsync(client, root, "fTi5TemperatureScaleCPerUnit", config.Scaling.TemperatureScaleCPerUnit, cancellationToken);

            var zeroOffset = config.Scaling.ZeroOffsetDegrees;
            if (config.Scaling.AutoZeroOnCheck)
            {
                zeroOffset -= await ReadRawPositionDegreesAsync(client, root, config.Scaling, cancellationToken);
            }

            await WriteAsync(client, root, "fTi5ZeroOffsetDeg", zeroOffset, cancellationToken);
            await PulseResetFaultAsync(client, config.Ads.SymbolPrefix, cancellationToken);
            var detail = config.Scaling.AutoZeroOnCheck
                ? $"auto-zero applied; zero_offset_deg={zeroOffset:F6}"
                : $"zero_offset_deg={zeroOffset:F6}";
            return AdsRuntimeConfigurationReport.Applied(detail);
        }
        catch (Exception exc)
        {
            return AdsRuntimeConfigurationReport.Failed(FormatFailure(exc));
        }
    }

    private async Task PulseResetFaultAsync(IAdsSymbolClient client, string symbolPrefix, CancellationToken cancellationToken)
    {
        var sequence = Convert.ToInt32(await client.ReadAsync($"{symbolPrefix}.nCommandSequence", typeof(int), cancellationToken));
        await client.WriteAsync($"{symbolPrefix}.nCommandSequence", sequence + 1, cancellationToken);
        await client.WriteAsync($"{symbolPrefix}.bResetFault", true, cancellationToken);
        await Task.Delay(resetPulseDuration, cancellationToken);
        await client.WriteAsync($"{symbolPrefix}.nCommandSequence", sequence + 2, cancellationToken);
        await client.WriteAsync($"{symbolPrefix}.bResetFault", false, cancellationToken);
    }

    private static Task WriteAsync(IAdsSymbolClient client, string root, string name, object value, CancellationToken cancellationToken) =>
        client.WriteAsync($"{root}.{name}", value, cancellationToken);

    private static async Task<double> ReadRawPositionDegreesAsync(
        IAdsSymbolClient client,
        string root,
        StationScaling scaling,
        CancellationToken cancellationToken)
    {
        if (scaling.EncoderCountsPerRev <= 0 || scaling.GearRatio <= 0.0)
        {
            throw new InvalidOperationException("Station scaling is invalid; encoder_counts_per_rev and gear_ratio must be positive.");
        }

        var rawCounts = Convert.ToDouble(await client.ReadAsync($"{root}.nTi5ActualPosition", typeof(int), cancellationToken));
        var countsPerDegree = scaling.EncoderCountsPerRev * scaling.GearRatio / 360.0;
        return rawCounts / countsPerDegree * scaling.PositionDirection;
    }

    private static string PlcRoot(string symbolPrefix)
    {
        var dot = symbolPrefix.IndexOf('.');
        return dot > 0 ? symbolPrefix[..dot] : "MAIN";
    }

    private static string FormatFailure(Exception exc)
    {
        var message = exc.Message;
        if (message.Contains("Symbol could not be found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("0x710", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC runtime config symbols are missing. Run Prepare TwinCAT with Activate once to deploy the latest JointBench PLC template.";
        }

        return message;
    }
}
