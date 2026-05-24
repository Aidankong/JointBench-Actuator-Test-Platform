from __future__ import annotations

from jointbench.cia402 import CiA402Scaling, enable_operation, mode_value, parse_statusword
from jointbench.comm.base_adapter import BaseAdapter
from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult
from jointbench.exceptions import ConfigurationError, DeviceIdentityMismatch, SafetyLimitViolation
from jointbench.models import ActuatorState, DeviceInfo


class CiA402AdapterBase(BaseAdapter):
    adapter_name = "CiA402"
    transport_mode = "CiA402"

    def __init__(self, bundle: ProtocolConfigBundle, backend) -> None:
        self.bundle = bundle
        self.backend = backend
        self.scaling = CiA402Scaling(bundle.scaling)
        self._connected = False
        self._enabled = False
        self._target_position_deg = bundle.test_config.start_position_deg
        self._control_mode = "position"
        self._operation_enabled: bool | None = None

    @property
    def config_files(self) -> dict[str, str]:
        return self.bundle.config_files

    @property
    def config_hashes(self) -> dict[str, str]:
        return self.bundle.config_hashes

    @property
    def operation_enabled(self) -> bool | None:
        return self._operation_enabled

    @classmethod
    def scan_devices(cls, bundle: ProtocolConfigBundle) -> list[ScanResult]:
        backend = cls._make_backend(bundle)
        return backend.scan()

    @classmethod
    def _make_backend(cls, bundle: ProtocolConfigBundle):
        raise NotImplementedError

    def connect(self) -> None:
        self.backend.connect()
        self._connected = True
        identity = self.backend.read_identity()
        if not self.bundle.device.matches_identity(
            vendor_id=identity.vendor_id,
            product_code=identity.product_code,
            revision_number=identity.revision_number,
        ):
            raise DeviceIdentityMismatch("Connected device identity does not match configured profile.")

    def disconnect(self) -> None:
        self.backend.disconnect()
        self._connected = False
        self._enabled = False

    def is_connected(self) -> bool:
        return self._connected

    def read_device_info(self) -> DeviceInfo:
        identity = self.backend.read_identity()
        return DeviceInfo(
            device_id=f"{self.adapter_name}-{identity.vendor_id:08X}-{identity.product_code:08X}",
            sn=str(identity.serial_number),
            firmware_version=f"revision-{identity.revision_number:08X}",
            adapter_type=self.adapter_name,
            hardware_version=self.bundle.device.name,
            protocol=self.bundle.protocol.value,
            vendor_id=identity.vendor_id,
            product_code=identity.product_code,
            revision_number=identity.revision_number,
            node_id=self.bundle.bus.node_id,
            slave_index=self.bundle.bus.slave_index,
            transport_mode=self.transport_mode,
        )

    def set_enable(self, enabled: bool) -> None:
        if not enabled:
            self.backend.write_controlword(0x0000)
            self._enabled = False
            self._operation_enabled = False
            return
        if self.bundle.safety is None:
            raise ConfigurationError("Safety limits are required before enabling a real CiA402 device.")
        enable_operation(self.backend.read_statusword, self.backend.write_controlword)
        self._enabled = True
        self._operation_enabled = True

    def set_control_mode(self, mode: str) -> None:
        if mode != "position":
            raise ValueError("V1 CiA402 adapter supports position mode only.")
        preferred_mode = self.bundle.device.preferred_mode or "profile_position"
        self.backend.write_mode(mode_value(preferred_mode))
        self._control_mode = mode

    def send_position_command(self, position_deg: float) -> None:
        self._check_position_command(position_deg)
        self._target_position_deg = position_deg
        self.backend.write_target_position(self.scaling.deg_to_counts(position_deg))

    def read_state(self, timestamp_s: float) -> ActuatorState:
        return self._state(timestamp_s)

    def step(self, dt_s: float, timestamp_s: float) -> ActuatorState:
        self.backend.cycle(dt_s)
        return self._state(timestamp_s)

    def emergency_stop(self) -> None:
        self.backend.quick_stop()
        self._enabled = False
        self._operation_enabled = False

    def _state(self, timestamp_s: float) -> ActuatorState:
        actual_counts = self.backend.read_actual_position()
        velocity_counts = self.backend.read_actual_velocity()
        statusword = self.backend.read_statusword()
        current_a = self.scaling.raw_current_to_a(self.backend.read_current())
        temperature_c = self.scaling.raw_temperature_to_c(self.backend.read_temperature())
        return ActuatorState(
            timestamp_s=timestamp_s,
            target_position_deg=self._target_position_deg,
            actual_position_deg=self.scaling.counts_to_deg(actual_counts),
            actual_speed_dps=self.scaling.counts_per_second_to_dps(velocity_counts),
            current_a=current_a,
            voltage_v=24.0,
            temperature_c=temperature_c,
            fault_code=self.backend.read_error_code(),
            enabled=self._enabled,
            control_mode=self._control_mode,
            protocol=self.bundle.protocol.value,
            statusword=statusword,
            controlword=self.backend.controlword,
        )

    def _check_position_command(self, position_deg: float) -> None:
        safety = self.bundle.safety
        if safety and safety.min_position_deg is not None and position_deg < safety.min_position_deg:
            raise SafetyLimitViolation(f"Target {position_deg:.2f}deg is below min limit {safety.min_position_deg:.2f}deg.")
        if safety and safety.max_position_deg is not None and position_deg > safety.max_position_deg:
            raise SafetyLimitViolation(f"Target {position_deg:.2f}deg is above max limit {safety.max_position_deg:.2f}deg.")
        if self.bundle.protocol is not ProtocolType.MOCK and not self.bundle.bus.is_fake_transport and abs(position_deg) > 5.0:
            raise SafetyLimitViolation("First real-device V1 command is limited to +/-5 deg.")

    def validate_device(self) -> bool:
        identity = self.backend.read_identity()
        return self.bundle.device.matches_identity(
            vendor_id=identity.vendor_id,
            product_code=identity.product_code,
            revision_number=identity.revision_number,
        )

    def validate_object_map(self) -> bool:
        return all(self.bundle.device.object_map.required_items().values())

    def current_state_name(self) -> str:
        return parse_statusword(self.backend.read_statusword()).value
