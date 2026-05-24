from __future__ import annotations

from dataclasses import dataclass

from jointbench.config.schemas import ScalingConfig
from jointbench.exceptions import ConfigurationError


@dataclass(frozen=True)
class CiA402Scaling:
    config: ScalingConfig

    def __post_init__(self) -> None:
        if not self.config.has_position_scaling:
            raise ConfigurationError("Position scaling is incomplete.")

    @property
    def counts_per_joint_rev(self) -> float:
        assert self.config.encoder_counts_per_rev is not None
        return float(self.config.encoder_counts_per_rev) * float(self.config.gear_ratio)

    def deg_to_counts(self, deg: float) -> int:
        mechanical_deg = (deg - self.config.zero_offset_deg) * self.config.position_direction
        return int(round(mechanical_deg / 360.0 * self.counts_per_joint_rev))

    def counts_to_deg(self, counts: int | float) -> float:
        mechanical_deg = float(counts) / self.counts_per_joint_rev * 360.0
        return mechanical_deg * self.config.position_direction + self.config.zero_offset_deg

    def dps_to_counts_per_second(self, dps: float) -> int:
        return int(round(dps / 360.0 * self.counts_per_joint_rev * self.config.position_direction))

    def counts_per_second_to_dps(self, counts_per_second: int | float) -> float:
        return float(counts_per_second) / self.counts_per_joint_rev * 360.0 * self.config.position_direction

    def raw_current_to_a(self, raw: int | float) -> float:
        scale = self.config.current_scale_a_per_unit
        return float(raw) * scale if scale is not None else float(raw)

    def raw_temperature_to_c(self, raw: int | float) -> float:
        scale = self.config.temperature_scale_c_per_unit
        return float(raw) * scale if scale is not None else float(raw)
