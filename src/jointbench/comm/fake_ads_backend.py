from __future__ import annotations

from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ScanResult


class FakeAdsBackend:
    """In-memory ADS symbol backend for development without TwinCAT."""

    def __init__(self, bundle: ProtocolConfigBundle) -> None:
        self.bundle = bundle
        self.connected = False
        self.symbols: dict[str, object] = {}
        self._target_position = bundle.test_config.start_position_deg
        self._position = bundle.test_config.start_position_deg
        self._velocity = 0.0
        self._temperature = 31.0
        self._current = 0.1
        self._init_symbols()

    def connect(self) -> None:
        self.connected = True

    def disconnect(self) -> None:
        self.connected = False

    def read(self, symbol: str) -> object:
        return self.symbols.get(symbol, _default_for_symbol(symbol))

    def write(self, symbol: str, value: object) -> None:
        self.symbols[symbol] = value
        if symbol.endswith(".bEnable"):
            self.symbols[self._name("bOperationEnabled")] = bool(value)
            self.symbols[self._name("bReady")] = bool(value)
        elif symbol.endswith(".bStop") and value:
            self.symbols[self._name("bBusy")] = False
            self.symbols[self._name("bDone")] = False
            self.symbols[self._name("bOperationEnabled")] = False
            self.symbols[self._name("nControlword")] = 0x0002
        elif symbol.endswith(".fTargetPositionDeg"):
            self._target_position = float(value)
        elif symbol.endswith(".bStart") and value:
            self.symbols[self._name("bBusy")] = True
            self.symbols[self._name("bDone")] = False
            self.symbols[self._name("nControlword")] = 0x000F

    def scan(self) -> list[ScanResult]:
        return [
            ScanResult(
                protocol=ProtocolType.TWINCAT_ADS,
                vendor_id=int(self.read(self._name("nVendorId")) or 0),
                product_code=int(self.read(self._name("nProductCode")) or 0),
                revision_number=int(self.read(self._name("nRevision")) or 0),
                state="ADS route ready" if self.connected else "ADS fake available",
                match=True,
                message="Fake TwinCAT ADS PLC interface detected.",
            )
        ]

    def cycle(self, dt_s: float) -> None:
        if not bool(self.read(self._name("bOperationEnabled"))):
            self._velocity = 0.0
        else:
            error = self._target_position - self._position
            velocity_cmd = max(-120.0, min(120.0, error * 12.0))
            self._velocity += (velocity_cmd - self._velocity) * min(1.0, dt_s * 16.0)
            self._position += self._velocity * dt_s
            self._current = min(2.5, 0.15 + abs(self._velocity) * 0.01 + abs(error) * 0.02)
            self._temperature += self._current * self._current * dt_s * 0.02
            if abs(error) < 0.05 and abs(self._velocity) < 1.0:
                self.symbols[self._name("bBusy")] = False
                self.symbols[self._name("bDone")] = True

        self.symbols[self._name("fActualPositionDeg")] = self._position
        self.symbols[self._name("fActualVelocityDps")] = self._velocity
        self.symbols[self._name("fCurrentA")] = self._current
        self.symbols[self._name("fTemperatureC")] = self._temperature
        self.symbols[self._name("nStatusword")] = 0x0027 if self.read(self._name("bOperationEnabled")) else 0x0040

    def _init_symbols(self) -> None:
        for key, value in {
            "bEnable": False,
            "bStart": False,
            "bStop": False,
            "bResetFault": False,
            "fTargetPositionDeg": self._target_position,
            "bReady": False,
            "bBusy": False,
            "bDone": False,
            "bError": False,
            "bOperationEnabled": False,
            "fActualPositionDeg": self._position,
            "fActualVelocityDps": 0.0,
            "fCurrentA": self._current,
            "fTemperatureC": self._temperature,
            "nStatusword": 0x0040,
            "nControlword": 0x0000,
            "nFaultCode": 0,
            "nErrorCode": 0,
            "sDeviceName": self.bundle.device.name,
            "nVendorId": self.bundle.device.vendor_id or 0,
            "nProductCode": self.bundle.device.product_code or 0,
            "nRevision": self.bundle.device.revision_number or 0,
        }.items():
            self.symbols[self._name(key)] = value

    def _name(self, key: str) -> str:
        return self.bundle.device.ads_symbols.get(key, f"{self.bundle.device.ads_symbol_prefix}.{key}")


def _default_for_symbol(symbol: str) -> object:
    if symbol.endswith(("Name", "sDeviceName")):
        return ""
    if symbol.split(".")[-1].startswith("b"):
        return False
    if symbol.split(".")[-1].startswith("f"):
        return 0.0
    return 0
