# TwinCAT ADS Integration

JointBench V0.3 adds `twincat_ads` as the recommended production path for EtherCAT axes.

## Architecture

```text
JointBench GUI
  TwinCATAdsAdapter
    pyads
      ADS Router
        TwinCAT PLC
          FB_JointBenchAxis
            EtherCAT master / Ti5 CiA402 axis
```

JointBench is not the EtherCAT master in this mode. It reads and writes PLC symbols exposed through ADS.

## Required ADS Symbols

Default prefix:

```text
MAIN.stJointBench
```

Required fields:

| Group | Symbol | Type |
|---|---|---|
| Command | bEnable | BOOL |
| Command | bStart | BOOL |
| Command | bStop | BOOL |
| Command | bResetFault | BOOL |
| Command | fTargetPositionDeg | LREAL |
| Command | nCommandSequence | DINT |
| Feedback | bReady | BOOL |
| Feedback | bBusy | BOOL |
| Feedback | bDone | BOOL |
| Feedback | bError | BOOL |
| Feedback | bOperationEnabled | BOOL |
| Feedback | bWatchdogOk | BOOL |
| Telemetry | fActualPositionDeg | LREAL |
| Telemetry | fActualVelocityDps | LREAL |
| Telemetry | fFollowingErrorDeg | LREAL |
| Telemetry | fCurrentA | LREAL |
| Telemetry | fTemperatureC | LREAL |
| Diagnostics | nStatusword | DINT |
| Diagnostics | nControlword | DINT |
| Diagnostics | nFaultCode | DINT |
| Diagnostics | nErrorCode | DINT |
| Metadata | sDeviceName | STRING |
| Metadata | nVendorId | DINT |
| Metadata | nProductCode | DINT |
| Metadata | nRevision | DINT |

The YAML device profile can override any symbol path. When not overridden, JointBench builds the path as `<prefix>.<symbol>`.

`nCommandSequence` is written by JointBench during connect, enable, start, stop, and each sampling cycle. The PLC watchdog must quick-stop the axis and publish an error when `bEnable` remains true but this value stops changing longer than the station timeout. `fFollowingErrorDeg` is diagnostic in JointBench reports; realtime following-error safety belongs in the PLC.

## YAML Example

Bus:

```yaml
protocol: twincat_ads
ads:
  ams_net_id: "127.0.0.1.1.1"
  ams_port: 851
  host: "127.0.0.1"
  timeout_ms: 1000
  cycle_time_ms: 10
```

Device:

```yaml
ads:
  symbol_prefix: "MAIN.stJointBench"
  symbols:
    bEnable: "MAIN.stJointBench.bEnable"
```

## Safety Rules

JointBench blocks real motion when:

- Safety limits are missing.
- Position scaling is missing.
- AMS Net ID is missing.
- Required ADS symbols cannot be resolved.
- The requested first target exceeds +/-5 deg.
- The PLC reports an unhealthy ADS watchdog through `bWatchdogOk = FALSE`.

The PLC must also implement local safety: software limits, quick stop, fault reset policy, and operation-enabled supervision.

## Build With ADS

```powershell
python -m pip install -e ".[ads]"
.\scripts\build_windows.ps1 -WithAds
```

Use `configs/buses/twincat_ads_fake.yaml` for offline UI and unit-test validation without TwinCAT.

On Windows, ADS communication requires Beckhoff `TcAdsDll.dll`. This is normally installed with TwinCAT XAR/XAE. A build PC without TwinCAT may show a PyInstaller warning for that DLL; the production PC still needs TwinCAT installed for real ADS communication.
