from __future__ import annotations

import csv
from pathlib import Path

from jointbench.models import ActuatorState


FIELDNAMES = [
    "test_id",
    "sample_index",
    "timestamp_s",
    "target_position_deg",
    "actual_position_deg",
    "actual_speed_dps",
    "current_a",
    "voltage_v",
    "temperature_c",
    "fault_code",
    "enabled",
    "control_mode",
]


def save_states_csv(path: Path, test_id: str, samples: list[ActuatorState]) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8-sig") as file:
        writer = csv.DictWriter(file, fieldnames=FIELDNAMES)
        writer.writeheader()
        for index, state in enumerate(samples):
            row = state.to_row(test_id, index)
            writer.writerow({field: row[field] for field in FIELDNAMES})
    return path
