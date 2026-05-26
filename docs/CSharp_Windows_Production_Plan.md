# C# Windows Production Development Plan

## Status on This PC

Verified on 2026-05-26:

- Visual Studio Community 2026 is installed at `C:\Program Files\Microsoft Visual Studio\18\Community`.
- Visual Studio version: `18.6.1`.
- VS `devenv.exe` is available through the Visual Studio installation directory.
- VS `MSBuild.exe` is available at `MSBuild\Current\Bin\amd64\MSBuild.exe`.
- `.NET SDK 10.0.300` is installed at `C:\Users\Administrator\.dotnet`.
- `dotnet msbuild` reports `MSBuild 18.6.3`.
- `NuGet CLI 7.6.0.59` is installed at `C:\Users\Administrator\.codex-tools\nuget`.
- `.NET Framework 4.8 targeting pack` is installed.
- WPF `net10.0-windows10.0.19041.0` build succeeds.
- `Beckhoff.TwinCAT.Ads 7.0.172` restores and builds in a `net10.0` console app.
- Local TwinCAT ADS .NET Framework DLL `C:\TwinCAT\AdsApi\.NET\v4.0.30319\TwinCAT.Ads.dll` builds in a `net48` console app.
- `TcXaeShell.DTE.15.0` COM automation can be instantiated.
- `VisualStudio.DTE.18.0` COM automation can be instantiated.
- TwinCAT 3.1 XAE base is present, version `3.1.4024.75`.

Conclusion: the machine is now suitable for C# development, WPF development, ADS communication development, and TwinCAT Automation Interface spike work.

## Direction

JointBench should move toward a C# Windows production application, but not as a single large rewrite.

The current Python implementation remains the reference MVP for:

- Test flow behavior.
- Safety gate semantics.
- ADS public symbol contract.
- Report output shape.
- Fake backend and unit-test behavior.

The C# path should first automate TwinCAT station setup and ADS validation, then become the production operator UI.

## Target Architecture

```text
JointBench Production App (WPF/.NET)
  |
  +-- Operator workflow
  |     - Station setup
  |     - ESI import
  |     - TwinCAT preflight
  |     - Scan / enable / 1deg / 5deg workflow
  |
  +-- JointBench.Core
  |     - Test case orchestration
  |     - Safety rules
  |     - Result models
  |     - Report models
  |
  +-- JointBench.Ads
  |     - Beckhoff.TwinCAT.Ads client
  |     - ADS symbol validation
  |     - Command sequence heartbeat
  |     - Watchdog/status/following-error reads
  |
  +-- JointBench.TwinCAT
        - ESI import
        - TwinCAT XAE / Automation Interface checks
        - Project open/generate/activate spike
        - EtherCAT scan and PDO binding spike
```

TwinCAT remains the EtherCAT master and real-time safety owner. The Windows app owns station workflow, operator UX, ADS command orchestration, data capture, and reports.

## Phase 1: C# TwinCAT Helper

Goal: create a small C# CLI/helper before starting the full WPF app.

The helper should prove that the upper computer can prepare and verify the TwinCAT side without requiring a production operator to manually open TwinCAT.

Current implementation:

- `windows/JointBench.Windows` solution exists.
- `JointBench.TwinCat` shared library exists for CLI and WPF reuse.
- `check-prereqs`, `twincat-info`, `esi-summary`, `install-esi`, `check-ads-symbols`, and `automation-smoke` are implemented.
- The helper targets `net10.0-windows10.0.19041.0`.
- Unit tests cover command parsing, ESI parsing/install behavior, helper help output, and required ADS symbol coverage.
- On this PC, `check-prereqs` passes and `install-esi` successfully installs `Ti5Robot_JointMotor_2.0.xml` into the TwinCAT ESI directory.

Planned commands:

- `check-prereqs`
  - Verify TwinCAT XAE/XAR installation.
  - Verify Visual Studio / TcXaeShell DTE registration.
  - Verify ADS router/system service status.
  - Verify ESI directory permissions.
  - Verify admin status when needed.

- `install-esi --file <path>`
  - Validate EtherCAT ESI XML root.
  - Extract vendor/product/revision summary.
  - Copy into TwinCAT ESI directory.
  - Report whether restart/reload is required.

- `check-ads-symbols --ams <id> --port 851 --prefix MAIN.stJointBench`
  - Connect through ADS.
  - Verify all required symbols, including `nCommandSequence`, `bWatchdogOk`, and `fFollowingErrorDeg`.
  - Print a machine-readable result for the WPF app.

- `twincat-info`
  - Show TwinCAT version, XAE path, TCatSysManager type library availability, DTE ProgIDs, and service status.

- `scan-spike`
  - Spike TwinCAT Automation Interface EtherCAT scan behavior.
  - Confirm whether scan can be run headless/reliably on this station.
  - If full scan automation is blocked by TwinCAT UI constraints, document the exact fallback.

Deliverable:

- `windows/JointBench.Windows/`
- Unit tests for ESI parsing and prerequisite checks.
- A documented smoke command sequence.

## Phase 2: Station Setup Workflow

Goal: make setup explicit and repeatable.

The WPF app should expose a station setup page with these states:

- Environment check.
- ESI import.
- TwinCAT project check or generation.
- EtherCAT scan check.
- Ti5 slave identity check.
- PDO binding check.
- ADS symbol check.
- Safety config validation.

Current implementation:

- `JointBench.ProductionApp` WPF shell exists.
- The first station setup surface calls the shared C# library for environment checks, ESI import, Automation Interface smoke, and ADS symbol validation.
- Motion controls remain visually locked until the PLC/Ti5 hardware path is validated.
- On this PC, the WPF app builds and passes a process startup smoke.

Expected operator result:

- The operator selects the ESI XML and station config.
- The app performs all safe automated steps.
- The app blocks motion until all required checks pass.
- Errors point to concrete fixes, such as AMS Net ID, ADS route, ESI mismatch, missing symbols, missing PDO links, or TwinCAT not in Run mode.

Important boundary:

- Engineering may still need to approve or prepare the first TwinCAT project template.
- Production operators should not need to browse TwinCAT during normal station setup or daily testing.

## Phase 3: Production Test UI

Goal: replace the Python operator UI with a Windows-native WPF production app.

Core screens:

- Station status.
- Device setup.
- Safety interlock status.
- Enable-only check.
- 1deg commissioning test.
- 5deg acceptance test.
- Report/result browser.

Required behaviors:

- Never allow first motion above 1deg.
- Keep the 5deg V1 hard limit.
- Increment command sequence on connect, enable, start, stop, and sampling loops.
- Surface watchdog state and following error in the UI.
- Write `raw_data.csv`, `events.log`, `config_snapshot.yaml`, `report.md`, and `report.html`.
- Keep Python report output shape as the compatibility baseline until C# reports are approved.

## Phase 4: TwinCAT Project Automation

Goal: reduce or remove manual TwinCAT engineering steps.

Work items:

- Generate or open a TwinCAT solution from a known template.
- Import PLC template code.
- Verify `MAIN.stJointBench` symbols are exported.
- Import/reload ESI descriptions.
- Scan EtherCAT adapters and slaves.
- Match Ti5 by vendor/product/revision.
- Link PDOs to PLC placeholders.
- Activate configuration.
- Set TwinCAT Run mode.

Risk:

TwinCAT Automation Interface support for fully automated scan/link/activate workflows must be validated on the real station. Some operations may require XAE UI availability, admin rights, or a prepared project template. This is why `scan-spike` is the first automation spike, not a late integration task.

## Phase 5: Python Parity and Cutover

Goal: switch production operation to C# only after parity is proven.

Cutover criteria:

- C# can install ESI and validate station prerequisites.
- C# can connect ADS and validate all required symbols.
- C# can perform enable-only without motion.
- C# can run 1deg and 5deg tests through the same PLC ADS contract.
- Reports match the Python acceptance content.
- Fault, stop, ADS disconnect, and watchdog scenarios are covered.
- Python and C# produce equivalent pass/fail decisions on fake data and ADS simulation.

After cutover:

- Python remains an engineering reference and test oracle.
- C# becomes the production operator application.
- Shared config schemas must be versioned carefully to prevent station drift.

## Immediate Sprint

1. Done: add `windows/JointBench.Windows` C# solution.
2. Done: implement `check-prereqs`.
3. Done: port ESI validation/import from Python to C#.
4. Done: implement ADS symbol validation with `Beckhoff.TwinCAT.Ads`.
5. Done: add fake/local smoke tests that do not require Ti5 hardware.
6. Done: spike `TcXaeShell.DTE.15.0` / `VisualStudio.DTE.18.0` startup and version discovery.
7. Done: add first WPF station setup shell backed by shared C# library calls.
8. Next: spike EtherCAT scan automation once the Ti5 slave is connected.
9. Next: decide whether project generation and PDO linking are reliable enough for production automation.

## Open Decisions

- Whether the production app targets `.NET 10` immediately, or uses `.NET 8 LTS` for longer conservative support.
- Whether station PCs will install full TwinCAT XAE, or only XAR plus a prepared project.
- Whether the production app is allowed to require administrator elevation for setup tasks.
- Whether TwinCAT project activation is performed by the app, or by an engineering-only setup mode.
- How much of the PLC template remains generated versus manually maintained in TwinCAT.
