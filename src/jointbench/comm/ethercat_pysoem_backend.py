from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable

from jointbench.cia402 import (
    ACTUAL_POSITION,
    ACTUAL_VELOCITY,
    CONTROLWORD,
    ERROR_CODE,
    MODE_DISPLAY,
    MODE_OF_OPERATION,
    STATUSWORD,
    TARGET_POSITION,
)
from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult
from jointbench.exceptions import CommunicationTimeout, ConfigurationError


@dataclass(frozen=True)
class EthercatIdentity:
    vendor_id: int
    product_code: int
    revision_number: int
    serial_number: int = 0


class PySoemEthercatBackend:
    """Minimal EtherCAT CoE SDO backend for CiA402 commissioning tests."""

    def __init__(self, bundle: ProtocolConfigBundle, master_factory: Callable[[], Any] | None = None) -> None:
        self.bundle = bundle
        self.master_factory = master_factory or self._default_master_factory
        self.master = None
        self.slave = None
        self.identity: EthercatIdentity | None = None
        self.connected = False
        self.controlword = 0
        self.mode = 0
        self.target_position_counts = 0
        self._last_actual_position = 0
        self._last_actual_velocity = 0
        self._last_error_code = 0

    def connect(self) -> None:
        if self.connected:
            return
        interface = self.bundle.bus.interface
        if not interface:
            raise ConfigurationError("EtherCAT interface is required.")
        self.master = self.master_factory()
        self.master.open(interface)
        slave_count = self.master.config_init()
        if slave_count <= 0:
            raise CommunicationTimeout(f"No EtherCAT slaves found on interface {interface}.")
        self.slave = self._select_slave()
        self.identity = self._read_identity_from_slave(self.slave)
        self.connected = True

    def disconnect(self) -> None:
        if self.master is not None:
            close = getattr(self.master, "close", None)
            if callable(close):
                close()
        self.connected = False
        self.master = None
        self.slave = None

    def scan(self) -> list[ScanResult]:
        close_after_scan = not self.connected
        self.connect()
        assert self.master is not None
        results: list[ScanResult] = []
        for index, slave in enumerate(getattr(self.master, "slaves", [])):
            identity = self._read_identity_from_slave(slave)
            match = self.bundle.device.matches_identity(
                vendor_id=identity.vendor_id,
                product_code=identity.product_code,
                revision_number=identity.revision_number,
            )
            results.append(
                ScanResult(
                    protocol=ProtocolType.ETHERCAT_COE_CIA402,
                    slave_index=index,
                    vendor_id=identity.vendor_id,
                    product_code=identity.product_code,
                    revision_number=identity.revision_number,
                    state="detected",
                    match=match,
                    message="EtherCAT slave detected by pysoem.",
                )
            )
        if close_after_scan:
            self.disconnect()
        return results

    def read_identity(self) -> EthercatIdentity:
        self._ensure_connected()
        assert self.identity is not None
        return self.identity

    def read_statusword(self) -> int:
        return self._sdo_read_u16(*STATUSWORD)

    def write_controlword(self, value: int) -> None:
        self.controlword = int(value)
        self._sdo_write_u16(*CONTROLWORD, value=self.controlword)

    def write_mode(self, mode: int) -> None:
        self.mode = int(mode)
        self._sdo_write_i8(*MODE_OF_OPERATION, value=self.mode)

    def read_mode(self) -> int:
        return self._sdo_read_i8(*MODE_DISPLAY)

    def write_target_position(self, counts: int) -> None:
        self.target_position_counts = int(counts)
        self._sdo_write_i32(*TARGET_POSITION, value=self.target_position_counts)

    def cycle(self, dt_s: float) -> None:
        del dt_s
        self._last_actual_position = self.read_actual_position()
        try:
            self._last_actual_velocity = self.read_actual_velocity()
        except Exception:
            self._last_actual_velocity = 0
        try:
            self._last_error_code = self.read_error_code()
        except Exception:
            self._last_error_code = 0

    def read_actual_position(self) -> int:
        return self._sdo_read_i32(*ACTUAL_POSITION)

    def read_actual_velocity(self) -> int:
        return self._sdo_read_i32(*ACTUAL_VELOCITY)

    def read_current(self) -> float:
        # Torque/current objects are vendor-dependent in many joint modules.
        return 0.0

    def read_temperature(self) -> float:
        return 25.0

    def read_error_code(self) -> int:
        return self._sdo_read_u16(*ERROR_CODE)

    def quick_stop(self) -> None:
        self.write_controlword(0x0002)

    def _select_slave(self):
        assert self.master is not None
        slaves = getattr(self.master, "slaves", [])
        index = self.bundle.bus.slave_index or 0
        if index < 0 or index >= len(slaves):
            raise ConfigurationError(f"EtherCAT slave_index {index} is outside detected range 0..{len(slaves)-1}.")
        return slaves[index]

    def _read_identity_from_slave(self, slave) -> EthercatIdentity:
        vendor_id = _first_attr(slave, ("man", "manufacturer", "vendor_id"), 0)
        product_code = _first_attr(slave, ("id", "product_code"), 0)
        revision_number = _first_attr(slave, ("rev", "revision", "revision_number"), 0)
        serial_number = _first_attr(slave, ("serial", "serial_number"), 0)
        if not vendor_id:
            try:
                vendor_id = self._sdo_read_u32_from(slave, 0x1018, 0x01)
                product_code = self._sdo_read_u32_from(slave, 0x1018, 0x02)
                revision_number = self._sdo_read_u32_from(slave, 0x1018, 0x03)
                serial_number = self._sdo_read_u32_from(slave, 0x1018, 0x04)
            except Exception:
                pass
        return EthercatIdentity(
            vendor_id=int(vendor_id or 0),
            product_code=int(product_code or 0),
            revision_number=int(revision_number or 0),
            serial_number=int(serial_number or 0),
        )

    def _sdo_read_i8(self, index: int, subindex: int) -> int:
        return int.from_bytes(self._sdo_read(index, subindex), "little", signed=True)

    def _sdo_read_u16(self, index: int, subindex: int) -> int:
        return int.from_bytes(self._sdo_read(index, subindex), "little", signed=False)

    def _sdo_read_i32(self, index: int, subindex: int) -> int:
        return int.from_bytes(self._sdo_read(index, subindex), "little", signed=True)

    def _sdo_read_u32_from(self, slave, index: int, subindex: int) -> int:
        return int.from_bytes(self._sdo_read_from(slave, index, subindex), "little", signed=False)

    def _sdo_write_i8(self, index: int, subindex: int, *, value: int) -> None:
        self._sdo_write(index, subindex, int(value).to_bytes(1, "little", signed=True))

    def _sdo_write_u16(self, index: int, subindex: int, *, value: int) -> None:
        self._sdo_write(index, subindex, int(value).to_bytes(2, "little", signed=False))

    def _sdo_write_i32(self, index: int, subindex: int, *, value: int) -> None:
        self._sdo_write(index, subindex, int(value).to_bytes(4, "little", signed=True))

    def _sdo_read(self, index: int, subindex: int) -> bytes:
        self._ensure_connected()
        assert self.slave is not None
        return self._sdo_read_from(self.slave, index, subindex)

    def _sdo_read_from(self, slave, index: int, subindex: int) -> bytes:
        try:
            data = slave.sdo_read(index, subindex)
        except Exception as exc:
            raise CommunicationTimeout(f"SDO read failed at 0x{index:04X}:{subindex:02X}: {exc}") from exc
        if isinstance(data, int):
            return int(data).to_bytes(4, "little", signed=False)
        return bytes(data)

    def _sdo_write(self, index: int, subindex: int, data: bytes) -> None:
        self._ensure_connected()
        assert self.slave is not None
        try:
            self.slave.sdo_write(index, subindex, data)
        except Exception as exc:
            raise CommunicationTimeout(f"SDO write failed at 0x{index:04X}:{subindex:02X}: {exc}") from exc

    def _ensure_connected(self) -> None:
        if not self.connected or self.slave is None:
            raise CommunicationTimeout("EtherCAT backend is not connected.")

    @staticmethod
    def _default_master_factory():
        try:
            import pysoem
        except ImportError as exc:
            raise ConfigurationError(
                'EtherCAT support requires optional dependencies. Install with: python -m pip install -e ".[ethercat]"'
            ) from exc
        return pysoem.Master()


def _first_attr(obj: Any, names: tuple[str, ...], default: Any = None) -> Any:
    for name in names:
        if hasattr(obj, name):
            return getattr(obj, name)
    return default
