namespace JointBench.TwinCat;

public interface IMotionAdapter : IDisposable
{
    bool IsSimulation { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken);

    Task<AdsRuntimeConfigurationReport> ApplyRuntimeConfigAsync(
        SafetyLimits safety,
        StationScaling scaling,
        CancellationToken cancellationToken);

    Task SetEnableAsync(bool enabled, CancellationToken cancellationToken);

    Task SendPositionCommandAsync(double positionDegrees, CancellationToken cancellationToken);

    Task<ActuatorState> SampleAsync(double dtSeconds, double timestampSeconds, CancellationToken cancellationToken);

    Task EmergencyStopAsync(CancellationToken cancellationToken);
}
