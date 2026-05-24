from __future__ import annotations

from dataclasses import dataclass

from jointbench.cia402 import Controlword, parse_statusword
from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult


@dataclass
class FakeCiA402Identity:
    vendor_id: int = 0x00000001
    product_code: int = 0x00004001
    revision_number: int = 0x00010000
    serial_number: int = 1


class FakeCiA402Backend:
    """Offline CiA402 backend used for tests and UI validation without hardware."""

    def __init__(self, bundle: ProtocolConfigBundle) -> None:
        self.bundle = bundle
        self.protocol = bundle.protocol
        self.identity = FakeCiA402Identity(
            vendor_id=bundle.device.vendor_id or 0x00000001,
            product_code=bundle.device.product_code or 0x00004001,
            revision_number=bundle.device.revision_number or 0x00010000,
            serial_number=bundle.device.serial_number or 1,
        )
        self.connected = False
        self.statusword = 0x0040
        self.controlword = 0x0000
        self.mode = 0
        self.target_position_counts = 0
        self.actual_position_counts = 0.0
        self.actual_velocity_counts_s = 0.0
        self.current_raw = 120.0
        self.temperature_raw = 310.0

    def connect(self) -> None:
        self.connected = True

    def disconnect(self) -> None:
        self.connected = False
        self.statusword = 0x0040

    def scan(self) -> list[ScanResult]:
        match = self.bundle.device.matches_identity(
            vendor_id=self.identity.vendor_id,
            product_code=self.identity.product_code,
            revision_number=self.identity.revision_number,
        )
        return [
            ScanResult(
                protocol=self.protocol,
                node_id=self.bundle.bus.node_id if self.protocol is ProtocolType.CANOPEN_CIA402 else None,
                slave_index=self.bundle.bus.slave_index if self.protocol is ProtocolType.ETHERCAT_COE_CIA402 else None,
                vendor_id=self.identity.vendor_id,
                product_code=self.identity.product_code,
                revision_number=self.identity.revision_number,
                state=parse_statusword(self.statusword).value,
                match=match,
                message="Fake CiA402 device detected.",
            )
        ]

    def read_identity(self) -> FakeCiA402Identity:
        return self.identity

    def read_statusword(self) -> int:
        return self.statusword

    def write_controlword(self, value: int) -> None:
        self.controlword = int(value)
        commands = Controlword()
        if value == commands.fault_reset:
            self.statusword = 0x0040
        elif value == commands.shutdown:
            self.statusword = 0x0021
        elif value == commands.switch_on:
            self.statusword = 0x0023
        elif value == commands.enable_operation:
            self.statusword = 0x0027
        elif value == commands.disable_voltage:
            self.statusword = 0x0040
        elif value == commands.quick_stop:
            self.statusword = 0x0007

    def write_mode(self, mode: int) -> None:
        self.mode = int(mode)

    def read_mode(self) -> int:
        return self.mode

    def write_target_position(self, counts: int) -> None:
        self.target_position_counts = int(counts)

    def cycle(self, dt_s: float) -> None:
        error = self.target_position_counts - self.actual_position_counts
        velocity_command = max(-180000.0, min(180000.0, error * 9.0))
        self.actual_velocity_counts_s += (velocity_command - self.actual_velocity_counts_s) * min(1.0, dt_s * 18.0)
        self.actual_position_counts += self.actual_velocity_counts_s * dt_s
        self.current_raw = min(4200.0, 120.0 + abs(self.actual_velocity_counts_s) * 0.004 + abs(error) * 0.002)
        self.temperature_raw += (self.current_raw / 1000.0) ** 2 * dt_s * 0.8

    def read_actual_position(self) -> int:
        return int(round(self.actual_position_counts))

    def read_actual_velocity(self) -> int:
        return int(round(self.actual_velocity_counts_s))

    def read_current(self) -> float:
        return self.current_raw

    def read_temperature(self) -> float:
        return self.temperature_raw

    def read_error_code(self) -> int:
        return 0

    def quick_stop(self) -> None:
        self.write_controlword(Controlword().quick_stop)
        self.actual_velocity_counts_s = 0.0
