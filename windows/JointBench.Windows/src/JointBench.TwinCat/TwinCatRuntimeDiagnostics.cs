using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace JointBench.TwinCat;

public sealed record TwinCatRuntimeEvent(
    DateTimeOffset TimeCreated,
    string ProviderName,
    string Level,
    string Message);

public static class TwinCatRuntimeDiagnostics
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<string> ReadRecentStartupErrors(DateTimeOffset since)
    {
        try
        {
            return ExtractStartupErrors(ReadApplicationEvents(since));
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<string> ExtractStartupErrors(IEnumerable<TwinCatRuntimeEvent> events) =>
        events
            .Where(IsStartupError)
            .Select(evt => $"{evt.TimeCreated:HH:mm:ss} {evt.ProviderName}: {Collapse(evt.Message)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private static IReadOnlyList<TwinCatRuntimeEvent> ReadApplicationEvents(DateTimeOffset since)
    {
        var systemTime = since.UtcDateTime.ToString("o");
        var xpath = $"*[System[TimeCreated[@SystemTime >= '{systemTime}']]]";
        var query = new EventLogQuery("Application", PathType.LogName, xpath)
        {
            ReverseDirection = true,
        };
        using var reader = new EventLogReader(query);
        var events = new List<TwinCatRuntimeEvent>();
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                var provider = record.ProviderName ?? string.Empty;
                if (!IsTwinCatProvider(provider))
                {
                    continue;
                }

                events.Add(new TwinCatRuntimeEvent(
                    record.TimeCreated is { } time ? new DateTimeOffset(time) : DateTimeOffset.MinValue,
                    provider,
                    record.LevelDisplayName ?? string.Empty,
                    record.FormatDescription() ?? string.Empty));
            }
        }

        return events;
    }

    private static bool IsStartupError(TwinCatRuntimeEvent evt)
    {
        var provider = evt.ProviderName;
        var text = evt.Message;
        return IsTwinCatProvider(provider) &&
            (evt.Level.Contains("error", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("License Violation", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("no license", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("Error starting TwinCAT", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("ADS ERROR", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTwinCatProvider(string provider) =>
        provider.Contains("Tc", StringComparison.OrdinalIgnoreCase) ||
        provider.Contains("TwinCAT", StringComparison.OrdinalIgnoreCase) ||
        provider.Contains("Beckhoff", StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string value) => Whitespace.Replace(value.Trim(), " ");
}
