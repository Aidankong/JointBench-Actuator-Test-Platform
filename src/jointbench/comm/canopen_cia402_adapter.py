from __future__ import annotations

from jointbench.comm.cia402_adapter_base import CiA402AdapterBase
from jointbench.comm.fake_cia402_backend import FakeCiA402Backend
from jointbench.config.schemas import ProtocolConfigBundle
from jointbench.exceptions import ConfigurationError


class CanopenCiA402Adapter(CiA402AdapterBase):
    adapter_name = "CANopen CiA402"
    transport_mode = "CANopen SDO polling"

    def __init__(self, bundle: ProtocolConfigBundle, backend=None) -> None:
        super().__init__(bundle, backend or self._make_backend(bundle))

    @classmethod
    def _make_backend(cls, bundle: ProtocolConfigBundle):
        if bundle.bus.is_fake_transport:
            return FakeCiA402Backend(bundle)
        try:
            import canopen  # noqa: F401
            import can  # noqa: F401
        except ImportError as exc:
            raise ConfigurationError(
                'CANopen support requires optional dependencies. Install with: python -m pip install -e ".[can]"'
            ) from exc
        raise ConfigurationError(
            "Real CANopen backend scaffold is present, but hardware-specific bus activation is not enabled in this build. "
            "Use interface/channel 'fake' for offline validation or implement the canopen backend for your adapter."
        )
