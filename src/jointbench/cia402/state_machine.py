from __future__ import annotations

import time
from dataclasses import dataclass
from enum import Enum
from typing import Callable

from jointbench.exceptions import CiA402StateError


class CiA402State(str, Enum):
    NOT_READY_TO_SWITCH_ON = "Not Ready to Switch On"
    SWITCH_ON_DISABLED = "Switch On Disabled"
    READY_TO_SWITCH_ON = "Ready to Switch On"
    SWITCHED_ON = "Switched On"
    OPERATION_ENABLED = "Operation Enabled"
    QUICK_STOP_ACTIVE = "Quick Stop Active"
    FAULT_REACTION_ACTIVE = "Fault Reaction Active"
    FAULT = "Fault"
    UNKNOWN = "Unknown"


@dataclass(frozen=True)
class Controlword:
    shutdown: int = 0x0006
    switch_on: int = 0x0007
    enable_operation: int = 0x000F
    disable_voltage: int = 0x0000
    quick_stop: int = 0x0002
    fault_reset: int = 0x0080


def parse_statusword(statusword: int) -> CiA402State:
    sw = int(statusword)
    if (sw & 0x004F) == 0x0000:
        return CiA402State.NOT_READY_TO_SWITCH_ON
    if (sw & 0x004F) == 0x0040:
        return CiA402State.SWITCH_ON_DISABLED
    if (sw & 0x006F) == 0x0021:
        return CiA402State.READY_TO_SWITCH_ON
    if (sw & 0x006F) == 0x0023:
        return CiA402State.SWITCHED_ON
    if (sw & 0x006F) == 0x0027:
        return CiA402State.OPERATION_ENABLED
    if (sw & 0x006F) == 0x0007:
        return CiA402State.QUICK_STOP_ACTIVE
    if (sw & 0x004F) == 0x000F:
        return CiA402State.FAULT_REACTION_ACTIVE
    if (sw & 0x004F) == 0x0008:
        return CiA402State.FAULT
    return CiA402State.UNKNOWN


def next_controlword_for_state(state: CiA402State) -> int:
    controlword = Controlword()
    if state is CiA402State.FAULT:
        return controlword.fault_reset
    if state in {CiA402State.NOT_READY_TO_SWITCH_ON, CiA402State.SWITCH_ON_DISABLED, CiA402State.UNKNOWN}:
        return controlword.shutdown
    if state is CiA402State.READY_TO_SWITCH_ON:
        return controlword.switch_on
    if state is CiA402State.SWITCHED_ON:
        return controlword.enable_operation
    if state is CiA402State.OPERATION_ENABLED:
        return controlword.enable_operation
    if state is CiA402State.QUICK_STOP_ACTIVE:
        return controlword.shutdown
    return controlword.disable_voltage


def enable_operation(
    read_statusword: Callable[[], int],
    write_controlword: Callable[[int], None],
    *,
    timeout_s: float = 2.0,
    poll_interval_s: float = 0.02,
) -> bool:
    deadline = time.perf_counter() + timeout_s
    last_state = CiA402State.UNKNOWN
    while time.perf_counter() < deadline:
        statusword = read_statusword()
        state = parse_statusword(statusword)
        last_state = state
        if state is CiA402State.OPERATION_ENABLED:
            return True
        write_controlword(next_controlword_for_state(state))
        time.sleep(poll_interval_s)
    raise CiA402StateError(f"Timed out enabling operation; last state was {last_state.value}.")
