# JointBench TwinCAT Helper

C# helper for Windows production station setup and TwinCAT validation.

This is the first C# component in the migration path from the Python MVP to a Windows-native production application. It is intentionally small and command-oriented so the future WPF app can call the same operations.

## Build

```powershell
cd tools\JointBench.TwinCatHelper
dotnet restore
dotnet build
dotnet test
```

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
- Provide JSON output for future WPF integration.

## Next Scope

- Add a TwinCAT Automation Interface project-open spike.
- Add an EtherCAT scan spike once the Ti5 slave is physically connected.
- Add a WPF setup page that calls these helper operations.
