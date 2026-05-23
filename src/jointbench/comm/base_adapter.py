from __future__ import annotations

from abc import ABC, abstractmethod

from jointbench.models import ActuatorState, DeviceInfo


class BaseAdapter(ABC):
    @abstractmethod
    def connect(self) -> None:
        raise NotImplementedError

    @abstractmethod
    def disconnect(self) -> None:
        raise NotImplementedError

    @abstractmethod
    def is_connected(self) -> bool:
        raise NotImplementedError

    @abstractmethod
    def read_device_info(self) -> DeviceInfo:
        raise NotImplementedError

    @abstractmethod
    def set_enable(self, enabled: bool) -> None:
        raise NotImplementedError

    @abstractmethod
    def set_control_mode(self, mode: str) -> None:
        raise NotImplementedError

    @abstractmethod
    def send_position_command(self, position_deg: float) -> None:
        raise NotImplementedError

    @abstractmethod
    def read_state(self, timestamp_s: float) -> ActuatorState:
        raise NotImplementedError

    @abstractmethod
    def step(self, dt_s: float, timestamp_s: float) -> ActuatorState:
        raise NotImplementedError

    @abstractmethod
    def emergency_stop(self) -> None:
        raise NotImplementedError
