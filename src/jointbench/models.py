from __future__ import annotations

from dataclasses import asdict, dataclass, field
from pathlib import Path


@dataclass(frozen=True)
class DeviceInfo:
    device_id: str
    sn: str
    firmware_version: str
    adapter_type: str
    hardware_version: str = "Mock-HW-1.0"
    protocol: str = "mock"
    vendor_id: int | None = None
    product_code: int | None = None
    revision_number: int | None = None
    node_id: int | None = None
    slave_index: int | None = None
    transport_mode: str = "Mock"
    ads_host: str | None = None
    ams_net_id: str | None = None
    ams_port: int | None = None
    ads_symbol_prefix: str | None = None
    twincat_route_status: str | None = None


@dataclass
class ActuatorState:
    timestamp_s: float
    target_position_deg: float
    actual_position_deg: float
    actual_speed_dps: float
    current_a: float
    voltage_v: float
    temperature_c: float
    fault_code: int = 0
    enabled: bool = True
    control_mode: str = "position"
    protocol: str = "mock"
    statusword: int | None = None
    controlword: int | None = None

    def to_row(self, test_id: str, sample_index: int) -> dict[str, object]:
        row = asdict(self)
        row["test_id"] = test_id
        row["sample_index"] = sample_index
        return row


@dataclass(frozen=True)
class TestConfig:
    start_position_deg: float = 0.0
    target_position_deg: float = 30.0
    duration_s: float = 3.0
    sample_rate_hz: float = 100.0
    settling_band_pct: float = 2.0
    max_position_abs_deg: float = 120.0
    max_current_a: float = 5.0
    max_temperature_c: float = 70.0
    max_overshoot_pct: float = 10.0
    max_settling_time_s: float = 0.6
    max_steady_state_error_deg: float = 0.5

    @property
    def sample_period_s(self) -> float:
        return 1.0 / self.sample_rate_hz


@dataclass(frozen=True)
class StepResponseMetrics:
    response_delay_s: float | None
    rise_time_s: float | None
    settling_time_s: float | None
    overshoot_pct: float
    steady_state_error_deg: float | None
    peak_current_a: float
    average_current_a: float
    max_temperature_c: float
    jitter_deg: float | None

    def to_dict(self) -> dict[str, float | None]:
        return asdict(self)


@dataclass
class TestResult:
    test_id: str
    result: str
    device_info: DeviceInfo
    config: TestConfig
    metrics: StepResponseMetrics
    raw_data_path: Path
    report_md_path: Path
    report_html_path: Path
    output_dir: Path
    failure_reasons: list[str] = field(default_factory=list)
    aborted: bool = False
    config_files: dict[str, str] = field(default_factory=dict)
    config_hashes: dict[str, str] = field(default_factory=dict)
    operation_enabled: bool | None = None
    final_statusword: int | None = None
    final_error_code: int | None = None

    def metric_rows(self) -> list[tuple[str, str]]:
        values = self.metrics.to_dict()
        rows: list[tuple[str, str]] = []
        for key, value in values.items():
            rows.append((key, "N/A" if value is None else f"{value:.4g}"))
        return rows
