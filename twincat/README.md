# JointBench TwinCAT Template

This folder contains a text-based PLC interface template for JointBench ADS integration.

Files:

- `JointBenchAdsInterface.md`: ADS contract and ownership boundaries.
- `src/JointBenchTypes.TcPOU`: structure definitions.
- `src/FB_JointBenchAxis.TcPOU`: example PLC-side axis wrapper.
- `src/MAIN.TcPOU`: example global instance exposing `MAIN.stJointBench`.

Import or copy the ST content into a TwinCAT PLC project. The template is intentionally conservative: it defines the ADS surface and placeholder control logic, while the final Ti5 EtherCAT axis binding must be completed in TwinCAT with the actual ESI, PDO mapping, scaling, and safety function blocks.
