namespace JointBench.TwinCat;

public sealed class HardStoneDebugSessionLock : IDisposable
{
    public const string DefaultName = @"Local\JointBenchHardStoneOpenOcd";

    private readonly FileStream stream;
    private bool disposed;

    private HardStoneDebugSessionLock(FileStream stream)
    {
        this.stream = stream;
    }

    public static HardStoneDebugSessionLock Acquire(TimeSpan timeout) =>
        Acquire(DefaultName, timeout);

    public static HardStoneDebugSessionLock Acquire(string name, TimeSpan timeout)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Sanitize(name)}.lock");
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                stream.SetLength(0);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.WriteLine($"pid={Environment.ProcessId}");
                writer.WriteLine($"utc={DateTimeOffset.UtcNow:O}");
                writer.Flush();
                stream.Position = 0;
                return new HardStoneDebugSessionLock(stream);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25));
            }
            catch (UnauthorizedAccessException) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25));
            }
            catch (IOException exc)
            {
                throw BusyException(exc);
            }
            catch (UnauthorizedAccessException exc)
            {
                throw BusyException(exc);
            }
        }

        static InvalidOperationException BusyException(Exception inner) =>
            new(
                "HardStone debug link is already in use by another JointBench/OpenOCD operation. Wait for it to finish and retry.",
                inner);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stream.Dispose();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Append('\\').Append('/').ToHashSet();
        return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
