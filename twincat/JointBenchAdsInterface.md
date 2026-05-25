# JointBench ADS Interface Contract

TwinCAT owns EtherCAT and realtime motion. JointBench owns test orchestration, reporting, and operator workflow.

## Exposed Symbol

```text
MAIN.stJointBench : ST_JointBenchAds
```

JointBench reads and writes only this structure unless the YAML symbol map overrides individual symbols.

## PLC Responsibilities

- Map JointBench commands to the Ti5 axis.
- Implement CiA402 enable, fault reset, quick stop, and disable voltage.
- Enforce position, velocity, current, and temperature limits.
- Monitor `nCommandSequence`; quick-stop the axis if commands stop updating while enabled.
- Publish actual position, velocity, current, temperature, statusword, controlword, and error codes.
- Keep the axis safe if ADS disconnects or commands stop updating.

## JointBench Responsibilities

- Load and validate station YAML configuration.
- Write `bEnable`, `bStart`, `bStop`, `bResetFault`, and `fTargetPositionDeg`.
- Increment `nCommandSequence` during connect, enable, start, stop, and sample cycles.
- Read feedback, telemetry, `bWatchdogOk`, and `fFollowingErrorDeg`.
- Abort on communication failures or reported faults.
- Generate CSV, Markdown, and HTML reports.

## Commissioning Sequence

1. Stage A: validate configuration, scan ADS symbols, and enable without motion.
2. Stage B: run the 1 deg commissioning position step.
3. Stage C: run the 5 deg acceptance position step.
