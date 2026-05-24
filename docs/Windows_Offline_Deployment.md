# JointBench Windows 离线产线部署说明

适用对象：Ti5 EtherCAT 谐波关节测试工位、离线 Windows 产线电脑。

## 1. 推荐环境

- Windows 10/11 x64 或 Windows 工控机系统。
- 独立 EtherCAT 网口，不与办公网络共用。
- Python 3.12 x64 构建环境用于开发机打包。
- 产线电脑不需要安装 Python。
- EtherCAT 真机测试需要安装 Npcap / WinPcap 兼容驱动。

## 2. 开发机打包

```powershell
python -m pip install -r requirements.txt
python -m pip install -e .
.\scripts\build_windows.ps1
.\scripts\smoke_packaged_app.ps1
```

输出目录：

```text
dist/
  JointBench/
    JointBench.exe
    configs/
    docs/
    reports/
```

## 3. 产线电脑部署

1. 将 `dist/JointBench` 整个文件夹复制到产线电脑。
2. 安装 Npcap / 网卡驱动。
3. 将 Ti5 EtherCAT 电机连接到独立网口。
4. 运行 `JointBench.exe`。
5. 在 `Protocol Setup` 中加载：
   - `configs/buses/ethercat_ti5_template.yaml`
   - `configs/devices/ti5_cia402_template.yaml`
   - `configs/safety/ti5_safe_limits_template.yaml`
   - `configs/tests/ti5_position_step_5deg.yaml`
   - Ti5 供应商 ESI XML
6. 点击 `Scan`，确认 vendor id / product code / revision 匹配。
7. 首次运动只允许小角度 `5 deg` 内测试。

## 4. Ti5 真机配置注意事项

- 必须根据 Ti5 实际样机填写编码器分辨率、减速比、方向和零点。
- 必须根据治具和电源能力填写软限位、电流限制和温度限制。
- 未提供 safety 或 scaling 时，平台会禁止真实电机运动。
- 未提供 ESI 时，允许 SDO 扫描，但 PDO 校验不可用。

## 5. 安全要求

- 首次测试必须空载或固定在安全治具上。
- 使用可限流电源。
- 准备物理急停或可快速断电开关。
- 不允许带电插拔 EtherCAT 或动力线。
- 真机首次目标角度不要超过 `5 deg`。

## 6. 故障排查

- 扫描不到 slave：检查网卡名称、网线、Npcap、驱动和电源。
- Identity 不匹配：检查 ESI、device YAML 中 vendor/product/revision。
- 不能运动：检查 safety、scaling、状态机是否进入 Operation Enabled。
- SDO 超时：检查设备是否支持对应 CiA402 对象，或是否需要先清 fault。
