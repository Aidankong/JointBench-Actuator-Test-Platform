from __future__ import annotations

from typing import Any

from jointbench.comm.ads_symbols import SYMBOL_TYPES
from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult
from jointbench.exceptions import CommunicationTimeout, ConfigurationError


class TwinCATAdsBackend:
    def __init__(self, bundle: ProtocolConfigBundle) -> None:
        self.bundle = bundle
        self.connection = None
        self.connected = False

    def connect(self) -> None:
        try:
            import pyads
        except ImportError as exc:
            raise ConfigurationError('TwinCAT ADS support requires: python -m pip install -e ".[ads]"') from exc
        if not self.bundle.bus.ams_net_id:
            raise ConfigurationError("TwinCAT ADS ams_net_id is required.")
        try:
            self.connection = pyads.Connection(
                self.bundle.bus.ams_net_id,
                self.bundle.bus.ams_port,
                self.bundle.bus.host,
            )
            self.connection.open()
            self.connected = True
        except Exception as exc:
            raise CommunicationTimeout(f"Failed to open TwinCAT ADS route: {exc}") from exc

    def disconnect(self) -> None:
        if self.connection is not None:
            close = getattr(self.connection, "close", None)
            if callable(close):
                close()
        self.connection = None
        self.connected = False

    def read(self, symbol: str) -> Any:
        self._ensure_connected()
        try:
            return self.connection.read_by_name(symbol, _pyads_type(symbol))
        except Exception as exc:
            raise CommunicationTimeout(f"ADS read failed for {symbol}: {exc}") from exc

    def write(self, symbol: str, value: object) -> None:
        self._ensure_connected()
        try:
            self.connection.write_by_name(symbol, value, _pyads_type(symbol))
        except Exception as exc:
            raise CommunicationTimeout(f"ADS write failed for {symbol}: {exc}") from exc

    def scan(self) -> list[ScanResult]:
        close_after_scan = not self.connected
        if close_after_scan:
            self.connect()
        try:
            return [
                ScanResult(
                    protocol=ProtocolType.TWINCAT_ADS,
                    vendor_id=int(self.read(self._name("nVendorId")) or 0),
                    product_code=int(self.read(self._name("nProductCode")) or 0),
                    revision_number=int(self.read(self._name("nRevision")) or 0),
                    state="ADS route ready",
                    match=True,
                    message=str(self.read(self._name("sDeviceName")) or "TwinCAT ADS PLC"),
                )
            ]
        finally:
            if close_after_scan:
                self.disconnect()

    def cycle(self, dt_s: float) -> None:
        del dt_s

    def _name(self, key: str) -> str:
        return self.bundle.device.ads_symbols.get(key, f"{self.bundle.device.ads_symbol_prefix}.{key}")

    def _ensure_connected(self) -> None:
        if not self.connected or self.connection is None:
            raise CommunicationTimeout("TwinCAT ADS backend is not connected.")


def _pyads_type(symbol: str):
    import pyads

    key = symbol.split(".")[-1]
    python_type = SYMBOL_TYPES.get(key, float)
    if python_type is bool:
        return pyads.PLCTYPE_BOOL
    if python_type is int:
        return pyads.PLCTYPE_DINT
    if python_type is str:
        return pyads.PLCTYPE_STRING
    return pyads.PLCTYPE_LREAL
