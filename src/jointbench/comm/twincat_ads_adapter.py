from __future__ import annotations

import time

from jointbench.comm.ads_symbols import AdsSymbolMap
from jointbench.comm.base_adapter import BaseAdapter
from jointbench.comm.fake_ads_backend import FakeAdsBackend
from jointbench.comm.twincat_ads_backend import TwinCATAdsBackend
from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult
from jointbench.exceptions import CommunicationTimeout, ConfigurationError, SafetyLimitViolation
from jointbench.models import ActuatorState, DeviceInfo


class TwinCATAdsAdapter(BaseAdapter):
    adapter_name = "TwinCAT ADS"
    transport_mode = "TwinCAT ADS"

    def __init__(self, bundle: ProtocolConfigBundle, backend=None) -> None:
        self.bundle = bundle
        self.symbols = AdsSymbolMap.from_bundle(bundle)
        self.backend = backend or self._make_backend(bundle)
        self._connected = False
        self._enabled = False
        self._target_position_deg = bundle.test_config.start_position_deg
        self._operation_enabled: bool | None = None
        self._command_sequence = 0

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
        return cls._make_backend(bundle).scan()

    @classmethod
    def _make_backend(cls, bundle: ProtocolConfigBundle):
        if bundle.bus.host and str(bundle.bus.host).lower() == "fake":
            return FakeAdsBackend(bundle)
        return TwinCATAdsBackend(bundle)

    def connect(self) -> None:
        self.backend.connect()
        self._connected = True
        self._bump_command_sequence()

    def disconnect(self) -> None:
        self.backend.disconnect()
        self._connected = False
        self._enabled = False

    def is_connected(self) -> bool:
        return self._connected

    def read_device_info(self) -> DeviceInfo:
        return DeviceInfo(
            device_id=str(self._read("sDeviceName") or self.bundle.device.name),
            sn="TwinCAT-ADS",
            firmware_version="TwinCAT ADS / PLC",
            adapter_type=self.adapter_name,
            hardware_version=self.bundle.device.name,
            protocol=ProtocolType.TWINCAT_ADS.value,
            vendor_id=int(self._read("nVendorId") or 0) or self.bundle.device.vendor_id,
            product_code=int(self._read("nProductCode") or 0) or self.bundle.device.product_code,
            revision_number=int(self._read("nRevision") or 0) or self.bundle.device.revision_number,
            transport_mode=self.transport_mode,
            ads_host=self.bundle.bus.host,
            ams_net_id=self.bundle.bus.ams_net_id,
            ams_port=self.bundle.bus.ams_port,
            ads_symbol_prefix=self.symbols.prefix,
            twincat_route_status="fake" if self.bundle.bus.host == "fake" else "connected",
        )

    def set_enable(self, enabled: bool) -> None:
        if enabled and self.bundle.safety is None:
            raise ConfigurationError("Safety limits are required before enabling a TwinCAT ADS axis.")
        self._bump_command_sequence()
        self._write("bEnable", bool(enabled))
        if enabled:
            self._wait_for("bOperationEnabled", True, timeout_s=self.bundle.bus.timeout_ms / 1000.0)
            self._enabled = True
            self._operation_enabled = True
        else:
            self._enabled = False
            self._operation_enabled = False

    def set_control_mode(self, mode: str) -> None:
        if mode != "position":
            raise ValueError("TwinCAT ADS V1 supports position mode only.")

    def send_position_command(self, position_deg: float) -> None:
        self._check_position_command(position_deg)
        self._target_position_deg = position_deg
        self._write("fTargetPositionDeg", float(position_deg))
        self._bump_command_sequence()
        self._write("bStart", False)
        self._write("bStart", True)

    def read_state(self, timestamp_s: float) -> ActuatorState:
        return self._state(timestamp_s)

    def step(self, dt_s: float, timestamp_s: float) -> ActuatorState:
        self._bump_command_sequence()
        cycle = getattr(self.backend, "cycle", None)
        if callable(cycle):
            cycle(dt_s)
        return self._state(timestamp_s)

    def emergency_stop(self) -> None:
        self._bump_command_sequence()
        self._write("bStop", True)
        self._write("bEnable", False)
        self._enabled = False
        self._operation_enabled = False

    def _state(self, timestamp_s: float) -> ActuatorState:
        return ActuatorState(
            timestamp_s=timestamp_s,
            target_position_deg=self._target_position_deg,
            actual_position_deg=float(self._read("fActualPositionDeg") or 0.0),
            actual_speed_dps=float(self._read("fActualVelocityDps") or 0.0),
            current_a=float(self._read("fCurrentA") or 0.0),
            voltage_v=24.0,
            temperature_c=float(self._read("fTemperatureC") or 0.0),
            fault_code=int(self._read("nFaultCode") or self._read("nErrorCode") or 0),
            enabled=bool(self._read("bOperationEnabled")),
            control_mode="position",
            protocol=ProtocolType.TWINCAT_ADS.value,
            statusword=int(self._read("nStatusword") or 0),
            controlword=int(self._read("nControlword") or 0),
            command_sequence=int(self._read("nCommandSequence") or self._command_sequence),
            watchdog_ok=bool(self._read("bWatchdogOk")),
            following_error_deg=float(self._read("fFollowingErrorDeg") or 0.0),
        )

    def _read(self, key: str):
        return self.backend.read(self.symbols.name(key))

    def _write(self, key: str, value: object) -> None:
        self.backend.write(self.symbols.name(key), value)

    def _wait_for(self, key: str, expected: object, timeout_s: float) -> None:
        deadline = time.perf_counter() + timeout_s
        while time.perf_counter() < deadline:
            self._bump_command_sequence()
            if self._read(key) == expected:
                return
            time.sleep(0.02)
        raise CommunicationTimeout(f"Timed out waiting for {key}={expected}.")

    def _bump_command_sequence(self) -> None:
        self._command_sequence += 1
        self._write("nCommandSequence", self._command_sequence)

    def _check_position_command(self, position_deg: float) -> None:
        safety = self.bundle.safety
        if safety and safety.min_position_deg is not None and position_deg < safety.min_position_deg:
            raise SafetyLimitViolation(f"Target {position_deg:.2f}deg is below min limit {safety.min_position_deg:.2f}deg.")
        if safety and safety.max_position_deg is not None and position_deg > safety.max_position_deg:
            raise SafetyLimitViolation(f"Target {position_deg:.2f}deg is above max limit {safety.max_position_deg:.2f}deg.")
        if abs(position_deg) > 5.0:
            raise SafetyLimitViolation("TwinCAT ADS V1 first motion is limited to +/-5 deg.")
