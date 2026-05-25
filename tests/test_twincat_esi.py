from __future__ import annotations

from pathlib import Path

import pytest

from jointbench.exceptions import ConfigurationError
from jointbench.twincat import install_esi_file, read_esi_summary


ROOT = Path(__file__).resolve().parents[1]
USER_ESI = Path(r"C:\Users\Administrator\Documents\Ti5Robot_JointMotor_2.0.xml")


def _esi_path() -> Path:
    if USER_ESI.exists():
        return USER_ESI
    pytest.skip(f"Ti5 ESI sample not available: {USER_ESI}")


def test_read_ti5_esi_summary():
    summary = read_esi_summary(_esi_path())

    assert summary.vendor_name == "Ti5Robot"
    assert summary.vendor_id == "#x00522227"
    assert summary.device_type == "Ti5Robot_JointMotor"
    assert summary.product_code == "#x00009253"
    assert summary.revision_number == "#x00010005"


def test_install_esi_file_copies_to_target_dir(tmp_path):
    installed = install_esi_file(_esi_path(), tmp_path)

    assert installed == tmp_path / _esi_path().name
    assert installed.exists()
    assert read_esi_summary(installed).product_code == "#x00009253"


def test_rejects_non_esi_xml(tmp_path):
    xml_path = tmp_path / "not_esi.xml"
    xml_path.write_text("<root />", encoding="utf-8")

    with pytest.raises(ConfigurationError, match="EtherCAT ESI"):
        read_esi_summary(xml_path)
