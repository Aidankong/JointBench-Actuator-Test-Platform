# JointBench V1 CiA402 通信升级开发计划

日期：2026-05-24  
适用版本：V1 规划  
目标协议：CANopen CiA402、EtherCAT CoE CiA402  
相关设备：机器人关节模组、伺服驱动器、支持 CiA402 的执行器

---

## 1. 可行性评估

### 1.1 结论

V1 增加 CiA402 真实通信与控制能力是可行的，且非常适合 JointBench 的产品定位。

推荐实施顺序：

1. CANopen CiA402
2. EtherCAT CoE CiA402
3. UART 舵机私有协议

原因：

- 当前 MVP 已经完成 Mock、测试流程、数据采集、分析和报告闭环。
- CiA402 是标准化驱动器设备模型，可以抽象出统一状态机和控制接口。
- CANopen 与 EtherCAT CoE 在上层对象字典和 CiA402 控制语义上高度相似，可共享大部分控制逻辑。
- 真实通信适配层可以在不破坏 MVP 的前提下扩展。

### 1.2 可行性等级

| 能力 | 可行性 | 说明 |
|---|---|---|
| CANopen CiA402 设备识别 | 高 | 可扫描 Node ID，并读取对象字典中的 Identity Object |
| CANopen CiA402 位置控制 | 高 | 基于 SDO/PDO、Controlword、Statusword、Mode of Operation 实现 |
| EtherCAT CoE 设备识别 | 高 | EtherCAT master 可扫描 slave，结合 ESI XML 识别设备 |
| EtherCAT CoE 周期控制 | 中高 | Python 可用 pysoem/SOEM，但 Windows 非硬实时，适合测试台架，不适合作为高实时控制器 |
| 协议自动检测 | 中 | 可检测协议、节点、对象字典、PDO 信息，但不能完全自动推断安全限位、减速比、方向和单位换算 |
| 配置文件即接入 | 中高 | 标准 CiA402 可以高度配置化；厂家扩展对象仍需设备 profile |
| UART 舵机自动接入 | 中低 | 多为厂家私有协议，需要单独协议适配器 |

### 1.3 关键限制

即使设备支持 CiA402，也不能只靠“协议类型”准确控制。至少还需要：

- 总线参数。
- 节点地址或 EtherCAT slave 位置。
- 设备 EDS/DCF 或 ESI 文件。
- PDO 映射。
- 位置、速度、电流单位换算。
- 减速比、编码器分辨率、方向定义。
- 安全限位和测试阈值。

自动检测可以降低配置成本，但不能替代安全配置。

---

## 2. V1 目标范围

### 2.1 V1 必做

- 增加通信协议配置文件导入能力。
- 增加前端配置上传窗口。
- 支持 CANopen CiA402 设备扫描与连接。
- 支持 EtherCAT CoE CiA402 设备扫描与连接的架构与基础实现。
- 实现通用 CiA402 状态机：
  - Not Ready to Switch On
  - Switch On Disabled
  - Ready to Switch On
  - Switched On
  - Operation Enabled
  - Fault
- 支持 Profile Position Mode 或 Cyclic Synchronous Position 的一种作为首个真实控制模式。
- 将真实设备状态转换为 MVP 已有的 `ActuatorState`。
- 真实通信数据复用现有阶跃测试、分析、CSV 和报告模块。
- 增加通信异常、状态机异常、PDO 缺失、配置校验失败的日志和阻断。

### 2.2 V1 不做

- 不实现整机多轴同步运动控制。
- 不承诺 Windows 下 EtherCAT 硬实时性能。
- 不自动推断机械安全限位。
- 不对未知厂家私有协议做自动控制。
- 不将 UART 舵机作为 V1 主线，只预留后续扩展接口。

---

## 3. 配置文件设计

### 3.1 配置文件分层

建议将配置拆成 4 类：

```text
configs/
  buses/
    canopen_pcan_1m.yaml
    ethercat_nic.yaml
  devices/
    vendor_joint_cia402.yaml
  safety/
    default_joint_limits.yaml
  tests/
    position_step_30deg.yaml
```

分层原因：

- 总线配置会随工位变化。
- 设备配置会随型号变化。
- 安全配置会随治具、负载和测试场景变化。
- 测试配置会随测试项目变化。

### 3.2 CANopen CiA402 总线配置示例

```yaml
protocol: canopen_cia402

can:
  interface: pcan
  channel: PCAN_USBBUS1
  bitrate: 1000000
  node_id: 1
  heartbeat_timeout_ms: 500
  sdo_timeout_ms: 300

eds:
  file: configs/vendor/vendor_joint.eds
```

### 3.3 EtherCAT CoE CiA402 总线配置示例

```yaml
protocol: ethercat_coe_cia402

ethercat:
  interface: "Ethernet 2"
  slave_index: 0
  cycle_time_ms: 1
  distributed_clock: true

esi:
  file: configs/vendor/vendor_joint.xml
```

### 3.4 设备 Profile 配置示例

```yaml
device:
  name: Vendor Joint 40
  vendor_id: 0x00000001
  product_code: 0x00004001
  revision_number: 0x00010000
  serial_number: null

cia402:
  controlword: "0x6040:00"
  statusword: "0x6041:00"
  mode_of_operation: "0x6060:00"
  mode_display: "0x6061:00"
  target_position: "0x607A:00"
  actual_position: "0x6064:00"
  target_velocity: "0x60FF:00"
  actual_velocity: "0x606C:00"
  target_torque: "0x6071:00"
  actual_torque: "0x6077:00"
  error_code: "0x603F:00"

control:
  preferred_mode: profile_position
  homing_required: false
  fault_reset_on_connect: false
```

### 3.5 单位换算配置示例

```yaml
scaling:
  encoder_counts_per_rev: 524288
  gear_ratio: 9.0
  position_direction: 1
  zero_offset_deg: 0.0
  velocity_unit: counts_per_second
  current_scale_a_per_unit: 0.001
  temperature_scale_c_per_unit: 0.1
```

### 3.6 安全配置示例

```yaml
limits:
  min_position_deg: -90
  max_position_deg: 90
  max_speed_dps: 360
  max_current_a: 8
  max_temperature_c: 75
  max_following_error_deg: 5
  communication_timeout_ms: 500

safe_stop:
  strategy: quick_stop
  disable_after_stop: true
```

### 3.7 测试配置示例

```yaml
test:
  type: position_step_response
  start_position_deg: 0
  target_position_deg: 30
  duration_s: 3
  sample_rate_hz: 100

pass_fail:
  max_overshoot_pct: 10
  max_settling_time_s: 0.6
  max_steady_state_error_deg: 0.5
  max_peak_current_a: 8
  max_temperature_c: 75
```

---

## 4. 前端升级设计

### 4.1 新增页面

在现有 PySide6 主界面中新增 `Communication Setup` 配置窗口。

入口：

- 左侧设备区增加 `Protocol Setup` 按钮。
- 顶部菜单增加 `File -> Import Protocol Config`。

窗口包含：

- 协议类型选择：Mock、CANopen CiA402、EtherCAT CoE CiA402、UART Servo。
- 配置文件上传：
  - Bus config YAML。
  - Device profile YAML。
  - Safety config YAML。
  - Test config YAML。
  - EDS/DCF 文件。
  - ESI XML 文件。
- 自动检测按钮：
  - Scan CANopen Nodes。
  - Scan EtherCAT Slaves。
  - Validate CiA402 Object Map。
- 检测结果表：
  - 协议。
  - 节点 ID / slave index。
  - Vendor ID。
  - Product Code。
  - Revision。
  - 支持的控制模式。
  - 状态机状态。
  - 配置匹配结果。
- 校验结果：
  - 必填对象是否存在。
  - PDO 映射是否完整。
  - 单位换算是否配置。
  - 安全阈值是否配置。
  - 是否允许进入 Operation Enabled。

### 4.2 自动检测能力

CANopen 自动检测：

- 扫描 Node ID 1 到 127。
- 读取 `0x1000 Device Type`。
- 读取 `0x1018 Identity Object`。
- 读取 `0x6041 Statusword`。
- 读取 `0x6061 Mode Display`。
- 尝试读取 `0x6502 Supported Drive Modes`。
- 根据 EDS/设备 profile 校验对象字典。

EtherCAT 自动检测：

- 扫描 EtherCAT slave topology。
- 读取 slave index、vendor id、product code、revision。
- 匹配 ESI XML。
- 读取 CoE 对象字典。
- 校验 PDO 输入输出映射。
- 校验 CiA402 必需对象。

### 4.3 自动检测边界

可以自动检测：

- 总线上是否有设备。
- 设备是否疑似 CiA402。
- 设备身份信息。
- 对象字典是否包含必要对象。
- 当前状态机状态。
- 部分支持的控制模式。

不能可靠自动检测：

- 关节机械零点。
- 正反方向是否符合机械安装。
- 减速比。
- 编码器真实分辨率。
- 安全运动范围。
- 最大允许电流和温度。
- 测试台架是否有机械干涉。

因此，自动检测后必须经过配置校验和人工确认，才能允许下发使能和运动命令。

---

## 5. 后端架构升级

### 5.1 新增模块

```text
src/jointbench/
  config/
    loader.py
    schemas.py
    validator.py
  comm/
    canopen_cia402_adapter.py
    ethercat_cia402_adapter.py
  cia402/
    object_dictionary.py
    state_machine.py
    scaling.py
    modes.py
  app/
    protocol_setup_dialog.py
```

### 5.2 共享 CiA402 控制层

CANopen 与 EtherCAT 共享：

- 对象索引定义。
- Controlword 生成。
- Statusword 解析。
- 状态机迁移。
- Profile Position / CSP 控制模式封装。
- 单位换算。
- 错误码解析。

协议适配器只负责：

- SDO 读写。
- PDO 周期数据收发。
- 总线扫描。
- 连接与断开。
- 通信异常转换。

### 5.3 依赖建议

CANopen：

- `python-can`
- `canopen`

EtherCAT：

- `pysoem`
- Windows 需要 Npcap 或兼容抓包驱动。
- 建议优先用独立网口，避免与办公网络共用。

说明：

- CANopen 更适合作为 V1 第一个真实通信目标。
- EtherCAT 可在 V1 做基础扫描、对象读取和单轴低速测试；高实时周期控制建议 V1.1 或 V2 深化。

---

## 6. 开发阶段拆分

### 阶段 1：配置系统与前端上传

交付：

- YAML 配置加载器。
- EDS/ESI 文件路径管理。
- 配置校验器。
- `Communication Setup` 前端窗口。
- Mock 协议配置也通过同一窗口加载。

验收：

- 可以上传 bus/device/safety/test 配置。
- 错误配置能给出明确提示。
- 配置通过后可保存为当前设备 profile。

### 阶段 2：CiA402 通用层

交付：

- CiA402 对象定义。
- Controlword / Statusword 解析。
- 状态机迁移。
- 单位换算。
- Profile Position Mode 封装。

验收：

- 使用离线样例状态字可正确判断状态。
- 状态机迁移输出正确 controlword。
- 位置 counts 与 deg 可双向转换。

### 阶段 3：CANopen CiA402 接入

交付：

- CANopen 适配器。
- Node 扫描。
- Identity Object 读取。
- SDO 初始化。
- 位置命令下发。
- 状态读取并转换为 `ActuatorState`。

验收：

- 能扫描并识别 CANopen CiA402 设备。
- 能进入 Operation Enabled。
- 能执行低速、小角度位置阶跃测试。
- 异常时能 quick stop 或 disable voltage。
- 测试数据复用现有 CSV、分析和报告。

### 阶段 4：EtherCAT CoE CiA402 接入

交付：

- EtherCAT 适配器。
- Slave 扫描。
- ESI 匹配。
- CoE 对象读取。
- PDO 映射校验。
- 单轴位置测试。

验收：

- 能识别 EtherCAT slave。
- 能校验 ESI 与设备身份。
- 能读取 statusword 和 actual position。
- 能在安全低速配置下执行位置测试。

### 阶段 5：用户体验与安全收口

交付：

- 前端协议自动检测结果展示。
- 配置缺失阻断。
- 操作前安全确认。
- 通信异常日志。
- 报告中记录协议、配置文件 hash 和设备身份信息。

验收：

- 没有安全限位配置时不能使能真实电机。
- 配置文件与设备身份不匹配时不能启动测试。
- 断连、故障、超限都会停止测试并写入报告。

---

## 7. V1 验收标准

### 软件验收

- Mock 流程不受影响。
- 配置上传窗口可用。
- YAML 配置校验可用。
- CiA402 状态机单元测试通过。
- CANopen 适配器可通过模拟或离线测试验证 SDO/PDO 映射逻辑。
- EtherCAT 适配器至少完成扫描与对象读取的结构验证。

### 硬件验收

最小真实硬件验收建议：

- 1 个支持 CiA402 的 CANopen 驱动器或关节模组。
- 1 个 CAN 适配器。
- 可限流电源。
- 机械空载或安全治具。
- 物理急停或可断电开关。

验收动作：

1. 导入配置。
2. 自动扫描设备。
3. 匹配设备 identity。
4. 进入 Operation Enabled。
5. 执行小角度位置阶跃，例如 5 deg。
6. 保存数据。
7. 生成报告。
8. 断开或故障时安全停止。

---

## 8. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| 设备虽支持 CiA402 但 PDO 映射不同 | 无法直接读写目标对象 | 使用设备 profile 和 EDS/ESI 校验 |
| Windows EtherCAT 实时性不足 | 周期控制抖动 | V1 限定低速测试，优先 CANopen 真机闭环 |
| 单位换算错误 | 运动幅度错误，有安全风险 | 配置校验 + 小角度首次测试 + 软限位 |
| 自动检测误判设备能力 | 错误使能或错误模式 | 检测结果只作为建议，必须人工确认配置 |
| 厂家扩展对象不公开 | 部分状态无法读取 | 允许 profile 扩展字段，必要时写厂商插件 |
| 真实电机失控风险 | 损坏设备或治具 | 限流电源、物理急停、默认低速低幅值测试 |

---

## 9. 结论

V1 支持 CiA402 是可行的，并且建议作为 JointBench 从 Mock MVP 走向真实机器人关节测试平台的核心升级方向。

最稳妥路线是：

1. 先做配置文件系统和前端上传窗口。
2. 再做通用 CiA402 状态机。
3. 先接 CANopen CiA402 真机。
4. 再接 EtherCAT CoE CiA402。
5. 全程保持 Mock 作为回归测试基准。

自动检测可以显著提升易用性，但必须被定义为“辅助识别和配置校验”，不能替代机械安全参数和人工确认。
