from __future__ import annotations

import hashlib
from pathlib import Path
from typing import Any

import yaml

from jointbench.config.schemas import (
    BusConfig,
    CiA402ObjectMap,
    ConfigArtifact,
    DeviceProfile,
    ProtocolConfigBundle,
    ProtocolType,
    SafetyLimits,
    ScalingConfig,
)
from jointbench.exceptions import ConfigurationError
from jointbench.models import TestConfig


def default_mock_bundle() -> ProtocolConfigBundle:
    return ProtocolConfigBundle(
        bus=BusConfig(protocol=ProtocolType.MOCK, interface="mock"),
        device=DeviceProfile(name="Mock Joint"),
        scaling=ScalingConfig(encoder_counts_per_rev=524288),
        safety=SafetyLimits(
            min_position_deg=-120.0,
            max_position_deg=120.0,
            max_speed_dps=360.0,
            max_current_a=5.0,
            max_temperature_c=70.0,
        ),
    )


def load_protocol_bundle(
    *,
    bus_path: str | Path | None = None,
    device_path: str | Path | None = None,
    safety_path: str | Path | None = None,
    test_path: str | Path | None = None,
    eds_path: str | Path | None = None,
    esi_path: str | Path | None = None,
    protocol_override: ProtocolType | str | None = None,
) -> ProtocolConfigBundle:
    artifacts: dict[str, ConfigArtifact] = {}
    bus_data = _load_yaml_artifact(bus_path, "bus", artifacts)
    device_data = _load_yaml_artifact(device_path, "device", artifacts)
    safety_data = _load_yaml_artifact(safety_path, "safety", artifacts)
    test_data = _load_yaml_artifact(test_path, "test", artifacts)
    _load_file_artifact(eds_path, "eds", artifacts)
    _load_file_artifact(esi_path, "esi", artifacts)

    bus = _parse_bus(bus_data, protocol_override)
    device = _parse_device(device_data)
    scaling = _parse_scaling(device_data)
    safety = _parse_safety(safety_data)
    test_config = _parse_test_config(test_data, safety)
    return ProtocolConfigBundle(
        bus=bus,
        device=device,
        scaling=scaling,
        safety=safety,
        test_config=test_config,
        artifacts=artifacts,
    )


def sha256_file(path: str | Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _load_yaml_artifact(
    path: str | Path | None,
    key: str,
    artifacts: dict[str, ConfigArtifact],
) -> dict[str, Any]:
    if not path:
        return {}
    artifact_path = Path(path)
    try:
        text = artifact_path.read_text(encoding="utf-8-sig")
        data = yaml.safe_load(text) or {}
    except FileNotFoundError as exc:
        raise ConfigurationError(f"{key} config file not found: {artifact_path}") from exc
    except yaml.YAMLError as exc:
        raise ConfigurationError(f"{key} config YAML parse failed: {exc}") from exc
    if not isinstance(data, dict):
        raise ConfigurationError(f"{key} config must be a YAML mapping.")
    artifacts[key] = ConfigArtifact(artifact_path, sha256_file(artifact_path))
    return data


def _load_file_artifact(
    path: str | Path | None,
    key: str,
    artifacts: dict[str, ConfigArtifact],
) -> None:
    if not path:
        return
    artifact_path = Path(path)
    if not artifact_path.exists():
        raise ConfigurationError(f"{key.upper()} file not found: {artifact_path}")
    artifacts[key] = ConfigArtifact(artifact_path, sha256_file(artifact_path))


def _parse_bus(data: dict[str, Any], protocol_override: ProtocolType | str | None) -> BusConfig:
    protocol_value = protocol_override or data.get("protocol", ProtocolType.MOCK.value)
    protocol = ProtocolType(str(protocol_value))
    can_data = data.get("can", {}) or {}
    ethercat_data = data.get("ethercat", {}) or {}
    if protocol is ProtocolType.CANOPEN_CIA402:
        return BusConfig(
            protocol=protocol,
            interface=_as_optional_str(can_data.get("interface")),
            channel=_as_optional_str(can_data.get("channel")),
            bitrate=_as_optional_int(can_data.get("bitrate")),
            node_id=_as_optional_int(can_data.get("node_id")),
            heartbeat_timeout_ms=int(can_data.get("heartbeat_timeout_ms", 500)),
            sdo_timeout_ms=int(can_data.get("sdo_timeout_ms", 300)),
        )
    if protocol is ProtocolType.ETHERCAT_COE_CIA402:
        return BusConfig(
            protocol=protocol,
            interface=_as_optional_str(ethercat_data.get("interface")),
            slave_index=_as_optional_int(ethercat_data.get("slave_index")),
            cycle_time_ms=_as_optional_float(ethercat_data.get("cycle_time_ms", 1.0)),
            distributed_clock=bool(ethercat_data.get("distributed_clock", False)),
        )
    return BusConfig(protocol=protocol, interface="mock")


def _parse_device(data: dict[str, Any]) -> DeviceProfile:
    device_data = data.get("device", {}) or {}
    cia402_data = data.get("cia402", {}) or {}
    control_data = data.get("control", {}) or {}
    object_map = CiA402ObjectMap(
        controlword=str(cia402_data.get("controlword", "0x6040:00")),
        statusword=str(cia402_data.get("statusword", "0x6041:00")),
        mode_of_operation=str(cia402_data.get("mode_of_operation", "0x6060:00")),
        mode_display=str(cia402_data.get("mode_display", "0x6061:00")),
        target_position=str(cia402_data.get("target_position", "0x607A:00")),
        actual_position=str(cia402_data.get("actual_position", "0x6064:00")),
        target_velocity=str(cia402_data.get("target_velocity", "0x60FF:00")),
        actual_velocity=str(cia402_data.get("actual_velocity", "0x606C:00")),
        target_torque=str(cia402_data.get("target_torque", "0x6071:00")),
        actual_torque=str(cia402_data.get("actual_torque", "0x6077:00")),
        error_code=str(cia402_data.get("error_code", "0x603F:00")),
    )
    return DeviceProfile(
        name=str(device_data.get("name", "CiA402 Joint")),
        vendor_id=_parse_int(device_data.get("vendor_id")),
        product_code=_parse_int(device_data.get("product_code")),
        revision_number=_parse_int(device_data.get("revision_number")),
        serial_number=_parse_int(device_data.get("serial_number")),
        object_map=object_map,
        preferred_mode=str(control_data.get("preferred_mode", "profile_position")),
        homing_required=bool(control_data.get("homing_required", False)),
        fault_reset_on_connect=bool(control_data.get("fault_reset_on_connect", False)),
    )


def _parse_scaling(data: dict[str, Any]) -> ScalingConfig:
    scaling_data = data.get("scaling", {}) or {}
    return ScalingConfig(
        encoder_counts_per_rev=_as_optional_int(scaling_data.get("encoder_counts_per_rev")),
        gear_ratio=float(scaling_data.get("gear_ratio", 1.0)),
        position_direction=int(scaling_data.get("position_direction", 1)),
        zero_offset_deg=float(scaling_data.get("zero_offset_deg", 0.0)),
        velocity_unit=str(scaling_data.get("velocity_unit", "counts_per_second")),
        current_scale_a_per_unit=_as_optional_float(scaling_data.get("current_scale_a_per_unit")),
        temperature_scale_c_per_unit=_as_optional_float(scaling_data.get("temperature_scale_c_per_unit")),
    )


def _parse_safety(data: dict[str, Any]) -> SafetyLimits | None:
    if not data:
        return None
    limits = data.get("limits", {}) or {}
    stop = data.get("safe_stop", {}) or {}
    return SafetyLimits(
        min_position_deg=_as_optional_float(limits.get("min_position_deg")),
        max_position_deg=_as_optional_float(limits.get("max_position_deg")),
        max_speed_dps=_as_optional_float(limits.get("max_speed_dps")),
        max_current_a=_as_optional_float(limits.get("max_current_a")),
        max_temperature_c=_as_optional_float(limits.get("max_temperature_c")),
        max_following_error_deg=_as_optional_float(limits.get("max_following_error_deg")),
        communication_timeout_ms=int(limits.get("communication_timeout_ms", 500)),
        safe_stop_strategy=str(stop.get("strategy", "quick_stop")),
        disable_after_stop=bool(stop.get("disable_after_stop", True)),
    )


def _parse_test_config(data: dict[str, Any], safety: SafetyLimits | None) -> TestConfig:
    test = data.get("test", {}) or {}
    pass_fail = data.get("pass_fail", {}) or {}
    return TestConfig(
        start_position_deg=float(test.get("start_position_deg", 0.0)),
        target_position_deg=float(test.get("target_position_deg", 30.0)),
        duration_s=float(test.get("duration_s", 3.0)),
        sample_rate_hz=float(test.get("sample_rate_hz", 100.0)),
        max_position_abs_deg=max(
            abs(float(safety.min_position_deg)),
            abs(float(safety.max_position_deg)),
        )
        if safety and safety.min_position_deg is not None and safety.max_position_deg is not None
        else 120.0,
        max_current_a=float(pass_fail.get("max_peak_current_a", safety.max_current_a if safety and safety.max_current_a else 5.0)),
        max_temperature_c=float(
            pass_fail.get("max_temperature_c", safety.max_temperature_c if safety and safety.max_temperature_c else 70.0)
        ),
        max_overshoot_pct=float(pass_fail.get("max_overshoot_pct", 10.0)),
        max_settling_time_s=float(pass_fail.get("max_settling_time_s", 0.6)),
        max_steady_state_error_deg=float(pass_fail.get("max_steady_state_error_deg", 0.5)),
    )


def _parse_int(value: Any) -> int | None:
    if value is None:
        return None
    if isinstance(value, int):
        return value
    text = str(value)
    return int(text, 16) if text.lower().startswith("0x") else int(text)


def _as_optional_int(value: Any) -> int | None:
    return None if value is None else _parse_int(value)


def _as_optional_float(value: Any) -> float | None:
    return None if value is None else float(value)


def _as_optional_str(value: Any) -> str | None:
    return None if value is None else str(value)
