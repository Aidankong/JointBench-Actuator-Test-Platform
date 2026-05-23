from __future__ import annotations

import argparse
import sys
from pathlib import Path

import pyqtgraph as pg
from PySide6.QtCore import QObject, QThread, Qt, QUrl, Signal, Slot
from PySide6.QtGui import QColor, QDesktopServices, QPalette
from PySide6.QtWidgets import (
    QApplication,
    QDoubleSpinBox,
    QFormLayout,
    QFrame,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QSpinBox,
    QSplitter,
    QTableWidget,
    QTableWidgetItem,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from jointbench.comm import MockActuatorAdapter
from jointbench.models import ActuatorState, TestConfig, TestResult
from jointbench.test_cases import run_position_step_test


class StepTestWorker(QObject):
    sample = Signal(object)
    finished = Signal(object)
    error = Signal(str)
    log = Signal(str)

    def __init__(self, adapter: MockActuatorAdapter, config: TestConfig, reports_root: Path) -> None:
        super().__init__()
        self._adapter = adapter
        self._config = config
        self._reports_root = reports_root
        self._stop_requested = False

    def request_stop(self) -> None:
        self._stop_requested = True

    @Slot()
    def run(self) -> None:
        try:
            self.log.emit("Step response test started.")
            result = run_position_step_test(
                self._adapter,
                self._config,
                self._reports_root,
                sample_callback=self.sample.emit,
                stop_requested=lambda: self._stop_requested,
                sleep=True,
            )
            self.log.emit(f"Test finished with result {result.result}.")
            self.finished.emit(result)
        except Exception as exc:  # pragma: no cover - displayed in GUI.
            self.error.emit(str(exc))
            self.finished.emit(None)


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("JointBench Actuator Test Platform")
        self.resize(1360, 820)
        self.adapter = MockActuatorAdapter()
        self.reports_root = Path.cwd() / "reports"
        self.last_result: TestResult | None = None
        self._thread: QThread | None = None
        self._worker: StepTestWorker | None = None
        self._reset_buffers()
        self._build_ui()
        self._apply_style()
        self._set_running(False)
        self._log("Ready. Select Mock adapter and connect.")

    def _build_ui(self) -> None:
        central = QWidget()
        root = QVBoxLayout(central)
        root.setContentsMargins(12, 12, 12, 12)
        root.setSpacing(10)
        root.addLayout(self._build_header())

        splitter = QSplitter(Qt.Orientation.Horizontal)
        splitter.addWidget(self._build_left_panel())
        splitter.addWidget(self._build_plot_panel())
        splitter.addWidget(self._build_right_panel())
        splitter.setSizes([290, 760, 310])
        root.addWidget(splitter, 1)

        self.log_view = QTextEdit()
        self.log_view.setReadOnly(True)
        self.log_view.setMaximumHeight(150)
        root.addWidget(self.log_view)
        self.setCentralWidget(central)

    def _build_header(self) -> QHBoxLayout:
        header = QHBoxLayout()
        title = QLabel("JointBench Actuator Test Platform")
        title.setObjectName("TitleLabel")
        self.result_label = QLabel("IDLE")
        self.result_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.result_label.setObjectName("ResultLabel")
        header.addWidget(title)
        header.addStretch(1)
        header.addWidget(QLabel("Test Result"))
        header.addWidget(self.result_label)
        return header

    def _build_left_panel(self) -> QWidget:
        panel = QWidget()
        layout = QVBoxLayout(panel)
        layout.setSpacing(10)

        connection = QGroupBox("Device")
        connection_layout = QVBoxLayout(connection)
        self.adapter_label = QLabel("Adapter: Mock")
        self.device_label = QLabel("Device: not connected")
        self.firmware_label = QLabel("Firmware: -")
        self.connect_button = QPushButton("Connect")
        self.connect_button.clicked.connect(self._connect_device)
        connection_layout.addWidget(self.adapter_label)
        connection_layout.addWidget(self.device_label)
        connection_layout.addWidget(self.firmware_label)
        connection_layout.addWidget(self.connect_button)

        config_box = QGroupBox("Position Step Test")
        config_layout = QFormLayout(config_box)
        self.target_spin = _double_spin(-180.0, 180.0, 30.0, " deg")
        self.duration_spin = _double_spin(0.5, 20.0, 3.0, " s")
        self.sample_rate_spin = QSpinBox()
        self.sample_rate_spin.setRange(20, 1000)
        self.sample_rate_spin.setValue(100)
        self.sample_rate_spin.setSuffix(" Hz")
        self.max_overshoot_spin = _double_spin(0.0, 100.0, 10.0, " %")
        self.max_settling_spin = _double_spin(0.1, 10.0, 0.6, " s")
        self.max_error_spin = _double_spin(0.01, 10.0, 0.5, " deg")
        self.max_current_spin = _double_spin(0.1, 50.0, 5.0, " A")
        self.max_temp_spin = _double_spin(20.0, 150.0, 70.0, " C")
        config_layout.addRow("Target", self.target_spin)
        config_layout.addRow("Duration", self.duration_spin)
        config_layout.addRow("Sample Rate", self.sample_rate_spin)
        config_layout.addRow("Max Overshoot", self.max_overshoot_spin)
        config_layout.addRow("Max Settling", self.max_settling_spin)
        config_layout.addRow("Max Error", self.max_error_spin)
        config_layout.addRow("Max Current", self.max_current_spin)
        config_layout.addRow("Max Temp", self.max_temp_spin)

        actions = QGroupBox("Actions")
        actions_layout = QGridLayout(actions)
        self.start_button = QPushButton("Start Test")
        self.stop_button = QPushButton("Stop")
        self.open_report_button = QPushButton("Open Report Folder")
        self.start_button.clicked.connect(self._start_test)
        self.stop_button.clicked.connect(self._stop_test)
        self.open_report_button.clicked.connect(self._open_report_folder)
        actions_layout.addWidget(self.start_button, 0, 0)
        actions_layout.addWidget(self.stop_button, 0, 1)
        actions_layout.addWidget(self.open_report_button, 1, 0, 1, 2)

        layout.addWidget(connection)
        layout.addWidget(config_box)
        layout.addWidget(actions)
        layout.addStretch(1)
        return panel

    def _build_plot_panel(self) -> QWidget:
        panel = QWidget()
        layout = QVBoxLayout(panel)
        layout.setContentsMargins(0, 0, 0, 0)

        self.position_plot = pg.PlotWidget(title="Position Response")
        self.position_plot.setLabel("left", "Position", units="deg")
        self.position_plot.setLabel("bottom", "Time", units="s")
        self.position_plot.showGrid(x=True, y=True, alpha=0.3)
        self.target_curve = self.position_plot.plot(pen=pg.mkPen("#d97706", width=2), name="Target")
        self.actual_curve = self.position_plot.plot(pen=pg.mkPen("#2563eb", width=2), name="Actual")

        self.telemetry_plot = pg.PlotWidget(title="Telemetry")
        self.telemetry_plot.setLabel("left", "Value")
        self.telemetry_plot.setLabel("bottom", "Time", units="s")
        self.telemetry_plot.showGrid(x=True, y=True, alpha=0.3)
        self.speed_curve = self.telemetry_plot.plot(pen=pg.mkPen("#059669", width=2), name="Speed dps")
        self.current_curve = self.telemetry_plot.plot(pen=pg.mkPen("#dc2626", width=2), name="Current A")
        self.temp_curve = self.telemetry_plot.plot(pen=pg.mkPen("#7c3aed", width=2), name="Temperature C")

        layout.addWidget(self.position_plot, 2)
        layout.addWidget(self.telemetry_plot, 1)
        return panel

    def _build_right_panel(self) -> QWidget:
        panel = QWidget()
        layout = QVBoxLayout(panel)
        status = QGroupBox("Live Status")
        status_layout = QFormLayout(status)
        self.status_position = QLabel("-")
        self.status_speed = QLabel("-")
        self.status_current = QLabel("-")
        self.status_voltage = QLabel("-")
        self.status_temp = QLabel("-")
        status_layout.addRow("Position", self.status_position)
        status_layout.addRow("Speed", self.status_speed)
        status_layout.addRow("Current", self.status_current)
        status_layout.addRow("Voltage", self.status_voltage)
        status_layout.addRow("Temp", self.status_temp)

        metrics = QGroupBox("Metrics")
        metrics_layout = QVBoxLayout(metrics)
        self.metrics_table = QTableWidget(0, 2)
        self.metrics_table.setHorizontalHeaderLabels(["Metric", "Value"])
        self.metrics_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.metrics_table.verticalHeader().setVisible(False)
        metrics_layout.addWidget(self.metrics_table)

        layout.addWidget(status)
        layout.addWidget(metrics, 1)
        return panel

    def _connect_device(self) -> None:
        try:
            if not self.adapter.is_connected():
                self.adapter.connect()
            info = self.adapter.read_device_info()
            self.device_label.setText(f"Device: {info.device_id}")
            self.firmware_label.setText(f"Firmware: {info.firmware_version}")
            self._log(f"Connected to {info.device_id} via {info.adapter_type}.")
        except Exception as exc:
            QMessageBox.critical(self, "Connection Failed", str(exc))

    def _start_test(self) -> None:
        if self._thread and self._thread.isRunning():
            return
        if not self.adapter.is_connected():
            self._connect_device()
        self._reset_buffers()
        self._clear_plots()
        self.metrics_table.setRowCount(0)
        self.result_label.setText("RUNNING")
        self.result_label.setProperty("state", "RUNNING")
        self.result_label.style().unpolish(self.result_label)
        self.result_label.style().polish(self.result_label)

        config = self._read_config()
        self._set_running(True)
        self._thread = QThread(self)
        self._worker = StepTestWorker(self.adapter, config, self.reports_root)
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.sample.connect(self._on_sample)
        self._worker.log.connect(self._log)
        self._worker.error.connect(self._on_worker_error)
        self._worker.finished.connect(self._on_test_finished)
        self._worker.finished.connect(self._thread.quit)
        self._worker.finished.connect(self._worker.deleteLater)
        self._thread.finished.connect(self._thread.deleteLater)
        self._thread.finished.connect(self._clear_worker_refs)
        self._thread.start()

    def _stop_test(self) -> None:
        if self._worker:
            self._log("Stop requested.")
            self._worker.request_stop()

    def _open_report_folder(self) -> None:
        folder = self.last_result.output_dir if self.last_result else self.reports_root
        folder.mkdir(parents=True, exist_ok=True)
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(folder.resolve())))

    def _read_config(self) -> TestConfig:
        return TestConfig(
            target_position_deg=self.target_spin.value(),
            duration_s=self.duration_spin.value(),
            sample_rate_hz=float(self.sample_rate_spin.value()),
            max_overshoot_pct=self.max_overshoot_spin.value(),
            max_settling_time_s=self.max_settling_spin.value(),
            max_steady_state_error_deg=self.max_error_spin.value(),
            max_current_a=self.max_current_spin.value(),
            max_temperature_c=self.max_temp_spin.value(),
        )

    @Slot(object)
    def _on_sample(self, state: ActuatorState) -> None:
        self.times.append(state.timestamp_s)
        self.target_positions.append(state.target_position_deg)
        self.actual_positions.append(state.actual_position_deg)
        self.speeds.append(state.actual_speed_dps)
        self.currents.append(state.current_a)
        self.temperatures.append(state.temperature_c)
        self.target_curve.setData(self.times, self.target_positions)
        self.actual_curve.setData(self.times, self.actual_positions)
        self.speed_curve.setData(self.times, self.speeds)
        self.current_curve.setData(self.times, self.currents)
        self.temp_curve.setData(self.times, self.temperatures)
        self.status_position.setText(f"{state.actual_position_deg:.2f} deg")
        self.status_speed.setText(f"{state.actual_speed_dps:.1f} deg/s")
        self.status_current.setText(f"{state.current_a:.2f} A")
        self.status_voltage.setText(f"{state.voltage_v:.2f} V")
        self.status_temp.setText(f"{state.temperature_c:.1f} C")

    @Slot(object)
    def _on_test_finished(self, result: TestResult | None) -> None:
        self._set_running(False)
        if result is None:
            self.result_label.setText("ERROR")
            return
        self.last_result = result
        self.result_label.setText(result.result)
        self.result_label.setProperty("state", result.result)
        self.result_label.style().unpolish(self.result_label)
        self.result_label.style().polish(self.result_label)
        self._fill_metrics(result)
        self._log(f"CSV saved: {result.raw_data_path}")
        self._log(f"Report saved: {result.report_html_path}")

    @Slot(str)
    def _on_worker_error(self, message: str) -> None:
        self._set_running(False)
        self._log(f"ERROR: {message}")
        QMessageBox.critical(self, "Test Error", message)

    def _fill_metrics(self, result: TestResult) -> None:
        rows = result.metric_rows()
        self.metrics_table.setRowCount(len(rows))
        for row_index, (name, value) in enumerate(rows):
            self.metrics_table.setItem(row_index, 0, QTableWidgetItem(name))
            self.metrics_table.setItem(row_index, 1, QTableWidgetItem(value))

    def _set_running(self, running: bool) -> None:
        self.start_button.setEnabled(not running)
        self.stop_button.setEnabled(running)
        self.open_report_button.setEnabled(not running)
        for widget in (
            self.target_spin,
            self.duration_spin,
            self.sample_rate_spin,
            self.max_overshoot_spin,
            self.max_settling_spin,
            self.max_error_spin,
            self.max_current_spin,
            self.max_temp_spin,
        ):
            widget.setEnabled(not running)

    @Slot()
    def _clear_worker_refs(self) -> None:
        self._thread = None
        self._worker = None

    def _reset_buffers(self) -> None:
        self.times: list[float] = []
        self.target_positions: list[float] = []
        self.actual_positions: list[float] = []
        self.speeds: list[float] = []
        self.currents: list[float] = []
        self.temperatures: list[float] = []

    def _clear_plots(self) -> None:
        self.target_curve.setData([], [])
        self.actual_curve.setData([], [])
        self.speed_curve.setData([], [])
        self.current_curve.setData([], [])
        self.temp_curve.setData([], [])

    def _log(self, message: str) -> None:
        self.log_view.append(message)

    def _apply_style(self) -> None:
        pg.setConfigOptions(antialias=True)
        palette = self.palette()
        palette.setColor(QPalette.ColorRole.Window, QColor("#f4f6f8"))
        self.setPalette(palette)
        self.setStyleSheet(
            """
            QMainWindow, QWidget { font-family: "Segoe UI", Arial, sans-serif; font-size: 10pt; }
            QGroupBox { border: 1px solid #cfd6de; border-radius: 6px; margin-top: 8px; padding: 8px; }
            QGroupBox::title { subcontrol-origin: margin; left: 8px; padding: 0 4px; }
            QPushButton { padding: 7px 10px; border: 1px solid #9aa8b5; border-radius: 5px; background: #eef2f6; }
            QPushButton:hover { background: #e2e8f0; }
            QPushButton:disabled { color: #8c98a4; background: #edf0f3; }
            QLabel#TitleLabel { font-size: 18pt; font-weight: 700; color: #1f2933; }
            QLabel#ResultLabel { min-width: 92px; padding: 6px 10px; border-radius: 4px; background: #e5e7eb; font-weight: 700; }
            QLabel#ResultLabel[state="PASS"] { background: #d9f7e8; color: #106b3d; }
            QLabel#ResultLabel[state="FAIL"], QLabel#ResultLabel[state="INVALID"], QLabel#ResultLabel[state="ABORTED"] {
                background: #fde2e2; color: #9b1c1c;
            }
            QLabel#ResultLabel[state="RUNNING"] { background: #dbeafe; color: #1d4ed8; }
            QTextEdit, QTableWidget { border: 1px solid #cfd6de; border-radius: 4px; background: #ffffff; }
            """
        )


def _double_spin(minimum: float, maximum: float, value: float, suffix: str) -> QDoubleSpinBox:
    spin = QDoubleSpinBox()
    spin.setRange(minimum, maximum)
    spin.setDecimals(2)
    spin.setValue(value)
    spin.setSuffix(suffix)
    return spin


def run_app(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="JointBench Actuator Test Platform")
    parser.add_argument("--smoke-test", action="store_true", help="Create the GUI once and exit.")
    args = parser.parse_args(argv)

    app = QApplication.instance() or QApplication(sys.argv[:1])
    window = MainWindow()
    if args.smoke_test:
        app.processEvents()
        window.close()
        return 0
    window.show()
    return int(app.exec())
