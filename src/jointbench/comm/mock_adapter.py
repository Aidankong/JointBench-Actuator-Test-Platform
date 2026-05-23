from __future__ import annotations

import math
import random

from jointbench.comm.base_adapter import BaseAdapter
from jointbench.models import ActuatorState, DeviceInfo


class MockActuatorAdapter(BaseAdapter):
    """Deterministic actuator model for hardware-free MVP testing."""

    def __init__(
        self,
        *,
        seed: int = 7,
        natural_frequency: float = 14.0,
        damping_ratio: float = 0.76,
        max_speed_dps: float = 260.0,
        noise_std_deg: float = 0.025,
    ) -> None:
        self._rng = random.Random(seed)
        self._connected = False
        self._enabled = False
        self._control_mode = "position"
        self._target_position_deg = 0.0
        self._position_deg = 0.0
        self._velocity_dps = 0.0
        self._temperature_c = 31.0
        self._voltage_v = 24.1
        self._fault_code = 0
        self._natural_frequency = natural_frequency
        self._damping_ratio = damping_ratio
        self._max_speed_dps = max_speed_dps
        self._noise_std_deg = noise_std_deg
        self._last_current_a = 0.12

    def connect(self) -> None:
        self._connected = True

    def disconnect(self) -> None:
        self._connected = False
        self._enabled = False

    def is_connected(self) -> bool:
        return self._connected

    def read_device_info(self) -> DeviceInfo:
        self._require_connected()
        return DeviceInfo(
            device_id="MOCK-JOINT-001",
            sn="SN-MOCK-20260524-001",
            firmware_version="mock-fw-v0.1",
            adapter_type="Mock",
        )

    def set_enable(self, enabled: bool) -> None:
        self._require_connected()
        self._enabled = enabled
        if not enabled:
            self._velocity_dps = 0.0

    def set_control_mode(self, mode: str) -> None:
        self._require_connected()
        if mode != "position":
            raise ValueError("Mock MVP supports position mode only.")
        self._control_mode = mode

    def send_position_command(self, position_deg: float) -> None:
        self._require_ready()
        self._target_position_deg = float(position_deg)

    def reset(self, position_deg: float = 0.0) -> None:
        self._target_position_deg = float(position_deg)
        self._position_deg = float(position_deg)
        self._velocity_dps = 0.0
        self._temperature_c = 31.0
        self._fault_code = 0
        self._last_current_a = 0.12

    def read_state(self, timestamp_s: float) -> ActuatorState:
        return self._state(timestamp_s)

    def step(self, dt_s: float, timestamp_s: float) -> ActuatorState:
        self._require_ready()
        error = self._target_position_deg - self._position_deg
        accel = (
            self._natural_frequency**2 * error
            - 2.0 * self._damping_ratio * self._natural_frequency * self._velocity_dps
        )
        self._velocity_dps += accel * dt_s
        self._velocity_dps = max(-self._max_speed_dps, min(self._max_speed_dps, self._velocity_dps))
        self._position_deg += self._velocity_dps * dt_s

        dynamic_current = 0.18 + 0.0045 * abs(self._velocity_dps) + 0.0009 * abs(accel)
        self._last_current_a = min(4.6, dynamic_current)
        heat_gain = 0.012 * self._last_current_a * self._last_current_a * dt_s
        cooling = 0.018 * max(0.0, self._temperature_c - 31.0) * dt_s
        self._temperature_c += heat_gain - cooling
        self._voltage_v = 24.1 - min(0.45, 0.045 * self._last_current_a)
        return self._state(timestamp_s)

    def emergency_stop(self) -> None:
        self._enabled = False
        self._velocity_dps = 0.0

    def _state(self, timestamp_s: float) -> ActuatorState:
        measured_position = self._position_deg + self._rng.gauss(0.0, self._noise_std_deg)
        if not math.isfinite(measured_position):
            self._fault_code = 1
        return ActuatorState(
            timestamp_s=timestamp_s,
            target_position_deg=self._target_position_deg,
            actual_position_deg=measured_position,
            actual_speed_dps=self._velocity_dps,
            current_a=self._last_current_a,
            voltage_v=self._voltage_v,
            temperature_c=self._temperature_c,
            fault_code=self._fault_code,
            enabled=self._enabled,
            control_mode=self._control_mode,
        )

    def _require_connected(self) -> None:
        if not self._connected:
            raise RuntimeError("Mock actuator is not connected.")

    def _require_ready(self) -> None:
        self._require_connected()
        if not self._enabled:
            raise RuntimeError("Mock actuator is not enabled.")
