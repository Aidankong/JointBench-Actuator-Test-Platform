# JointBench Windows Offline Deployment

This guide covers an offline production Windows PC running `JointBench.exe`.

## Recommended Production Route

For Ti5 EtherCAT production stations, use:

```text
JointBench.exe -> TwinCAT ADS -> TwinCAT XAR Runtime -> EtherCAT -> Ti5 axis
```

TwinCAT owns the EtherCAT master, PDO mapping, CiA402 state machine, and realtime motion.
JointBench sends commands and reads feedback through ADS symbols.

The direct `pysoem` EtherCAT path remains available for engineering experiments, but do not let TwinCAT and `pysoem` control the same EtherCAT adapter at the same time.

## Build On Development PC

Recommended build environment:

- Windows 10/11 x64
- Python 3.12 x64 virtual environment
- TwinCAT ADS build: install with `.[ads]`

```powershell
python -m pip install -e ".[dev]"
.\scripts\build_windows.ps1

# ADS-enabled package
.\scripts\build_windows.ps1 -WithAds

.\scripts\smoke_packaged_app.ps1
```

Output:

```text
dist/
  JointBench/
    JointBench.exe
    configs/
    docs/
    twincat/
    reports/
```

Copy the entire `dist/JointBench` folder to the production PC.

## Production PC Setup

1. Install TwinCAT 3 XAR Runtime. TwinCAT XAE includes XAR and is also suitable for development/debug stations.
2. Install the Ti5 ESI file in TwinCAT.
3. Create or import the PLC project that exposes `MAIN.stJointBench`.
4. Activate the EtherCAT configuration and PLC project to the local runtime.
5. Confirm the ADS route is available and the PLC task is running.
6. Launch `JointBench.exe`.
7. Open `Protocol Setup` and load:
   - `configs/buses/twincat_ads_local.yaml`
   - `configs/devices/ti5_twincat_ads_template.yaml`
   - `configs/safety/ti5_safe_limits_template.yaml`
   - `configs/tests/ti5_ads_position_step_1deg.yaml` for first motion, then `configs/tests/ti5_ads_position_step_5deg.yaml`
8. Click `Validate`, then `Scan`.
9. Confirm metadata, safety limits, fixture state, emergency stop, `bOperationEnabled`, and `bWatchdogOk` before starting the first 1 deg test.

## Offline Notes

- The production PC does not need Python.
- `reports/` is created automatically on first run.
- Each test folder contains `raw_data.csv`, `events.log`, `config_snapshot.yaml`, `report.md`, and `report.html`.
- Use the ADS-enabled build when controlling through TwinCAT.
- `pyads` uses Beckhoff `TcAdsDll.dll` on Windows. If PyInstaller reports that this DLL was not found on the build PC, the ADS-enabled app can still run on a production PC where TwinCAT XAR/XAE is installed and the DLL is available through TwinCAT.
- Keep the loaded YAML files under version control or release control for traceability.
