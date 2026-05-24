from __future__ import annotations

PROFILE_POSITION_MODE = 1
CYCLIC_SYNCHRONOUS_POSITION_MODE = 8


def mode_value(name: str) -> int:
    normalized = name.strip().lower()
    if normalized in {"profile_position", "pp", "position"}:
        return PROFILE_POSITION_MODE
    if normalized in {"csp", "cyclic_synchronous_position"}:
        return CYCLIC_SYNCHRONOUS_POSITION_MODE
    raise ValueError(f"Unsupported CiA402 mode: {name}")
