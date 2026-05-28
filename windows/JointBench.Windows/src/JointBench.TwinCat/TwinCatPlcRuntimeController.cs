using TwinCAT.Ads;

namespace JointBench.TwinCat;

public interface IPlcRuntimeStateClient
{
    StateInfo ReadState();

    void WriteControl(StateInfo stateInfo);
}

public sealed class BeckhoffPlcRuntimeStateClient : IPlcRuntimeStateClient, IDisposable
{
    private readonly AdsClient client = new();

    public BeckhoffPlcRuntimeStateClient(int port)
    {
        client.Connect(port);
    }

    public StateInfo ReadState() => client.ReadState();

    public void WriteControl(StateInfo stateInfo) => client.WriteControl(stateInfo);

    public void Dispose() => client.Dispose();
}

public static class TwinCatPlcRuntimeController
{
    public static StateInfo EnsureLocalPortRun(int port = 851, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        Exception? lastException = null;
        do
        {
            try
            {
                using var client = new BeckhoffPlcRuntimeStateClient(port);
                return EnsureRun(client, TimeSpan.FromSeconds(3));
            }
            catch (Exception exc) when (exc is AdsErrorException or TimeoutException)
            {
                lastException = exc;
                Thread.Sleep(TimeSpan.FromMilliseconds(250));
            }
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException($"Timed out waiting for PLC ADS port {port} to enter Run.", lastException);
    }

    public static StateInfo EnsureRun(IPlcRuntimeStateClient client, TimeSpan timeout)
    {
        var state = client.ReadState();
        if (state.AdsState == AdsState.Run)
        {
            return state;
        }

        client.WriteControl(new StateInfo(AdsState.Run, state.DeviceState));
        if (timeout <= TimeSpan.Zero)
        {
            return client.ReadState();
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            state = client.ReadState();
            if (state.AdsState == AdsState.Run)
            {
                return state;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException($"PLC ADS server did not enter Run. Last state: {state.AdsState}/{state.DeviceState}.");
    }
}
