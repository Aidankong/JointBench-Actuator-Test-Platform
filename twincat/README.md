# JointBench TwinCAT Template

This folder contains a text-based PLC interface template for JointBench ADS integration.

Files:

- `JointBenchAdsInterface.md`: ADS contract and ownership boundaries.
- `src/ST_JointBenchAds.TcDUT`: ADS command/status structure exposed as `MAIN.stJointBench`.
- `src/ST_Ti5CiA402PdoInput.TcDUT`: Ti5 CiA402 input PDO structure.
- `src/ST_Ti5CiA402PdoOutput.TcDUT`: Ti5 CiA402 output PDO structure.
- `src/FB_JointBenchAxis.TcPOU`: example PLC-side axis wrapper.
- `src/MAIN.TcPOU`: example global instance exposing `MAIN.stJointBench`.

Import the split DUT files first, then the function block and `MAIN` POU. The template is intentionally conservative: it defines the ADS surface, Ti5 CiA402 PDO binding placeholders, command watchdog, scaling, and local safety checks. The final station values and PDO links still must be completed with the actual ESI, slave scan result, fixture limits, and emergency-stop wiring.
