from __future__ import annotations

CONTROLWORD = (0x6040, 0x00)
STATUSWORD = (0x6041, 0x00)
MODE_OF_OPERATION = (0x6060, 0x00)
MODE_DISPLAY = (0x6061, 0x00)
TARGET_POSITION = (0x607A, 0x00)
ACTUAL_POSITION = (0x6064, 0x00)
TARGET_VELOCITY = (0x60FF, 0x00)
ACTUAL_VELOCITY = (0x606C, 0x00)
TARGET_TORQUE = (0x6071, 0x00)
ACTUAL_TORQUE = (0x6077, 0x00)
ERROR_CODE = (0x603F, 0x00)


def parse_object_ref(value: str | tuple[int, int]) -> tuple[int, int]:
    if isinstance(value, tuple):
        return value
    text = value.strip()
    if ":" in text:
        index_text, subindex_text = text.split(":", 1)
    else:
        index_text, subindex_text = text, "0"
    return int(index_text, 16), int(subindex_text, 16)
