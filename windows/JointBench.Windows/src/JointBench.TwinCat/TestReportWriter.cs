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
        "debug_command_ack",
        "debug_heartbeat_ack",
        "debug_target_relative_counts",
        "debug_target_counts",
        "debug_actual_counts",
        "mode_command",
        "mode_display",
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
        builder.AppendLine($"protocol: {result.ConfigSnapshot.Protocol}");
        if (string.Equals(result.ConfigSnapshot.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase) &&
            result.ConfigSnapshot.HardStone is { } hardStone)
        {
            builder.AppendLine("hardstone:");
            builder.AppendLine($"  firmware_elf: {hardStone.FirmwareElfPath}");
            builder.AppendLine($"  adapter_speed_khz: {hardStone.AdapterSpeedKHz}");
            builder.AppendLine($"  counts_per_degree: {Format(hardStone.CountsPerDegree)}");
        }
        else
        {
            builder.AppendLine($"ams_net_id: {result.ConfigSnapshot.Ads.AmsNetId}");
            builder.AppendLine($"ams_port: {result.ConfigSnapshot.Ads.Port}");
            builder.AppendLine($"symbol_prefix: {result.ConfigSnapshot.Ads.SymbolPrefix}");
        }
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
            builder.AppendLine($"    type: {test.MotionProfile}");
            builder.AppendLine($"    start_position_deg: {Format(test.StartPositionDegrees)}");
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
        builder.AppendLine($"- Protocol: {result.Device.Protocol}");
        builder.AppendLine($"- Transport: {result.Device.TransportMode}");
        if (!string.Equals(result.Device.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- AMS Net ID: {result.Device.AmsNetId}");
            builder.AppendLine($"- AMS Port: {result.Device.AmsPort}");
            builder.AppendLine($"- ADS Symbol Prefix: {result.Device.AdsSymbolPrefix}");
        }
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
        builder.Append(MotionSummaryMarkdown(result));
        builder.AppendLine();
        builder.Append(PreRunStateMarkdown(result));
        builder.AppendLine();
        builder.Append(PreRunChecksMarkdown(result));
        builder.AppendLine();
        builder.Append(FinalStateMarkdown(result));
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
        var finalStateHeading = zh ? "最终状态" : "Final State";
        var finalStateRows = string.Join(
            Environment.NewLine,
            FinalStateRows(result).Select(row =>
                $"<tr><th>{WebUtility.HtmlEncode(row.Label)}</th><td>{WebUtility.HtmlEncode(row.Value)}</td></tr>"));
        var preRunHeading = zh ? "预运行检查" : "Pre-run Checks";
        var preRunRows = string.Join(
            Environment.NewLine,
            PreRunCheckRows(result).Select(row =>
                $"<tr><td>{WebUtility.HtmlEncode(row.Name)}</td><td>{WebUtility.HtmlEncode(row.Status)}</td><td>{WebUtility.HtmlEncode(row.Message)}</td><td>{WebUtility.HtmlEncode(row.Detail)}</td></tr>"));
        var motionSummaryHeading = zh ? "运动摘要" : "Motion Summary";
        var motionSummaryRows = string.Join(
            Environment.NewLine,
            MotionSummaryRows(result).Select(row =>
                $"<tr><th>{WebUtility.HtmlEncode(row.Label)}</th><td>{WebUtility.HtmlEncode(row.Value)}</td></tr>"));
        var preRunStateHeading = zh ? "运动前状态" : "Pre-run State";
        var preRunStateRows = string.Join(
            Environment.NewLine,
            PreRunStateRows(result).Select(row =>
                $"<tr><th>{WebUtility.HtmlEncode(row.Label)}</th><td>{WebUtility.HtmlEncode(row.Value)}</td></tr>"));
        var connectionRows = string.Equals(result.Device.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase)
            ? $"""
                <tr><th>Protocol</th><td>{WebUtility.HtmlEncode(result.Device.Protocol)}</td></tr>
                <tr><th>Transport</th><td>{WebUtility.HtmlEncode(result.Device.TransportMode)}</td></tr>
                """
            : $"""
                <tr><th>AMS Net ID</th><td>{WebUtility.HtmlEncode(result.Device.AmsNetId)}</td></tr>
                <tr><th>ADS Symbol Prefix</th><td>{WebUtility.HtmlEncode(result.Device.AdsSymbolPrefix)}</td></tr>
                """;
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
                {{connectionRows}}
              </table>
              <h2>{{(zh ? "阶段结果" : "Stage Results")}}</h2>
              <table><tr><th>Stage</th><th>Result</th><th>Reasons</th></tr>{{rows}}</table>
              <h2>{{WebUtility.HtmlEncode(motionSummaryHeading)}}</h2>
              <table>{{motionSummaryRows}}</table>
              <h2>{{WebUtility.HtmlEncode(preRunStateHeading)}}</h2>
              <table>{{preRunStateRows}}</table>
              <h2>{{WebUtility.HtmlEncode(preRunHeading)}}</h2>
              <table><tr><th>Name</th><th>Status</th><th>Message</th><th>Detail</th></tr>{{preRunRows}}</table>
              <h2>{{WebUtility.HtmlEncode(finalStateHeading)}}</h2>
              <table>{{finalStateRows}}</table>
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

    private static string MotionSummaryMarkdown(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var heading = zh ? "运动摘要" : "Motion Summary";
        var builder = new StringBuilder();
        builder.AppendLine($"## {heading}");
        builder.AppendLine();
        foreach (var row in MotionSummaryRows(result))
        {
            builder.AppendLine($"- {row.Label}: {row.Value}");
        }

        return builder.ToString();
    }

    private static string PreRunStateMarkdown(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var heading = zh ? "运动前状态" : "Pre-run State";
        var builder = new StringBuilder();
        builder.AppendLine($"## {heading}");
        builder.AppendLine();
        foreach (var row in PreRunStateRows(result))
        {
            builder.AppendLine($"- {row.Label}: {row.Value}");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<(string Label, string Value)> PreRunStateRows(ProductionSequenceResult result)
    {
        var state = result.PreRunState;
        if (state is null)
        {
            return [("Captured", "False")];
        }

        return
        [
            ("Captured", "True"),
            ("Message", state.Message),
            ("Slave Index", state.Ti5SlaveIndex.ToString(CultureInfo.InvariantCulture)),
            ("EtherCAT OP", state.EtherCatOperational.ToString(CultureInfo.InvariantCulture)),
            ("Enabled", state.Enabled.ToString()),
            ("Watchdog OK", state.WatchdogOk.ToString()),
            ("Error", state.CommandError.ToString(CultureInfo.InvariantCulture)),
            ("Statusword", FormatHex(state.Statusword)),
            ("Controlword", FormatHex(state.Controlword)),
            ("Mode Command", state.ModeOfOperationCommand.ToString(CultureInfo.InvariantCulture)),
            ("Mode Display", state.ModeOfOperationDisplay.ToString(CultureInfo.InvariantCulture)),
            ("Actual Position", $"{Format(state.ActualPositionDegrees)} deg"),
            ("Target Position", $"{Format(state.TargetPositionDegrees)} deg"),
            ("Following Error", $"{Format(state.FollowingErrorDegrees)} deg"),
            ("Actual Counts", state.ActualPositionCounts.ToString(CultureInfo.InvariantCulture)),
            ("Target Counts", state.TargetPositionCounts.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    private static IReadOnlyList<(string Label, string Value)> MotionSummaryRows(ProductionSequenceResult result)
    {
        if (result.Samples.Count == 0)
        {
            return
            [
                ("Sample Count", "0"),
                ("Actual Position Range", "n/a"),
                ("Actual Travel", "n/a"),
                ("Final Target", "n/a"),
                ("Final Actual", "n/a"),
            ];
        }

        var minActual = result.Samples.Min(sample => sample.ActualPositionDegrees);
        var maxActual = result.Samples.Max(sample => sample.ActualPositionDegrees);
        var final = result.Samples[^1];
        return
        [
            ("Sample Count", result.Samples.Count.ToString(CultureInfo.InvariantCulture)),
            ("Actual Position Range", $"{Format(minActual)}..{Format(maxActual)} deg"),
            ("Actual Travel", $"{Format(maxActual - minActual)} deg"),
            ("Final Target", $"{Format(final.TargetPositionDegrees)} deg"),
            ("Final Actual", $"{Format(final.ActualPositionDegrees)} deg"),
        ];
    }

    private static string PreRunChecksMarkdown(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var heading = zh ? "预运行检查" : "Pre-run Checks";
        var builder = new StringBuilder();
        builder.AppendLine($"## {heading}");
        builder.AppendLine();
        builder.AppendLine("| Name | Status | Message | Detail |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var row in PreRunCheckRows(result))
        {
            builder.AppendLine($"| {row.Name} | {row.Status} | {row.Message} | {row.Detail} |");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<(string Name, string Status, string Message, string Detail)> PreRunCheckRows(ProductionSequenceResult result)
    {
        if (result.PreRunChecks.Count == 0)
        {
            return [("n/a", "n/a", "No pre-run checks were attached to this report.", string.Empty)];
        }

        return result.PreRunChecks
            .Select(check => (check.Name, check.Status, check.Message, check.Detail ?? string.Empty))
            .ToList();
    }

    private static string FinalStateMarkdown(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var heading = zh ? "最终状态" : "Final State";
        var builder = new StringBuilder();
        builder.AppendLine($"## {heading}");
        builder.AppendLine();
        foreach (var row in FinalStateRows(result))
        {
            builder.AppendLine($"- {row.Label}: {row.Value}");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<(string Label, string Value)> FinalStateRows(ProductionSequenceResult result)
    {
        var zh = result.Language == ReportLanguage.SimplifiedChinese;
        var sample = result.Samples.LastOrDefault();
        if (sample is null)
        {
            return
            [
                (zh ? "样本" : "Samples", zh ? "未采集" : "not captured"),
            ];
        }

        return
        [
            ("Enabled", sample.Enabled.ToString()),
            ("Fault/Error Code", sample.FaultCode.ToString(CultureInfo.InvariantCulture)),
            ("Watchdog OK", FormatNullableBool(sample.WatchdogOk)),
            ("Statusword", FormatHex(sample.Statusword)),
            ("Controlword", FormatHex(sample.Controlword)),
            ("Mode Command", FormatNullableInt(sample.ModeOfOperationCommand)),
            ("Mode Display", FormatNullableInt(sample.ModeOfOperationDisplay)),
            ("Diagnosis", CiA402StateDiagnosis.Describe(sample)),
            ("Command Sequence", FormatNullableInt(sample.CommandSequence)),
            ("Target Position", $"{Format(sample.TargetPositionDegrees)} deg"),
            ("Actual Position", $"{Format(sample.ActualPositionDegrees)} deg"),
            ("Following Error", FormatNullableDegrees(sample.FollowingErrorDegrees)),
            ("Command Ack", FormatNullableInt(sample.DebugCommandAck)),
            ("Heartbeat Ack", FormatNullableInt(sample.DebugHeartbeatAck)),
            ("Target Relative Counts", FormatNullableInt(sample.DebugTargetRelativeCounts)),
            ("Target Counts", FormatNullableInt(sample.DebugTargetCounts)),
            ("Actual Counts", FormatNullableInt(sample.DebugActualCounts)),
        ];
    }

    private static string CsvEscape(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatNullableDegrees(double? value) =>
        value.HasValue ? $"{Format(value.Value)} deg" : "n/a";

    private static string FormatNullableInt(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";

    private static string FormatNullableBool(bool? value) =>
        value.HasValue ? value.Value.ToString() : "n/a";

    private static string FormatHex(int? value) =>
        value.HasValue ? $"0x{value.Value:X4}" : "n/a";
}
