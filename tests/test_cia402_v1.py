from __future__ import annotations

from pathlib import Path

from jointbench.cia402 import CiA402Scaling, CiA402State, Controlword, parse_statusword
from jointbench.comm import create_adapter, scan_devices
from jointbench.config import ProtocolType, load_protocol_bundle, validate_bundle
from jointbench.config.schemas import ScalingConfig
from jointbench.exceptions import ConfigurationError
from jointbench.test_cases import run_position_step_test


ROOT = Path(__file__).resolve().parents[1]


def _bundle(protocol: ProtocolType = ProtocolType.CANOPEN_CIA402):
    bus = "canopen_fake.yaml" if protocol is ProtocolType.CANOPEN_CIA402 else "ethercat_fake.yaml"
    return load_protocol_bundle(
        bus_path=ROOT / "configs" / "buses" / bus,
        device_path=ROOT / "configs" / "devices" / "sample_cia402_joint.yaml",
        safety_path=ROOT / "configs" / "safety" / "default_joint_limits.yaml",
        test_path=ROOT / "configs" / "tests" / "position_step_5deg.yaml",
    )


def test_load_and_validate_canopen_fake_bundle():
    bundle = _bundle()
    report = validate_bundle(bundle)

    assert bundle.protocol is ProtocolType.CANOPEN_CIA402
    assert report.ok
    assert report.motion_allowed
    assert "bus" in bundle.config_hashes
    assert "device" in bundle.config_files


def test_validation_blocks_real_motion_without_safety():
    bundle = load_protocol_bundle(
        bus_path=ROOT / "configs" / "buses" / "canopen_fake.yaml",
        device_path=ROOT / "configs" / "devices" / "sample_cia402_joint.yaml",
    )

    report = validate_bundle(bundle)

    assert not report.ok
    assert not report.motion_allowed
    assert any("Safety config" in issue.message for issue in report.errors)


def test_cia402_state_machine_and_scaling():
    assert parse_statusword(0x0040) is CiA402State.SWITCH_ON_DISABLED
    assert parse_statusword(0x0021) is CiA402State.READY_TO_SWITCH_ON
    assert parse_statusword(0x0023) is CiA402State.SWITCHED_ON
    assert parse_statusword(0x0027) is CiA402State.OPERATION_ENABLED
    assert parse_statusword(0x0008) is CiA402State.FAULT
    assert Controlword().enable_operation == 0x000F

    scaling = CiA402Scaling(ScalingConfig(encoder_counts_per_rev=3600, gear_ratio=2.0))
    counts = scaling.deg_to_counts(90.0)
    assert counts == 1800
    assert scaling.counts_to_deg(counts) == 90.0


def test_scan_fake_canopen_and_ethercat():
    can_results = scan_devices(_bundle(ProtocolType.CANOPEN_CIA402))
    ethercat_results = scan_devices(_bundle(ProtocolType.ETHERCAT_COE_CIA402))

    assert can_results[0].node_id == 1
    assert can_results[0].match
    assert ethercat_results[0].slave_index == 0
    assert ethercat_results[0].match


def test_fake_canopen_adapter_runs_position_step(tmp_path):
    bundle = _bundle()
    adapter = create_adapter(bundle)
    result = run_position_step_test(adapter, bundle.test_config, tmp_path, sleep=False)

    assert result.result == "PASS"
    assert result.device_info.protocol == "canopen_cia402"
    assert result.device_info.transport_mode == "CANopen SDO polling"
    assert result.operation_enabled is True
    assert result.config_hashes
    assert result.final_statusword is not None


def test_factory_rejects_invalid_bundle():
    bundle = load_protocol_bundle(
        bus_path=ROOT / "configs" / "buses" / "canopen_fake.yaml",
        device_path=ROOT / "configs" / "devices" / "sample_cia402_joint.yaml",
    )

    try:
        create_adapter(bundle)
    except ConfigurationError:
        pass
    else:
        raise AssertionError("Invalid bundle should not create a real adapter.")
