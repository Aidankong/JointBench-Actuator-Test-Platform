from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path

from jointbench.models import TestConfig


class ProtocolType(str, Enum):
    MOCK = "mock"
    CANOPEN_CIA402 = "canopen_cia402"
    ETHERCAT_COE_CIA402 = "ethercat_coe_cia402"
    UART_SERVO = "uart_servo"

    @property
    def is_real_bus(self) -> bool:
        return self is not ProtocolType.MOCK


@dataclass(frozen=True)
class ConfigArtifact:
    path: Path
    sha256: str


@dataclass(frozen=True)
class BusConfig:
    protocol: ProtocolType = ProtocolType.MOCK
    interface: str | None = None
    channel: str | None = None
    bitrate: int | None = None
    node_id: int | None = None
    slave_index: int | None = None
    cycle_time_ms: float | None = None
    heartbeat_timeout_ms: int = 500
    sdo_timeout_ms: int = 300
    distributed_clock: bool = False

    @property
    def is_fake_transport(self) -> bool:
        values = [self.interface, self.channel]
        return any(str(value).lower() == "fake" for value in values if value is not None)


@dataclass(frozen=True)
class CiA402ObjectMap:
    controlword: str = "0x6040:00"
    statusword: str = "0x6041:00"
    mode_of_operation: str = "0x6060:00"
    mode_display: str = "0x6061:00"
    target_position: str = "0x607A:00"
    actual_position: str = "0x6064:00"
    target_velocity: str = "0x60FF:00"
    actual_velocity: str = "0x606C:00"
    target_torque: str = "0x6071:00"
    actual_torque: str = "0x6077:00"
    error_code: str = "0x603F:00"

    def required_items(self) -> dict[str, str]:
        return {
            "controlword": self.controlword,
            "statusword": self.statusword,
            "mode_of_operation": self.mode_of_operation,
            "mode_display": self.mode_display,
            "target_position": self.target_position,
            "actual_position": self.actual_position,
        }


@dataclass(frozen=True)
class DeviceProfile:
    name: str = "Mock Joint"
    vendor_id: int | None = None
    product_code: int | None = None
    revision_number: int | None = None
    serial_number: int | None = None
    object_map: CiA402ObjectMap = field(default_factory=CiA402ObjectMap)
    preferred_mode: str = "profile_position"
    homing_required: bool = False
    fault_reset_on_connect: bool = False

    def matches_identity(
        self,
        *,
        vendor_id: int | None,
        product_code: int | None,
        revision_number: int | None,
    ) -> bool:
        for expected, actual in (
            (self.vendor_id, vendor_id),
            (self.product_code, product_code),
            (self.revision_number, revision_number),
        ):
            if expected is not None and actual is not None and expected != actual:
                return False
        return True


@dataclass(frozen=True)
class ScalingConfig:
    encoder_counts_per_rev: int | None = None
    gear_ratio: float = 1.0
    position_direction: int = 1
    zero_offset_deg: float = 0.0
    velocity_unit: str = "counts_per_second"
    current_scale_a_per_unit: float | None = None
    temperature_scale_c_per_unit: float | None = None

    @property
    def has_position_scaling(self) -> bool:
        return bool(self.encoder_counts_per_rev and self.encoder_counts_per_rev > 0 and self.gear_ratio > 0)


@dataclass(frozen=True)
class SafetyLimits:
    min_position_deg: float | None = None
    max_position_deg: float | None = None
    max_speed_dps: float | None = None
    max_current_a: float | None = None
    max_temperature_c: float | None = None
    max_following_error_deg: float | None = None
    communication_timeout_ms: int = 500
    safe_stop_strategy: str = "quick_stop"
    disable_after_stop: bool = True

    @property
    def has_motion_limits(self) -> bool:
        return self.min_position_deg is not None and self.max_position_deg is not None


@dataclass(frozen=True)
class ProtocolConfigBundle:
    bus: BusConfig = field(default_factory=BusConfig)
    device: DeviceProfile = field(default_factory=DeviceProfile)
    scaling: ScalingConfig = field(default_factory=ScalingConfig)
    safety: SafetyLimits | None = None
    test_config: TestConfig = field(default_factory=TestConfig)
    artifacts: dict[str, ConfigArtifact] = field(default_factory=dict)

    @property
    def protocol(self) -> ProtocolType:
        return self.bus.protocol

    @property
    def config_files(self) -> dict[str, str]:
        return {key: str(artifact.path) for key, artifact in self.artifacts.items()}

    @property
    def config_hashes(self) -> dict[str, str]:
        return {key: artifact.sha256 for key, artifact in self.artifacts.items()}


@dataclass(frozen=True)
class ValidationIssue:
    level: str
    message: str


@dataclass(frozen=True)
class ValidationReport:
    issues: list[ValidationIssue] = field(default_factory=list)
    motion_allowed: bool = False

    @property
    def errors(self) -> list[ValidationIssue]:
        return [issue for issue in self.issues if issue.level == "error"]

    @property
    def warnings(self) -> list[ValidationIssue]:
        return [issue for issue in self.issues if issue.level == "warning"]

    @property
    def ok(self) -> bool:
        return not self.errors

    def summary_lines(self) -> list[str]:
        if not self.issues:
            return ["Configuration is valid."]
        return [f"[{issue.level.upper()}] {issue.message}" for issue in self.issues]


@dataclass(frozen=True)
class ScanResult:
    protocol: ProtocolType
    node_id: int | None = None
    slave_index: int | None = None
    vendor_id: int | None = None
    product_code: int | None = None
    revision_number: int | None = None
    state: str = "unknown"
    match: bool = False
    message: str = ""
