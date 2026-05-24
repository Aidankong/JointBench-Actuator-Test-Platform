from __future__ import annotations

from jointbench.comm.cia402_adapter_base import CiA402AdapterBase
from jointbench.comm.fake_cia402_backend import FakeCiA402Backend
from jointbench.config.schemas import ProtocolConfigBundle
from jointbench.exceptions import ConfigurationError


class EthercatCiA402Adapter(CiA402AdapterBase):
    adapter_name = "EtherCAT CoE CiA402"
    transport_mode = "EtherCAT CoE"

    def __init__(self, bundle: ProtocolConfigBundle, backend=None) -> None:
        super().__init__(bundle, backend or self._make_backend(bundle))

    @classmethod
    def _make_backend(cls, bundle: ProtocolConfigBundle):
        if bundle.bus.is_fake_transport:
            return FakeCiA402Backend(bundle)
        try:
            import pysoem  # noqa: F401
        except ImportError as exc:
            raise ConfigurationError(
                'EtherCAT support requires optional dependencies. Install with: python -m pip install -e ".[ethercat]"'
            ) from exc
        raise ConfigurationError(
            "Real EtherCAT backend scaffold is present, but hardware-specific master activation is not enabled in this build. "
            "Use interface 'fake' for offline validation or implement the pysoem backend for your NIC."
        )
