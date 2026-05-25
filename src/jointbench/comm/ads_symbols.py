from __future__ import annotations

from dataclasses import dataclass

from jointbench.config.schemas import ProtocolConfigBundle


SYMBOL_TYPES: dict[str, type] = {
    "bEnable": bool,
    "bStart": bool,
    "bStop": bool,
    "bResetFault": bool,
    "fTargetPositionDeg": float,
    "nCommandSequence": int,
    "bReady": bool,
    "bBusy": bool,
    "bDone": bool,
    "bError": bool,
    "bOperationEnabled": bool,
    "bWatchdogOk": bool,
    "fActualPositionDeg": float,
    "fActualVelocityDps": float,
    "fFollowingErrorDeg": float,
    "fCurrentA": float,
    "fTemperatureC": float,
    "nStatusword": int,
    "nControlword": int,
    "nFaultCode": int,
    "nErrorCode": int,
    "sDeviceName": str,
    "nVendorId": int,
    "nProductCode": int,
    "nRevision": int,
}


@dataclass(frozen=True)
class AdsSymbolMap:
    prefix: str
    symbols: dict[str, str]

    def name(self, key: str) -> str:
        return self.symbols.get(key, f"{self.prefix}.{key}")

    @classmethod
    def from_bundle(cls, bundle: ProtocolConfigBundle) -> "AdsSymbolMap":
        return cls(prefix=bundle.device.ads_symbol_prefix, symbols=dict(bundle.device.ads_symbols))
