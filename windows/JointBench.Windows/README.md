# JointBench Windows

C#/.NET solution for the Windows production station software.

This is the migration path from the Python MVP to a Windows-native production application. The shared `JointBench.TwinCat` library is used by both the command-line helper and the WPF production shell.

## Build

```powershell
cd windows\JointBench.Windows
dotnet restore
dotnet build
dotnet test
```

## Projects

- `src\JointBench.TwinCat`: shared TwinCAT, ADS, ESI, and Automation Interface helpers.
- `src\JointBench.TwinCatHelper`: command-line station setup helper.
- `src\JointBench.ProductionApp`: WPF production shell.
- `tests\JointBench.TwinCatHelper.Tests`: local unit tests for helper and shared logic.

## Commands

```powershell
dotnet run --project src\JointBench.TwinCatHelper -- check-prereqs
dotnet run --project src\JointBench.TwinCatHelper -- twincat-info --json
dotnet run --project src\JointBench.TwinCatHelper -- esi-summary --file C:\path\to\Ti5Robot_JointMotor_2.0.xml
dotnet run --project src\JointBench.TwinCatHelper -- install-esi --file C:\path\to\Ti5Robot_JointMotor_2.0.xml
dotnet run --project src\JointBench.TwinCatHelper -- check-ads-symbols --ams 127.0.0.1.1.1 --port 851 --prefix MAIN.stJointBench
dotnet run --project src\JointBench.TwinCatHelper -- automation-smoke --prog-id TcXaeShell.DTE.15.0
dotnet run --project src\JointBench.TwinCatHelper -- scan-spike
dotnet run --project src\JointBench.TwinCatHelper -- prepare-twincat --station data\stations\ti5_ads
dotnet run --project src\JointBench.TwinCatHelper -- run-sequence --station data\stations\ti5_ads --language zh-CN --confirm-motion
```

## Current Scope

- Validate the Windows/TwinCAT/C# prerequisite environment.
- Validate and install EtherCAT ESI XML files into the TwinCAT ESI directory.
- Validate the public JointBench ADS symbol surface.
- Validate basic TwinCAT / Visual Studio DTE Automation Interface startup.
- Scan EtherCAT masters and slave boxes through a temporary TwinCAT project without activating configuration.
- Build a Ti5 PDO link plan for the scanned slave.
- Run the C# native enable-only, 1deg, and 5deg ADS sequence after the station is confirmed safe.
- Generate `raw_data.csv`, `events.log`, `config_snapshot.yaml`, `report.md`, and `report.html` from C#.
- Switch the WPF production shell between Chinese and English; reports follow the selected language.
- Provide JSON output for integration and diagnostics.
- Provide a WPF production surface for preflight, ESI import, engineering preparation, scan, ADS symbol checks, safety confirmation, sequence execution, and report browsing.

## Station Directory

Real station values live under ignored local data, for example `data\stations\ti5_ads`:

```text
data\stations\ti5_ads\
  bus.yaml
  device.yaml
  safety.yaml
  tests.yaml
```

`run-sequence` requires `--confirm-motion` for real ADS motion. Use `--fake` only for offline software verification.

## Next Scope

- Harden full TwinCAT project generation, PDO linking, activation, and runtime restart on the first engineering station.
- Validate the real hardware sequence with physical emergency stop, current-limited power, and safe fixture.
- Add richer trend plots to generated HTML reports.
