# JointBench TwinCAT Template

This folder contains a text-based PLC interface template for JointBench ADS integration.

Files:

- `JointBenchAdsInterface.md`: ADS contract and ownership boundaries.
- `src/JointBenchTypes.TcPOU`: structure definitions.
- `src/FB_JointBenchAxis.TcPOU`: example PLC-side axis wrapper.
- `src/MAIN.TcPOU`: example global instance exposing `MAIN.stJointBench`.

Import or copy the ST content into a TwinCAT PLC project. The template is intentionally conservative: it defines the ADS surface, Ti5 CiA402 PDO binding placeholders, command watchdog, scaling, and local safety checks. The final station values and PDO links still must be completed in TwinCAT with the actual ESI, slave scan result, fixture limits, and emergency-stop wiring.
