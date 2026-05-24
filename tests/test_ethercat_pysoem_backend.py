from __future__ import annotations

from pathlib import Path

from jointbench.comm.ethercat_pysoem_backend import PySoemEthercatBackend
from jointbench.config import ProtocolType, load_protocol_bundle, validate_bundle


ROOT = Path(__file__).resolve().parents[1]


class FakeSlave:
    man = 0x00000001
    id = 0x00004001
    rev = 0x00010000
    serial = 1234

    def __init__(self) -> None:
        self.objects: dict[tuple[int, int], bytes] = {
            (0x6041, 0x00): (0x0040).to_bytes(2, "little"),
            (0x6061, 0x00): (1).to_bytes(1, "little", signed=True),
            (0x6064, 0x00): (0).to_bytes(4, "little", signed=True),
            (0x606C, 0x00): (0).to_bytes(4, "little", signed=True),
            (0x603F, 0x00): (0).to_bytes(2, "little"),
        }
        self.writes: list[tuple[int, int, bytes]] = []

    def sdo_read(self, index: int, subindex: int) -> bytes:
        return self.objects[(index, subindex)]

    def sdo_write(self, index: int, subindex: int, data: bytes) -> None:
        self.writes.append((index, subindex, bytes(data)))
        self.objects[(index, subindex)] = bytes(data)
        if (index, subindex) == (0x6040, 0x00):
            controlword = int.from_bytes(data, "little")
            if controlword == 0x0006:
                self.objects[(0x6041, 0x00)] = (0x0021).to_bytes(2, "little")
            elif controlword == 0x0007:
                self.objects[(0x6041, 0x00)] = (0x0023).to_bytes(2, "little")
            elif controlword == 0x000F:
                self.objects[(0x6041, 0x00)] = (0x0027).to_bytes(2, "little")
            elif controlword == 0x0002:
                self.objects[(0x6041, 0x00)] = (0x0007).to_bytes(2, "little")
        if (index, subindex) == (0x607A, 0x00):
            self.objects[(0x6064, 0x00)] = bytes(data)


class FakeMaster:
    def __init__(self) -> None:
        self.slaves = [FakeSlave()]
        self.opened_interface = None
        self.closed = False

    def open(self, interface: str) -> None:
        self.opened_interface = interface

    def config_init(self) -> int:
        return len(self.slaves)

    def close(self) -> None:
        self.closed = True


def _ti5_bundle():
    return load_protocol_bundle(
        bus_path=ROOT / "configs" / "buses" / "ethercat_ti5_template.yaml",
        device_path=ROOT / "configs" / "devices" / "ti5_cia402_template.yaml",
        safety_path=ROOT / "configs" / "safety" / "ti5_safe_limits_template.yaml",
        test_path=ROOT / "configs" / "tests" / "ti5_position_step_5deg.yaml",
    )


def test_ti5_template_loads_with_expected_warnings():
    bundle = _ti5_bundle()
    report = validate_bundle(bundle)

    assert bundle.protocol is ProtocolType.ETHERCAT_COE_CIA402
    assert not report.ok
    assert any("Position scaling" in issue.message for issue in report.errors)
    assert any("ESI XML" in issue.message for issue in report.warnings)


def test_pysoem_backend_scan_and_sdo_calls():
    bundle = load_protocol_bundle(
        bus_path=ROOT / "configs" / "buses" / "ethercat_fake.yaml",
        device_path=ROOT / "configs" / "devices" / "sample_cia402_joint.yaml",
        safety_path=ROOT / "configs" / "safety" / "default_joint_limits.yaml",
        test_path=ROOT / "configs" / "tests" / "position_step_5deg.yaml",
        protocol_override=ProtocolType.ETHERCAT_COE_CIA402,
    )
    master = FakeMaster()
    backend = PySoemEthercatBackend(bundle, master_factory=lambda: master)

    results = backend.scan()
    backend.connect()
    backend.write_controlword(0x0006)
    backend.write_mode(1)
    backend.write_target_position(1024)
    backend.cycle(0.01)
    backend.quick_stop()

    assert results[0].match
    assert master.opened_interface == "fake"
    assert backend.read_statusword() == 0x0007
    assert backend.read_actual_position() == 1024
    assert (0x6060, 0x00, (1).to_bytes(1, "little", signed=True)) in master.slaves[0].writes
    assert master.slaves[0].writes[-1] == (0x6040, 0x00, (0x0002).to_bytes(2, "little"))
