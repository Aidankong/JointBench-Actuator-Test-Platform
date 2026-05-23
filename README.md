# JointBench Actuator Test Platform

机器人关节模组测试与性能分析平台。

JointBench 面向机器人关节、电机模组、伺服执行器和产线测试场景，目标是通过上位机下发测试指令，采集位置、速度、电流、电压、温度等数据，自动计算超调量、调节时间、稳态误差、峰值电流等指标，并生成可追溯测试报告。

## 当前内容

- [产品设计报告](./JointBench_Product_Design_Report.md)

## 规划方向

- Mock 执行器模型
- PySide6 / PyQt 上位机
- 位置阶跃响应测试
- 实时曲线显示
- CSV 数据记录
- 阶跃响应指标分析
- Markdown / HTML / PDF 报告生成
- UART / CAN / RS485 / TCP 通信适配
- 产线 PASS / FAIL 自动判定

## 推荐仓库名

GitHub 仓库 URL 建议使用：

```text
JointBench-Actuator-Test-Platform
```

项目展示名称继续使用：

```text
JointBench Actuator Test Platform
```
