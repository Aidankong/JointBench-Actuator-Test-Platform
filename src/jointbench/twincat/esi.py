from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

from jointbench.exceptions import ConfigurationError


DEFAULT_TWINCAT_ESI_DIR = Path(os.environ.get("JOINTBENCH_TWINCAT_ESI_DIR", r"C:\TwinCAT\3.1\Config\Io\EtherCAT"))


@dataclass(frozen=True)
class EsiSummary:
    vendor_name: str
    vendor_id: str
    device_type: str
    product_code: str
    revision_number: str

    def label(self) -> str:
        return (
            f"{self.vendor_name} {self.device_type} "
            f"(vendor {self.vendor_id}, product {self.product_code}, revision {self.revision_number})"
        )


def install_esi_file(source_path: str | Path, target_dir: str | Path = DEFAULT_TWINCAT_ESI_DIR) -> Path:
    source = Path(source_path)
    target_root = Path(target_dir)
    summary = read_esi_summary(source)

    if not target_root.exists():
        raise ConfigurationError(f"TwinCAT ESI directory does not exist: {target_root}")
    if not target_root.is_dir():
        raise ConfigurationError(f"TwinCAT ESI target is not a directory: {target_root}")

    target = target_root / source.name
    try:
        if source.resolve() != target.resolve():
            shutil.copy2(source, target)
    except PermissionError as exc:
        raise ConfigurationError(
            f"Permission denied while installing ESI to {target_root}. Run JointBench as administrator."
        ) from exc
    except OSError as exc:
        raise ConfigurationError(f"Failed to install ESI {summary.label()} to {target_root}: {exc}") from exc
    return target


def read_esi_summary(source_path: str | Path) -> EsiSummary:
    source = Path(source_path)
    if not source.exists():
        raise ConfigurationError(f"ESI XML file not found: {source}")
    if source.suffix.lower() != ".xml":
        raise ConfigurationError(f"ESI file must be an XML file: {source}")

    try:
        tree = ET.parse(source)
    except ET.ParseError as exc:
        raise ConfigurationError(f"ESI XML parse failed: {exc}") from exc

    root = tree.getroot()
    if _local_name(root.tag) != "EtherCATInfo":
        raise ConfigurationError("Selected XML is not an EtherCAT ESI file; root element must be EtherCATInfo.")

    vendor = root.find("Vendor")
    descriptions = root.find("Descriptions")
    devices = descriptions.find("Devices") if descriptions is not None else None
    device = devices.find("Device") if devices is not None else None
    device_type = device.find("Type") if device is not None else None
    if vendor is None or device is None or device_type is None:
        raise ConfigurationError("ESI XML is missing Vendor or Device/Type metadata.")

    return EsiSummary(
        vendor_name=_text(vendor.find("Name"), "UnknownVendor"),
        vendor_id=_text(vendor.find("Id"), "unknown"),
        device_type=(device_type.text or "UnknownDevice").strip(),
        product_code=device_type.attrib.get("ProductCode", "unknown"),
        revision_number=device_type.attrib.get("RevisionNo", "unknown"),
    )


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def _text(element: ET.Element | None, default: str) -> str:
    if element is None or element.text is None:
        return default
    return element.text.strip() or default
