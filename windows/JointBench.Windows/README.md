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
```

## Current Scope

- Validate the Windows/TwinCAT/C# prerequisite environment.
- Validate and install EtherCAT ESI XML files into the TwinCAT ESI directory.
- Validate the public JointBench ADS symbol surface.
- Validate basic TwinCAT / Visual Studio DTE Automation Interface startup.
- Provide JSON output for integration and diagnostics.
- Provide a first WPF station setup surface for preflight, ESI import, automation smoke, and ADS symbol checks.

## Next Scope

- Add an EtherCAT scan spike once the Ti5 slave is physically connected.
- Add TwinCAT project template open/generate/activate operations.
- Add enable-only, 1deg, and 5deg ADS workflows after PLC hardware validation.
