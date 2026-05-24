from __future__ import annotations

from dataclasses import replace
from pathlib import Path
import sys
import types

import pytest

from jointbench.comm import create_adapter, scan_devices
from jointbench.comm.ads_symbols import AdsSymbolMap
from jointbench.comm.twincat_ads_backend import TwinCATAdsBackend
from jointbench.config import ProtocolType, load_protocol_bundle, validate_bundle
from jointbench.config.schemas import BusConfig, DeviceProfile, ProtocolConfigBundle
from jointbench.exceptions import ConfigurationError, SafetyLimitViolation
from jointbench.test_cases import run_position_step_test


ROOT = Path(__file__).resolve().parents[1]


def _ads_bundle(bus_name: str = "twincat_ads_fake.yaml") -> ProtocolConfigBundle:
    return load_protocol_bundle(
        bus_path=ROOT / "configs" / "buses" / bus_name,
        device_path=ROOT / "configs" / "devices" / "ti5_twincat_ads_template.yaml",
        safety_path=ROOT / "configs" / "safety" / "ti5_safe_limits_template.yaml",
        test_path=ROOT / "configs" / "tests" / "ti5_ads_position_step_5deg.yaml",
    )


def test_twincat_ads_yaml_loads_and_validates():
    bundle = _ads_bundle()
    report = validate_bundle(bundle)

    assert bundle.protocol is ProtocolType.TWINCAT_ADS
    assert bundle.bus.ams_net_id == "127.0.0.1.1.1"
    assert bundle.bus.ams_port == 851
    assert bundle.bus.host == "fake"
    assert report.ok
    assert report.motion_allowed


def test_ads_symbol_mapping_defaults_and_override():
    default_bundle = ProtocolConfigBundle(
        bus=BusConfig(protocol=ProtocolType.TWINCAT_ADS, ams_net_id="1.2.3.4.5.6", host="fake"),
        device=DeviceProfile(ads_symbol_prefix="MAIN.axis"),
    )
    override_bundle = ProtocolConfigBundle(
        bus=default_bundle.bus,
        device=DeviceProfile(
            ads_symbol_prefix="MAIN.axis",
            ads_symbols={"bEnable": "GVL_JointBench.bEnableAxis"},
        ),
    )

    assert AdsSymbolMap.from_bundle(default_bundle).name("bEnable") == "MAIN.axis.bEnable"
    assert AdsSymbolMap.from_bundle(override_bundle).name("bEnable") == "GVL_JointBench.bEnableAxis"
    assert AdsSymbolMap.from_bundle(override_bundle).name("fCurrentA") == "MAIN.axis.fCurrentA"


def test_fake_ads_scan_enable_start_stop_fault_path(tmp_path):
    bundle = _ads_bundle()
    scan = scan_devices(bundle)
    adapter = create_adapter(bundle)

    assert scan[0].protocol is ProtocolType.TWINCAT_ADS
    assert scan[0].match
    adapter.connect()
    adapter.set_enable(True)
    adapter.send_position_command(5.0)
    state = adapter.step(0.01, 0.01)
    assert state.enabled
    assert state.protocol == "twincat_ads"
    assert state.statusword == 0x0027
    adapter.emergency_stop()
    stopped = adapter.read_state(0.02)
    assert not stopped.enabled

    result = run_position_step_test(create_adapter(bundle), bundle.test_config, tmp_path, sleep=False)
    assert result.result == "PASS"
    assert result.device_info.protocol == "twincat_ads"
    assert result.device_info.ams_net_id == "127.0.0.1.1.1"
    assert result.device_info.ads_symbol_prefix == "MAIN.stJointBench"
    assert result.operation_enabled is True


def test_twincat_ads_missing_ams_net_id_blocks_motion():
    bundle = _ads_bundle()
    invalid = replace(bundle, bus=replace(bundle.bus, ams_net_id=None))
    report = validate_bundle(invalid)

    assert not report.ok
    assert any("ams_net_id" in issue.message for issue in report.errors)
    with pytest.raises(ConfigurationError):
        create_adapter(invalid)


def test_twincat_ads_rejects_large_first_motion():
    bundle = _ads_bundle()
    adapter = create_adapter(bundle)
    adapter.connect()
    adapter.set_enable(True)

    with pytest.raises(SafetyLimitViolation):
        adapter.send_position_command(6.0)


def test_twincat_backend_uses_pyads_connection(monkeypatch):
    bundle = replace(_ads_bundle(), bus=replace(_ads_bundle().bus, host="127.0.0.1"))
    calls: list[tuple[str, object]] = []
    symbols = {
        "MAIN.stJointBench.sDeviceName": "Ti5 PLC",
        "MAIN.stJointBench.nVendorId": 123,
        "MAIN.stJointBench.nProductCode": 456,
        "MAIN.stJointBench.nRevision": 789,
    }

    class FakeConnection:
        def __init__(self, ams_net_id, ams_port, host):
            calls.append(("init", (ams_net_id, ams_port, host)))

        def open(self):
            calls.append(("open", None))

        def close(self):
            calls.append(("close", None))

        def read_by_name(self, symbol, plc_type):
            calls.append(("read", (symbol, plc_type)))
            return symbols[symbol]

        def write_by_name(self, symbol, value, plc_type):
            calls.append(("write", (symbol, value, plc_type)))
            symbols[symbol] = value

    fake_pyads = types.SimpleNamespace(
        Connection=FakeConnection,
        PLCTYPE_BOOL="BOOL",
        PLCTYPE_DINT="DINT",
        PLCTYPE_STRING="STRING",
        PLCTYPE_LREAL="LREAL",
    )
    monkeypatch.setitem(sys.modules, "pyads", fake_pyads)

    backend = TwinCATAdsBackend(bundle)
    backend.connect()
    backend.write("MAIN.stJointBench.fTargetPositionDeg", 5.0)
    results = backend.scan()
    backend.disconnect()

    assert ("init", ("127.0.0.1.1.1", 851, "127.0.0.1")) in calls
    assert ("write", ("MAIN.stJointBench.fTargetPositionDeg", 5.0, "LREAL")) in calls
    assert results[0].message == "Ti5 PLC"
    assert ("close", None) in calls
