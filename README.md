# JointBench Actuator Test Platform

机器人关节模组测试与性能分析平台 MVP。

JointBench 面向机器人关节、电机模组、伺服执行器和产线测试场景。当前版本实现了一个不依赖真实硬件的闭环演示：连接 Mock 执行器，执行位置阶跃响应测试，实时显示曲线，保存 CSV 数据，计算响应指标，并生成 Markdown / HTML 测试报告。

## 当前交付

- PySide6 桌面上位机
- Mock 执行器模型
- 位置阶跃响应测试
- 实时位置、速度、电流、温度曲线
- CSV 原始数据保存
- 阶跃响应指标分析
- PASS / FAIL 自动判定
- Markdown / HTML 报告生成
- pytest 单元测试
- TwinCAT ADS 产线主链路模板

## 快速开始

```powershell
python -m pip install -r requirements.txt
python -m pip install -e .
python -m jointbench
```

可选通信依赖：

```powershell
# CANopen CiA402
python -m pip install -e ".[can]"

# EtherCAT CoE CiA402
python -m pip install -e ".[ethercat]"

# TwinCAT ADS production route
python -m pip install -e ".[ads]"
```

Windows 离线产线打包：

```powershell
python -m pip install -e .
.\scripts\build_windows.ps1

# ADS-enabled package for TwinCAT production stations
.\scripts\build_windows.ps1 -WithAds
.\scripts\smoke_packaged_app.ps1
```

打包产物建议命名为 `JointBench-v0.3.0-win64.zip`，用于 GitHub Release 或离线拷贝到产线电脑。

启动后：

1. 点击 `Connect` 连接 Mock 执行器。
2. 保持默认目标位置 `30 deg`。
3. 点击 `Start Test`。
4. 等待曲线运行结束。
5. 查看右侧指标和 PASS / FAIL 结果。
6. 点击 `Open Report Folder` 查看生成的报告。

## 运行测试

```powershell
python -m pytest
python -m jointbench --smoke-test
python -m jointbench --protocol-dialog-smoke-test
```

## V1 CiA402 配置入口

主界面左侧点击 `Protocol Setup` 可以打开通信配置窗口，支持：

- Mock
- CANopen CiA402
- EtherCAT CoE CiA402
- TwinCAT ADS

仓库内提供了离线 fake 配置，可用于无硬件验证协议配置、扫描和状态机流程：

- `configs/buses/canopen_fake.yaml`
- `configs/buses/ethercat_fake.yaml`
- `configs/buses/twincat_ads_fake.yaml`
- `configs/devices/sample_cia402_joint.yaml`
- `configs/safety/default_joint_limits.yaml`
- `configs/tests/position_step_5deg.yaml`

真实设备接入前必须提供安全限位和单位换算配置；配置不完整时平台会阻止真实设备运动。

Ti5 EtherCAT 模板：

- `configs/buses/ethercat_ti5_template.yaml`
- `configs/devices/ti5_cia402_template.yaml`
- `configs/safety/ti5_safe_limits_template.yaml`
- `configs/tests/ti5_position_step_5deg.yaml`

Ti5 TwinCAT ADS 产线模板：

- `configs/buses/twincat_ads_local.yaml`
- `configs/devices/ti5_twincat_ads_template.yaml`
- `configs/safety/ti5_safe_limits_template.yaml`
- `configs/tests/ti5_ads_position_step_1deg.yaml`
- `configs/tests/ti5_ads_position_step_5deg.yaml`

产线推荐路线：

```text
JointBench -> TwinCAT ADS -> TwinCAT XAR -> EtherCAT -> Ti5
```

研发备选路线：

```text
JointBench -> pysoem direct EtherCAT -> Ti5
```

不要让 TwinCAT 和 pysoem 同时控制同一块 EtherCAT 网卡。

离线部署说明：

- [Windows Offline Deployment](./docs/Windows_Offline_Deployment.md)
- [TwinCAT ADS Integration](./docs/TwinCAT_ADS_Integration.md)
- [Ti5 TwinCAT Commissioning](./docs/Ti5_TwinCAT_Commissioning.md)
- [C# Windows Production Plan](./docs/CSharp_Windows_Production_Plan.md)

## 输出文件

每次测试会在 `reports/<test_id>/` 下生成：

- `raw_data.csv`
- `report.md`
- `report.html`
- `events.log`
- `config_snapshot.yaml`

其中 `reports/` 默认不纳入 Git 跟踪，适合保存本地测试结果。

## 核心指标

- response_delay_s：响应延迟
- rise_time_s：上升时间
- settling_time_s：调节时间
- overshoot_pct：超调量
- steady_state_error_deg：稳态误差
- peak_current_a：峰值电流
- average_current_a：平均电流
- max_temperature_c：最高温度
- jitter_deg：稳态抖动

## Ti5 首次上机顺序

真实 Ti5 / TwinCAT ADS 首次上机按三阶段执行：

1. Stage A：只执行 `Protocol Setup -> Validate -> Scan` 和使能状态检查，不下发运动。
2. Stage B：加载 `ti5_ads_position_step_1deg.yaml`，执行 1 deg 小幅阶跃。
3. Stage C：加载 `ti5_ads_position_step_5deg.yaml`，执行 5 deg 验收阶跃。

真实工位的 AMS Net ID、Ti5 identity、编码器/减速比、方向、零偏和治具限位建议复制到 `data/stations/ti5_ads/` 下维护；`data/` 默认不纳入 Git 跟踪。

## 产品设计报告

- [JointBench Product Design Report](./JointBench_Product_Design_Report.md)
- [V1 CiA402 Communication Upgrade Plan](./docs/V1_CiA402_Communication_Upgrade_Plan.md)

## 后续方向

- CANopen CiA402 / EtherCAT CoE CiA402 真实通信适配增强
- TwinCAT PLC 工程模板自动化生成
- UART 舵机私有协议适配
- 多测试模式：速度响应、电流限制、温升、重复定位
- SQLite 历史记录
- 批量报告导出
- 产线 SN / 条码绑定
- PDF 报告
