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

## 快速开始

```powershell
python -m pip install -r requirements.txt
python -m pip install -e .
python -m jointbench
```

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
```

## 输出文件

每次测试会在 `reports/<test_id>/` 下生成：

- `raw_data.csv`
- `report.md`
- `report.html`

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

## 产品设计报告

- [JointBench Product Design Report](./JointBench_Product_Design_Report.md)
- [V1 CiA402 Communication Upgrade Plan](./docs/V1_CiA402_Communication_Upgrade_Plan.md)

## 后续方向

- CANopen CiA402 / EtherCAT CoE CiA402 真实通信适配
- 协议配置文件上传、校验和自动检测
- UART 舵机私有协议适配
- 多测试模式：速度响应、电流限制、温升、重复定位
- SQLite 历史记录
- 批量报告导出
- 产线 SN / 条码绑定
- PDF 报告
