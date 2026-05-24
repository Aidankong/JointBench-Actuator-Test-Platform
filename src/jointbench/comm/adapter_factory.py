from __future__ import annotations

from jointbench.comm.base_adapter import BaseAdapter
from jointbench.comm.canopen_cia402_adapter import CanopenCiA402Adapter
from jointbench.comm.ethercat_cia402_adapter import EthercatCiA402Adapter
from jointbench.comm.mock_adapter import MockActuatorAdapter
from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult
from jointbench.config.validator import validate_bundle
from jointbench.exceptions import ConfigurationError


def create_adapter(bundle: ProtocolConfigBundle) -> BaseAdapter:
    report = validate_bundle(bundle)
    if not report.ok:
        raise ConfigurationError("; ".join(issue.message for issue in report.errors))
    if bundle.protocol is ProtocolType.MOCK:
        return MockActuatorAdapter()
    if bundle.protocol is ProtocolType.CANOPEN_CIA402:
        return CanopenCiA402Adapter(bundle)
    if bundle.protocol is ProtocolType.ETHERCAT_COE_CIA402:
        return EthercatCiA402Adapter(bundle)
    raise ConfigurationError(f"Unsupported protocol: {bundle.protocol.value}")


def scan_devices(bundle: ProtocolConfigBundle) -> list[ScanResult]:
    if bundle.protocol is ProtocolType.MOCK:
        return [
            ScanResult(
                protocol=ProtocolType.MOCK,
                state="Mock Ready",
                match=True,
                message="Mock actuator is always available.",
            )
        ]
    if bundle.protocol is ProtocolType.CANOPEN_CIA402:
        return CanopenCiA402Adapter.scan_devices(bundle)
    if bundle.protocol is ProtocolType.ETHERCAT_COE_CIA402:
        return EthercatCiA402Adapter.scan_devices(bundle)
    raise ConfigurationError(f"Scanning is not implemented for {bundle.protocol.value}.")
