from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QComboBox,
    QDialog,
    QFileDialog,
    QFormLayout,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMessageBox,
    QPushButton,
    QTableWidget,
    QTableWidgetItem,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from jointbench.comm import scan_devices
from jointbench.config import (
    ProtocolConfigBundle,
    ProtocolType,
    default_mock_bundle,
    load_protocol_bundle,
    validate_bundle,
)
from jointbench.exceptions import JointBenchError
from jointbench.twincat import DEFAULT_TWINCAT_ESI_DIR, install_esi_file, read_esi_summary


class ProtocolSetupDialog(QDialog):
    def __init__(self, current_bundle: ProtocolConfigBundle, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Communication Setup")
        self.resize(900, 620)
        self.bundle = current_bundle
        self.validation_report = validate_bundle(current_bundle)
        self._build_ui()
        self._set_protocol(current_bundle.protocol)
        self._render_validation()

    def _build_ui(self) -> None:
        root = QVBoxLayout(self)

        protocol_row = QHBoxLayout()
        protocol_row.addWidget(QLabel("Protocol"))
        self.protocol_combo = QComboBox()
        self.protocol_combo.addItem("Mock", ProtocolType.MOCK.value)
        self.protocol_combo.addItem("CANopen CiA402", ProtocolType.CANOPEN_CIA402.value)
        self.protocol_combo.addItem("EtherCAT CoE CiA402", ProtocolType.ETHERCAT_COE_CIA402.value)
        self.protocol_combo.addItem("TwinCAT ADS", ProtocolType.TWINCAT_ADS.value)
        protocol_row.addWidget(self.protocol_combo)
        protocol_row.addStretch(1)
        root.addLayout(protocol_row)

        files_box = QGroupBox("Configuration Files")
        files_layout = QFormLayout(files_box)
        self.path_fields: dict[str, QLineEdit] = {}
        for key, label in (
            ("bus", "Bus YAML"),
            ("device", "Device YAML"),
            ("safety", "Safety YAML"),
            ("test", "Test YAML"),
            ("eds", "EDS / DCF"),
            ("esi", "ESI XML"),
        ):
            files_layout.addRow(label, self._file_row(key))
        root.addWidget(files_box)

        actions = QHBoxLayout()
        self.load_button = QPushButton("Load")
        self.validate_button = QPushButton("Validate")
        self.scan_button = QPushButton("Scan")
        self.install_esi_button = QPushButton("Install ESI")
        self.apply_button = QPushButton("Apply")
        self.close_button = QPushButton("Close")
        self.load_button.clicked.connect(self._load_bundle)
        self.validate_button.clicked.connect(self._validate_bundle)
        self.scan_button.clicked.connect(self._scan)
        self.install_esi_button.clicked.connect(self._install_esi)
        self.apply_button.clicked.connect(self.accept)
        self.close_button.clicked.connect(self.reject)
        actions.addWidget(self.load_button)
        actions.addWidget(self.validate_button)
        actions.addWidget(self.scan_button)
        actions.addWidget(self.install_esi_button)
        actions.addStretch(1)
        actions.addWidget(self.apply_button)
        actions.addWidget(self.close_button)
        root.addLayout(actions)

        bottom = QGridLayout()
        self.scan_table = QTableWidget(0, 8)
        self.scan_table.setHorizontalHeaderLabels(
            ["Protocol", "Node", "Slave", "Vendor", "Product", "Revision", "State", "Match"]
        )
        self.scan_table.horizontalHeader().setStretchLastSection(True)
        bottom.addWidget(self.scan_table, 0, 0)

        self.validation_view = QTextEdit()
        self.validation_view.setReadOnly(True)
        bottom.addWidget(self.validation_view, 0, 1)
        bottom.setColumnStretch(0, 2)
        bottom.setColumnStretch(1, 1)
        root.addLayout(bottom, 1)

    def _file_row(self, key: str) -> QWidget:
        row = QWidget()
        layout = QHBoxLayout(row)
        layout.setContentsMargins(0, 0, 0, 0)
        field = QLineEdit()
        button = QPushButton("Browse")
        button.clicked.connect(lambda: self._browse_file(key))
        layout.addWidget(field, 1)
        layout.addWidget(button)
        self.path_fields[key] = field
        return row

    def _browse_file(self, key: str) -> None:
        filters = "Config Files (*.yaml *.yml *.eds *.dcf *.xml);;All Files (*)"
        path, _ = QFileDialog.getOpenFileName(self, f"Select {key} file", str(Path.cwd()), filters)
        if path:
            self.path_fields[key].setText(path)

    def _set_protocol(self, protocol: ProtocolType) -> None:
        index = self.protocol_combo.findData(protocol.value)
        if index >= 0:
            self.protocol_combo.setCurrentIndex(index)

    def _load_bundle(self) -> None:
        try:
            protocol = ProtocolType(self.protocol_combo.currentData())
            if protocol is ProtocolType.MOCK:
                self.bundle = default_mock_bundle()
            else:
                self.bundle = load_protocol_bundle(
                    bus_path=self._path("bus"),
                    device_path=self._path("device"),
                    safety_path=self._path("safety"),
                    test_path=self._path("test"),
                    eds_path=self._path("eds"),
                    esi_path=self._path("esi"),
                    protocol_override=protocol,
                )
            self.validation_report = validate_bundle(self.bundle)
            self._render_validation()
        except (JointBenchError, ValueError) as exc:
            QMessageBox.critical(self, "Load Failed", str(exc))

    def _validate_bundle(self) -> None:
        self.validation_report = validate_bundle(self.bundle)
        self._render_validation()

    def _scan(self) -> None:
        try:
            self._load_bundle()
            results = scan_devices(self.bundle)
            self.scan_table.setRowCount(len(results))
            for row, result in enumerate(results):
                values = [
                    result.protocol.value,
                    "" if result.node_id is None else str(result.node_id),
                    "" if result.slave_index is None else str(result.slave_index),
                    "" if result.vendor_id is None else f"0x{result.vendor_id:08X}",
                    "" if result.product_code is None else f"0x{result.product_code:08X}",
                    "" if result.revision_number is None else f"0x{result.revision_number:08X}",
                    result.state,
                    "YES" if result.match else "NO",
                ]
                for col, value in enumerate(values):
                    item = QTableWidgetItem(value)
                    item.setFlags(item.flags() ^ Qt.ItemFlag.ItemIsEditable)
                    self.scan_table.setItem(row, col, item)
        except (JointBenchError, ValueError) as exc:
            QMessageBox.critical(self, "Scan Failed", str(exc))

    def _install_esi(self) -> None:
        try:
            esi_path = self._path("esi")
            if not esi_path:
                path, _ = QFileDialog.getOpenFileName(
                    self,
                    "Select EtherCAT ESI XML",
                    str(Path.cwd()),
                    "EtherCAT ESI XML (*.xml);;All Files (*)",
                )
                if not path:
                    return
                esi_path = path
                self.path_fields["esi"].setText(path)

            summary = read_esi_summary(esi_path)
            installed_path = install_esi_file(esi_path)
            QMessageBox.information(
                self,
                "ESI Installed",
                (
                    f"Installed {summary.label()}.\n\n"
                    f"Source: {esi_path}\n"
                    f"Target: {installed_path}\n\n"
                    "Restart TwinCAT XAE or reload EtherCAT device descriptions before scanning."
                ),
            )
        except (JointBenchError, OSError, ValueError) as exc:
            QMessageBox.critical(
                self,
                "Install ESI Failed",
                f"{exc}\n\nDefault TwinCAT ESI directory: {DEFAULT_TWINCAT_ESI_DIR}",
            )

    def _render_validation(self) -> None:
        lines = self.validation_report.summary_lines()
        lines.append("")
        lines.append(f"Motion allowed: {'YES' if self.validation_report.motion_allowed else 'NO'}")
        self.validation_view.setPlainText("\n".join(lines))
        self.apply_button.setEnabled(self.validation_report.ok)

    def _path(self, key: str) -> str | None:
        value = self.path_fields[key].text().strip()
        return value or None
