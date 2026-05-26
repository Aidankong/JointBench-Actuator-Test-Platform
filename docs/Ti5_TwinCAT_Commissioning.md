# Ti5 TwinCAT Commissioning Checklist

Use this checklist for the first Ti5 harmonic joint commissioning run.

## TwinCAT Preparation

1. Install TwinCAT 3 XAE on the development PC. XAE includes XAR runtime components.
2. Install or import the Ti5 ESI XML.
3. Connect the Ti5 axis to a dedicated EtherCAT port.
4. Scan EtherCAT devices in TwinCAT and confirm the Ti5 slave appears.
5. Map PDOs and verify CiA402 statusword, controlword, actual position, and target position.
6. Add the PLC interface from `twincat/src`.
7. Link `MAIN.stTi5In` / `MAIN.stTi5Out` to the scanned Ti5 CiA402 PDO variables.
8. Confirm `MAIN.stJointBench.*` symbols are visible in the PLC symbol table.
9. Activate configuration and start TwinCAT in Run mode.

## JointBench Preparation

Load these templates in `Protocol Setup`:

- `configs/buses/twincat_ads_local.yaml`
- `configs/devices/ti5_twincat_ads_template.yaml`
- `configs/safety/ti5_safe_limits_template.yaml`
- `configs/tests/ti5_ads_position_step_1deg.yaml`
- `configs/tests/ti5_ads_position_step_5deg.yaml`

Edit the templates for the actual station:

- AMS Net ID
- ADS host
- Ti5 vendor ID / product code / revision
- Encoder resolution, gear ratio, direction, zero offset
- Current and temperature limits
- Fixture-specific software limits

## C# Windows Helper Scan

With the Ti5 connected, the Windows helper can scan without opening TwinCAT manually:

```powershell
cd windows\JointBench.Windows
dotnet run --project src\JointBench.TwinCatHelper -- scan-spike
```

Expected Ti5 identity:

- Vendor ID: `0x00522227`
- Product code: `0x00009253`
- Revision: `0x00010005`
- Detected name: `Drive 1 (Ti5Robot_JointMotor)`

This scan creates a temporary TwinCAT project and does not activate configuration, enable the drive, or command motion.

## First Motion

1. Use an unloaded axis or a safe fixture.
2. Use a current-limited power supply.
3. Confirm physical emergency stop.
4. Confirm TwinCAT can enable the axis safely.
5. Stage A: In JointBench, click `Scan` and confirm PLC metadata. Enable only; do not command motion until `bOperationEnabled=True` and `bWatchdogOk=True`.
6. Stage B: Run the 1 deg profile-position test and review the generated report.
7. Stage C: Run the 5 deg profile-position test after the 1 deg result is clean.
8. Review `raw_data.csv`, `events.log`, `config_snapshot.yaml`, `report.md`, and `report.html`.

## Failure Handling

- ADS connection failure: check TwinCAT Router, AMS Net ID, port 851, and host.
- Symbol read failure: verify PLC symbol names and that symbol download is enabled.
- Operation enabled timeout: check PLC state machine and Ti5 drive faults.
- Motion blocked by JointBench: check safety YAML, scaling YAML, and first target limit.
- Watchdog error: check that JointBench keeps running, ADS writes are succeeding, and `nCommandSequence` changes in the PLC watch window.
- Drive fault: use TwinCAT diagnostics first, then retry after the PLC exposes a clean fault-reset flow.
