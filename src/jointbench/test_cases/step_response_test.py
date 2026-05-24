from __future__ import annotations

import time
from datetime import datetime
from pathlib import Path
from typing import Callable

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
    if not adapter.is_connected():
        adapter.connect()
    if hasattr(adapter, "reset"):
        adapter.reset(config.start_position_deg)

    device_info = adapter.read_device_info()
    adapter.set_enable(True)
    adapter.set_control_mode("position")
    adapter.send_position_command(config.target_position_deg)

    test_id = datetime.now().strftime("JB%Y%m%d-%H%M%S")
    output_dir = output_root / test_id
    samples: list[ActuatorState] = []
    failure_reasons: list[str] = []
    aborted = False
    dt_s = config.sample_period_s
    sample_count = int(config.duration_s * config.sample_rate_hz) + 1
    start_time = time.perf_counter()

    for index in range(sample_count):
        if stop_requested and stop_requested():
            aborted = True
            failure_reasons.append("User stopped the test.")
            adapter.emergency_stop()
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
            adapter.emergency_stop()
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
    raw_data_path = output_dir / "raw_data.csv"
    report_md_path = output_dir / "report.md"
    report_html_path = output_dir / "report.html"
    save_states_csv(raw_data_path, test_id, samples)

    result = TestResult(
        test_id=test_id,
        result=result_name,
        device_info=device_info,
        config=config,
        metrics=metrics,
        raw_data_path=raw_data_path,
        report_md_path=report_md_path,
        report_html_path=report_html_path,
        output_dir=output_dir,
        failure_reasons=failure_reasons,
        aborted=aborted,
        config_files=getattr(adapter, "config_files", {}),
        config_hashes=getattr(adapter, "config_hashes", {}),
        operation_enabled=getattr(adapter, "operation_enabled", None),
        final_statusword=samples[-1].statusword if samples else None,
        final_error_code=samples[-1].fault_code if samples else None,
    )
    build_reports(result)
    return result


def _safety_failure(state: ActuatorState, config: TestConfig) -> str | None:
    if state.fault_code:
        return f"Device fault code {state.fault_code}."
    if abs(state.actual_position_deg) > config.max_position_abs_deg:
        return f"Position {state.actual_position_deg:.2f}deg exceeded +/-{config.max_position_abs_deg:.2f}deg."
    if state.current_a > config.max_current_a:
        return f"Current {state.current_a:.2f}A exceeded {config.max_current_a:.2f}A."
    if state.temperature_c > config.max_temperature_c:
        return f"Temperature {state.temperature_c:.1f}C exceeded {config.max_temperature_c:.1f}C."
    return None
