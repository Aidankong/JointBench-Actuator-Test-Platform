from __future__ import annotations

from jointbench.comm.cia402_adapter_base import CiA402AdapterBase
from jointbench.comm.ethercat_pysoem_backend import PySoemEthercatBackend
from jointbench.comm.fake_cia402_backend import FakeCiA402Backend
from jointbench.config.schemas import ProtocolConfigBundle


class EthercatCiA402Adapter(CiA402AdapterBase):
    adapter_name = "EtherCAT CoE CiA402"
    transport_mode = "EtherCAT CoE SDO polling"

    def __init__(self, bundle: ProtocolConfigBundle, backend=None) -> None:
        super().__init__(bundle, backend or self._make_backend(bundle))

    @classmethod
    def _make_backend(cls, bundle: ProtocolConfigBundle):
        if bundle.bus.is_fake_transport:
            return FakeCiA402Backend(bundle)
        return PySoemEthercatBackend(bundle)
