using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JointBench.TwinCat;

public sealed record StationConfig(
    AdsConnectionOptions Ads,
    SafetyLimits Safety,
    IReadOnlyList<TestConfig> Tests,
    string SymbolPrefix,
    int VendorId,
    int ProductCode,
    int RevisionNumber)
{
    public bool MotionAllowed =>
        !string.IsNullOrWhiteSpace(Ads.AmsNetId) &&
        Safety.MinPositionDegrees <= -1.0 &&
        Safety.MaxPositionDegrees >= 5.0 &&
        Tests.Any(test => Math.Abs(test.TargetPositionDegrees - 1.0) < 1e-9) &&
        Tests.Any(test => Math.Abs(test.TargetPositionDegrees - 5.0) < 1e-9);
}

public static class StationConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static StationConfig Load(string stationDirectory)
    {
        var root = new DirectoryInfo(stationDirectory);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Station directory not found: {stationDirectory}");
        }

        var bus = LoadYaml(Path.Combine(root.FullName, "bus.yaml"));
        var device = LoadYaml(Path.Combine(root.FullName, "device.yaml"));
        var safety = LoadYaml(Path.Combine(root.FullName, "safety.yaml"));
        var tests = LoadYaml(Path.Combine(root.FullName, "tests.yaml"));

        var adsNode = Map(bus, "ads");
        var deviceNode = Map(device, "device");
        var deviceAdsNode = Map(device, "ads");
        var limitsNode = Map(safety, "limits");
        var ads = new AdsConnectionOptions(
            String(adsNode, "ams_net_id", "127.0.0.1.1.1"),
            Int(adsNode, "ams_port", 851),
            String(deviceAdsNode, "symbol_prefix", "MAIN.stJointBench"));
        var safetyLimits = new SafetyLimits(
            Double(limitsNode, "min_position_deg", -6.0),
            Double(limitsNode, "max_position_deg", 6.0),
            Double(limitsNode, "max_current_a", 3.0),
            Double(limitsNode, "max_temperature_c", 60.0),
            Double(limitsNode, "max_following_error_deg", 2.0),
            Int(limitsNode, "communication_timeout_ms", 500));
        var testConfigs = ReadTests(tests, safetyLimits);

        return new StationConfig(
            ads,
            safetyLimits,
            testConfigs,
            ads.SymbolPrefix,
            Int(deviceNode, "vendor_id", 0x00522227),
            Int(deviceNode, "product_code", 0x00009253),
            Int(deviceNode, "revision_number", 0x00010005));
    }

    private static IReadOnlyList<TestConfig> ReadTests(IReadOnlyDictionary<object, object?> root, SafetyLimits safety)
    {
        var tests = root.TryGetValue("tests", out var rawTests) && rawTests is IEnumerable<object> list
            ? list.OfType<IReadOnlyDictionary<object, object?>>()
            : [];
        var result = new List<TestConfig>();
        foreach (var test in tests)
        {
            var target = Double(test, "target_position_deg", 1.0);
            result.Add(new TestConfig(
                String(test, "name", Math.Abs(target) <= 1.0 ? "PositionStep1Deg" : "PositionStep5Deg"),
                Double(test, "start_position_deg", 0.0),
                target,
                Double(test, "duration_s", Math.Abs(target) <= 1.0 ? 2.5 : 3.0),
                Double(test, "sample_rate_hz", 100.0),
                Double(test, "settling_band_pct", 2.0),
                Math.Max(Math.Abs(safety.MinPositionDegrees), Math.Abs(safety.MaxPositionDegrees)),
                safety.MaxCurrentA,
                safety.MaxTemperatureC,
                safety.MaxFollowingErrorDegrees,
                Double(test, "max_overshoot_pct", 10.0),
                Double(test, "max_settling_time_s", Math.Abs(target) <= 1.0 ? 1.0 : 1.2),
                Double(test, "max_steady_state_error_deg", Math.Abs(target) <= 1.0 ? 0.2 : 0.5)));
        }

        return result.Count > 0 ? result : [TestConfig.ForTarget(1.0), TestConfig.ForTarget(5.0, 3.0)];
    }

    private static IReadOnlyDictionary<object, object?> LoadYaml(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<object, object?>();
        }

        using var reader = File.OpenText(path);
        return Deserializer.Deserialize<Dictionary<object, object?>>(reader) ?? new Dictionary<object, object?>();
    }

    private static IReadOnlyDictionary<object, object?> Map(IReadOnlyDictionary<object, object?> root, string key) =>
        root.TryGetValue(key, out var value) && value is IReadOnlyDictionary<object, object?> map
            ? map
            : new Dictionary<object, object?>();

    private static string String(IReadOnlyDictionary<object, object?> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && value is not null ? Convert.ToString(value) ?? fallback : fallback;

    private static int Int(IReadOnlyDictionary<object, object?> map, string key, int fallback)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        var text = Convert.ToString(value) ?? string.Empty;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(text, 16)
            : Convert.ToInt32(value);
    }

    private static double Double(IReadOnlyDictionary<object, object?> map, string key, double fallback) =>
        map.TryGetValue(key, out var value) && value is not null ? Convert.ToDouble(value) : fallback;
}
