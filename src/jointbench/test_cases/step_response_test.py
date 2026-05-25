from __future__ import annotations

from dataclasses import asdict, is_dataclass
from datetime import datetime
from enum import Enum
from pathlib import Path
import time
from typing import Callable

import yaml

from jointbench.analysis import analyze_step_response, judge_step_response
from jointbench.comm.base_adapter import BaseAdapter
from jointbench.models import ActuatorState, TestConfig, TestResult
from jointbench.reports import build_reports
from jointbench.storage import save_states_csv


SampleCallback = Callable[[ActuatorState], None]
StopCallback = Callable[[], bool]


def run_position_step_test(
    adapter: BaseAdapter,
    config: TestConfig,
    output_root: Path,
    *,
    sample_callback: SampleCallback | None = None,
    stop_requested: StopCallback | None = None,
    sleep: bool = True,
) -> TestResult:
    test_id = datetime.now().strftime("JB%Y%m%d-%H%M%S")
    output_dir = output_root / test_id
    raw_data_path = output_dir / "raw_data.csv"
    report_md_path = output_dir / "report.md"
    report_html_path = output_dir / "report.html"
    events_log_path = output_dir / "events.log"
    config_snapshot_path = output_dir / "config_snapshot.yaml"

    events: list[str] = []
    event_start = time.perf_counter()

    def record_event(message: str) -> None:
        events.append(f"{time.perf_counter() - event_start:8.3f}s  {message}")

    samples: list[ActuatorState] = []
    failure_reasons: list[str] = []
    aborted = False
    dt_s = config.sample_period_s
    sample_count = int(config.duration_s * config.sample_rate_hz) + 1

    try:
        record_event(f"Test {test_id} initialized.")
        if not adapter.is_connected():
            record_event("Connecting adapter.")
            adapter.connect()
            record_event("Adapter connected.")
        if hasattr(adapter, "reset"):
            adapter.reset(config.start_position_deg)
            record_event(f"Adapter reset to {config.start_position_deg:.3f} deg.")

        device_info = adapter.read_device_info()
        record_event(f"Device info read: {device_info.device_id} via {device_info.adapter_type}.")
        adapter.set_enable(True)
        record_event("Operation enable requested.")
        adapter.set_control_mode("position")
        record_event("Position control mode selected.")
        adapter.send_position_command(config.target_position_deg)
        record_event(f"Position command sent: {config.target_position_deg:.3f} deg.")

        start_time = time.perf_counter()
        for index in range(sample_count):
            if stop_requested and stop_requested():
                aborted = True
                failure_reasons.append("User stopped the test.")
                record_event("Stop requested by user.")
                adapter.emergency_stop()
                record_event("Emergency stop requested after user stop.")
                break

            elapsed_s = index * dt_s
            state = adapter.step(dt_s, elapsed_s)
            samples.append(state)
            if sample_callback:
                sample_callback(state)

            safety_reason = _safety_failure(state, config)
            if safety_reason:
                aborted = True
                failure_reasons.append(safety_reason)
                record_event(f"Safety abort: {safety_reason}")
                adapter.emergency_stop()
                record_event("Emergency stop requested after safety abort.")
                break

            if sleep:
                target_time = start_time + (index + 1) * dt_s
                remaining = target_time - time.perf_counter()
                if remaining > 0:
                    time.sleep(remaining)

        metrics = analyze_step_response(samples, config)
        result_name, failure_reasons = judge_step_response(
            metrics,
            config,
            aborted=aborted,
            failure_reasons=failure_reasons,
        )
        record_event(f"Analysis finished with result {result_name}.")
        save_states_csv(raw_data_path, test_id, samples)
        record_event(f"Raw samples saved: {raw_data_path.name}.")
        _write_config_snapshot(config_snapshot_path, adapter, config)
        record_event(f"Configuration snapshot saved: {config_snapshot_path.name}.")
        _write_events_log(events_log_path, events)

        final_state = samples[-1] if samples else None
        result = TestResult(
            test_id=test_id,
            result=result_name,
            device_info=device_info,
            config=config,
            metrics=metrics,
            raw_data_path=raw_data_path,
            report_md_path=report_md_path,
            report_html_path=report_html_path,
            events_log_path=events_log_path,
            config_snapshot_path=config_snapshot_path,
            output_dir=output_dir,
            failure_reasons=failure_reasons,
            aborted=aborted,
            config_files=getattr(adapter, "config_files", {}),
            config_hashes=getattr(adapter, "config_hashes", {}),
            operation_enabled=getattr(adapter, "operation_enabled", None),
            final_statusword=final_state.statusword if final_state else None,
            final_controlword=final_state.controlword if final_state else None,
            final_error_code=final_state.fault_code if final_state else None,
            final_command_sequence=final_state.command_sequence if final_state else None,
            final_watchdog_ok=final_state.watchdog_ok if final_state else None,
            final_following_error_deg=final_state.following_error_deg if final_state else None,
        )
        build_reports(result)
        return result
    except Exception as exc:
        record_event(f"ERROR: {exc}")
        if adapter.is_connected():
            try:
                adapter.emergency_stop()
                record_event("Emergency stop requested after exception.")
            except Exception as stop_exc:
                record_event(f"Emergency stop failed after exception: {stop_exc}")
        _write_config_snapshot(config_snapshot_path, adapter, config)
        _write_events_log(events_log_path, events)
        raise


def _safety_failure(state: ActuatorState, config: TestConfig) -> str | None:
    if state.watchdog_ok is False:
        return "ADS watchdog reported unhealthy command updates."
    if state.fault_code:
        return f"Device fault code {state.fault_code}."
    if abs(state.actual_position_deg) > config.max_position_abs_deg:
        return f"Position {state.actual_position_deg:.2f}deg exceeded +/-{config.max_position_abs_deg:.2f}deg."
    if state.current_a > config.max_current_a:
        return f"Current {state.current_a:.2f}A exceeded {config.max_current_a:.2f}A."
    if state.temperature_c > config.max_temperature_c:
        return f"Temperature {state.temperature_c:.1f}C exceeded {config.max_temperature_c:.1f}C."
    return None


def _write_events_log(path: Path, events: list[str]) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(events) + ("\n" if events else ""), encoding="utf-8")
    return path


def _write_config_snapshot(path: Path, adapter: BaseAdapter, config: TestConfig) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    bundle = getattr(adapter, "bundle", None)
    snapshot: dict[str, object] = {
        "runtime_test_config": _plain(config),
        "config_files": dict(getattr(adapter, "config_files", {})),
        "config_hashes": dict(getattr(adapter, "config_hashes", {})),
    }
    if bundle is not None:
        snapshot.update(
            {
                "protocol": bundle.protocol.value,
                "bus": _plain(bundle.bus),
                "device": _plain(bundle.device),
                "scaling": _plain(bundle.scaling),
                "safety": _plain(bundle.safety),
                "loaded_test_config": _plain(bundle.test_config),
            }
        )
    path.write_text(yaml.safe_dump(snapshot, allow_unicode=True, sort_keys=False), encoding="utf-8")
    return path


def _plain(value):
    if value is None:
        return None
    if is_dataclass(value):
        return _plain(asdict(value))
    if isinstance(value, Enum):
        return value.value
    if isinstance(value, Path):
        return str(value)
    if isinstance(value, dict):
        return {str(key): _plain(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [_plain(item) for item in value]
    return value
