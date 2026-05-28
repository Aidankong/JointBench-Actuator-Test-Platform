using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JointBench.TwinCat;

public interface IClock
{
    DateTimeOffset NowLocal();
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset NowLocal() => DateTimeOffset.Now;
}

public sealed class FixedClock(DateTimeOffset value) : IClock
{
    public DateTimeOffset NowLocal() => value;
}

public sealed class TestReportWriter
{
    private static readonly string[] CsvFields =
    [
        "test_id",
        "sample_index",
        "timestamp_s",
        "target_position_deg",
        "actual_position_deg",
        "actual_speed_dps",
        "current_a",
        "voltage_v",
        "temperature_c",
        "fault_code",
        "enabled",
        "control_mode",
        "protocol",
        "statusword",
        "controlword",
        "command_sequence",
        "watchdog_ok",
        "following_error_deg",
    ];

    public TestReportWriter(IClock? clock = null)
    {
        Clock = clock ?? new SystemClock();
    }

    public IClock Clock { get; }

    public TestOutputArtifacts Write(ProductionSequenceResult result)
    {
        Directory.CreateDirectory(result.OutputDirectory);
        var raw = Path.Combine(result.OutputDirectory, "raw_data.csv");
        var events = Path.Combine(result.OutputDirectory, "events.log");
        var snapshot = Path.Combine(result.OutputDirectory, "config_snapshot.yaml");
        var markdown = Path.Combine(result.OutputDirectory, "report.md");
        var html = Path.Combine(result.OutputDirectory, "report.html");

        WriteCsv(raw, result);
        File.WriteAllText(events, string.Join(Environment.NewLine, result.Events) + Environment.NewLine, Encoding.UTF8);
        File.WriteAllText(snapshot, SnapshotYaml(result), Encoding.UTF8);
        File.WriteAllText(markdown, Markdown(result), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(html, Html(result), Encoding.UTF8);

        return new TestOutputArtifacts(raw, events, snapshot, markdown, html);
    }

    private static void WriteCsv(string path, ProductionSequenceResult result)
    {
        var lines = new List<string> { string.Join(",", CsvFields) };
        for (var index = 0; index < result.Samples.Count; index++)
        {
            var row = result.Samples[index].ToCsvRow(result.TestId, index);
            lines.Add(string.Join(",", CsvFields.Select(field => CsvEscape(row[field]))));
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string SnapshotYaml(ProductionSequenceResult result)
    {
        var json = JsonSerializer.Serialize(result.ConfigSnapshot, new JsonSerializerOptions { WriteIndented = true });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var builder = new StringBuilder();
        builder.AppendLine($"config_hash: {hash}");
        builder.AppendLine($"ams_net_id: {result.ConfigSnapshot.Ads.AmsNetId}");
        builder.AppendLine($"ams_port: {result.ConfigSnapshot.Ads.Port}");
        builder.AppendLine($"symbol_prefix: {result.ConfigSnapshot.Ads.SymbolPrefix}");
        builder.AppendLine("safety:");
        builder.AppendLine($"  min_position_deg: {Format(result.ConfigSnapshot.Safety.MinPositionDegrees)}");
        builder.AppendLine($"  max_position_deg: {Format(result.ConfigSnapshot.Safety.MaxPositionDegrees)}");
        builder.AppendLine($"  max_current_a: {Format(result.ConfigSnapshot.Safety.MaxCurrentA)}");
        builder.AppendLine($"  max_temperature_c: {Format(result.ConfigSnapshot.Safety.MaxTemperatureC)}");
        builder.AppendLine($"  max_following_error_deg: {Format(result.ConfigSnapshot.Safety.MaxFollowingErrorDegrees)}");
        builder.AppendLine("scaling:");
        builder.AppendLine($"  encoder_counts_per_rev: {result.ConfigSnapshot.Scaling.EncoderCountsPerRev}");
        builder.AppendLine($"  gear_ratio: {Format(result.ConfigSnapshot.Scaling.GearRatio)}");
        builder.AppendLine($"  position_direction: {result.ConfigSnapshot.Scaling.PositionDirection}");
        builder.AppendLine($"  zero_offset_deg: {Format(result.ConfigSnapshot.Scaling.ZeroOffsetDegrees)}");
        builder.AppendLine($"  auto_zero_on_check: {result.ConfigSnapshot.Scaling.AutoZeroOnCheck.ToString().ToLowerInvariant()}");
        builder.AppendLine("tests:");
        foreach (var test in result.ConfigSnapshot.Tests)
        {
            builder.AppendLine($"  - name: {test.Name}");
            builder.AppendLine($"    target_position_deg: {Format(test.TargetPositionDegrees)}");
            builder.AppendLine($"    duration_s: {Format(test.DurationSeconds)}");
            builder.AppendLine($"    sample_rate_hz: {Format(test.SampleRateHz)}");
        }

        return builder.ToString();
    }

    private string Markdown(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var title = zh ? "JointBench 测试报告" : "JointBench Test Report";
        var stage = zh ? "阶段结果" : "Stage Results";
        var artifacts = zh ? "输出文件" : "Artifacts";
        var config = zh ? "配置快照" : "Configuration Snapshot";
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine(zh ? "## 摘要" : "## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Test ID: {result.TestId}");
        builder.AppendLine($"- Result: **{result.OverallResult}**");
        builder.AppendLine($"- Generated At: {Clock.NowLocal():yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"- Device: {result.Device.DeviceId}");
        builder.AppendLine($"- AMS Net ID: {result.Device.AmsNetId}");
        builder.AppendLine($"- AMS Port: {result.Device.AmsPort}");
        builder.AppendLine($"- ADS Symbol Prefix: {result.Device.AdsSymbolPrefix}");
        builder.AppendLine($"- Vendor ID: 0x{result.Device.VendorId:X8}");
        builder.AppendLine($"- Product Code: 0x{result.Device.ProductCode:X8}");
        builder.AppendLine($"- Revision: 0x{result.Device.RevisionNumber:X8}");
        builder.AppendLine();
        builder.AppendLine($"## {stage}");
        builder.AppendLine();
        builder.AppendLine("| Stage | Result | Reasons |");
        builder.AppendLine("|---|---|---|");
        foreach (var item in result.StageResults)
        {
            builder.AppendLine($"| {item.StageName} | {item.Result} | {string.Join("; ", item.FailureReasons)} |");
        }

        builder.AppendLine();
        builder.AppendLine($"## {artifacts}");
        builder.AppendLine();
        builder.AppendLine("- `raw_data.csv`");
        builder.AppendLine("- `events.log`");
        builder.AppendLine("- `config_snapshot.yaml`");
        builder.AppendLine("- `report.md`");
        builder.AppendLine("- `report.html`");
        builder.AppendLine();
        builder.AppendLine($"## {config}");
        builder.AppendLine();
        builder.AppendLine($"- Tests: {string.Join(", ", result.ConfigSnapshot.Tests.Select(test => $"{test.Name} {test.TargetPositionDegrees}deg"))}");
        return builder.ToString();
    }

    private string Html(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var title = zh ? "JointBench 测试报告" : "JointBench Test Report";
        var config = zh ? "配置快照" : "Configuration Snapshot";
        var rows = string.Join(
            Environment.NewLine,
            result.StageResults.Select(stage =>
                $"<tr><td>{WebUtility.HtmlEncode(stage.StageName)}</td><td>{WebUtility.HtmlEncode(stage.Result)}</td><td>{WebUtility.HtmlEncode(string.Join("; ", stage.FailureReasons))}</td></tr>"));
        return $$"""
            <!doctype html>
            <html lang="{{(zh ? "zh-CN" : "en")}}">
            <head>
              <meta charset="utf-8">
              <title>{{WebUtility.HtmlEncode(title)}} {{WebUtility.HtmlEncode(result.TestId)}}</title>
              <style>
                body { font-family: "Segoe UI", Arial, sans-serif; margin: 32px; color: #1f2933; background: #f7f8fa; }
                main { max-width: 980px; margin: 0 auto; background: #fff; border: 1px solid #dde2e8; padding: 28px; }
                table { border-collapse: collapse; width: 100%; margin: 12px 0 24px; }
                th, td { border: 1px solid #d7dde4; padding: 8px 10px; text-align: left; }
                th { background: #eef2f6; }
              </style>
            </head>
            <body>
            <main>
              <h1>{{WebUtility.HtmlEncode(title)}}</h1>
              <p><strong>{{WebUtility.HtmlEncode(result.OverallResult)}}</strong></p>
              <table>
                <tr><th>Test ID</th><td>{{WebUtility.HtmlEncode(result.TestId)}}</td></tr>
                <tr><th>AMS Net ID</th><td>{{WebUtility.HtmlEncode(result.Device.AmsNetId)}}</td></tr>
                <tr><th>ADS Symbol Prefix</th><td>{{WebUtility.HtmlEncode(result.Device.AdsSymbolPrefix)}}</td></tr>
              </table>
              <h2>{{(zh ? "阶段结果" : "Stage Results")}}</h2>
              <table><tr><th>Stage</th><th>Result</th><th>Reasons</th></tr>{{rows}}</table>
              <h2>{{WebUtility.HtmlEncode(config)}}</h2>
              <p>config_snapshot.yaml</p>
              <h2>{{(zh ? "输出文件" : "Artifacts")}}</h2>
              <ul>
                <li><a href="raw_data.csv">raw_data.csv</a></li>
                <li><a href="events.log">events.log</a></li>
                <li><a href="config_snapshot.yaml">config_snapshot.yaml</a></li>
              </ul>
            </main>
            </body>
            </html>
            """;
    }

    private static string CsvEscape(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
