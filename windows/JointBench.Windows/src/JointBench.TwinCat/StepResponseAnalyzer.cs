namespace JointBench.TwinCat;

public static class StepResponseAnalyzer
{
    public static StepResponseMetrics Analyze(IReadOnlyList<ActuatorState> samples, TestConfig config)
    {
        if (samples.Count < 5)
        {
            return new StepResponseMetrics(null, null, null, 0.0, null, 0.0, 0.0, 0.0, null);
        }

        var positions = samples.Select(sample => sample.ActualPositionDegrees).ToArray();
        var currents = samples.Select(sample => Math.Abs(sample.CurrentA)).ToArray();
        var temperatures = samples.Select(sample => sample.TemperatureC).ToArray();
        var times = samples.Select(sample => sample.TimestampSeconds).ToArray();
        var delta = config.TargetPositionDegrees - config.StartPositionDegrees;
        var absDelta = Math.Abs(delta);
        if (absDelta < 1e-9)
        {
            return new StepResponseMetrics(
                null,
                null,
                null,
                0.0,
                null,
                currents.Max(),
                currents.Average(),
                temperatures.Max(),
                null);
        }

        var direction = delta > 0 ? 1.0 : -1.0;
        var moved = positions.Select(position => direction * (position - config.StartPositionDegrees)).ToArray();
        var responseThreshold = Math.Max(0.5, 0.02 * absDelta);
        var responseDelay = TimeAt(times, FirstIndex(moved, value => value >= responseThreshold));
        var rise10 = FirstIndex(moved, value => value >= 0.10 * absDelta);
        var rise90 = FirstIndex(moved, value => value >= 0.90 * absDelta);
        double? riseTime = rise10 is not null && rise90 is not null && rise90 >= rise10
            ? times[rise90.Value] - times[rise10.Value]
            : null;

        var overshoot = direction > 0
            ? Math.Max(0.0, (positions.Max() - config.TargetPositionDegrees) / absDelta * 100.0)
            : Math.Max(0.0, (config.TargetPositionDegrees - positions.Min()) / absDelta * 100.0);
        var band = Math.Max(0.05, config.SettlingBandPercent / 100.0 * absDelta);
        var withinBand = positions.Select(position => Math.Abs(position - config.TargetPositionDegrees) <= band).ToArray();
        var settlingTime = TimeAt(times, SettlingIndex(withinBand, config.SampleRateHz));
        var tailCount = Math.Max(3, (int)(positions.Length * 0.1));
        var tail = positions.Skip(Math.Max(0, positions.Length - tailCount)).ToArray();
        var steadyStateError = tail.Average() - config.TargetPositionDegrees;
        var tailAverage = tail.Average();
        var jitter = Math.Sqrt(tail.Select(position => Math.Pow(position - tailAverage, 2)).Average());

        return new StepResponseMetrics(
            responseDelay,
            riseTime,
            settlingTime,
            overshoot,
            steadyStateError,
            currents.Max(),
            currents.Average(),
            temperatures.Max(),
            jitter);
    }

    public static StepJudgment Judge(
        StepResponseMetrics metrics,
        TestConfig config,
        bool aborted = false,
        IEnumerable<string>? failureReasons = null)
    {
        var reasons = new List<string>(failureReasons ?? []);
        if (aborted)
        {
            return new StepJudgment("ABORTED", reasons.Count > 0 ? reasons : ["Test aborted before completion."]);
        }

        if (metrics.SettlingTimeSeconds is null || metrics.SteadyStateErrorDegrees is null)
        {
            return new StepJudgment("INVALID", reasons.Count > 0 ? reasons : ["Response did not produce enough valid analysis data."]);
        }

        if (metrics.OvershootPercent > config.MaxOvershootPercent)
        {
            reasons.Add($"Overshoot {metrics.OvershootPercent:F2}% > {config.MaxOvershootPercent:F2}%.");
        }

        if (metrics.SettlingTimeSeconds > config.MaxSettlingTimeSeconds)
        {
            reasons.Add($"Settling time {metrics.SettlingTimeSeconds:F3}s > {config.MaxSettlingTimeSeconds:F3}s.");
        }

        if (Math.Abs(metrics.SteadyStateErrorDegrees.Value) > config.MaxSteadyStateErrorDegrees)
        {
            reasons.Add(
                $"Steady-state error {metrics.SteadyStateErrorDegrees:F3}deg > {config.MaxSteadyStateErrorDegrees:F3}deg.");
        }

        if (metrics.PeakCurrentA > config.MaxCurrentA)
        {
            reasons.Add($"Peak current {metrics.PeakCurrentA:F2}A > {config.MaxCurrentA:F2}A.");
        }

        if (metrics.MaxTemperatureC > config.MaxTemperatureC)
        {
            reasons.Add($"Max temperature {metrics.MaxTemperatureC:F1}C > {config.MaxTemperatureC:F1}C.");
        }

        return new StepJudgment(reasons.Count > 0 ? "FAIL" : "PASS", reasons);
    }

    private static int? FirstIndex(IReadOnlyList<double> values, Func<double, bool> predicate)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }

        return null;
    }

    private static double? TimeAt(IReadOnlyList<double> times, int? index) =>
        index is null ? null : times[index.Value];

    private static int? SettlingIndex(IReadOnlyList<bool> withinBand, double sampleRateHz)
    {
        var minTail = Math.Max(5, (int)(0.15 * sampleRateHz));
        for (var index = 0; index <= withinBand.Count - minTail; index++)
        {
            var tail = withinBand.Skip(index).ToArray();
            if (tail.Length < minTail)
            {
                break;
            }

            if (tail.Count(value => value) / (double)tail.Length >= 0.98)
            {
                return index;
            }
        }

        return null;
    }
}
