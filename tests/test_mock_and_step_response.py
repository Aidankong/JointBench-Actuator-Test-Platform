from __future__ import annotations

import csv

from jointbench.analysis import judge_step_response
from jointbench.comm import MockActuatorAdapter
from jointbench.models import TestConfig as StepTestConfig
from jointbench.test_cases import run_position_step_test


def test_mock_step_response_reaches_target(tmp_path):
    adapter = MockActuatorAdapter()
    config = StepTestConfig()

    result = run_position_step_test(adapter, config, tmp_path, sleep=False)

    assert result.result == "PASS"
    assert result.metrics.steady_state_error_deg is not None
    assert abs(result.metrics.steady_state_error_deg) <= config.max_steady_state_error_deg
    assert result.metrics.settling_time_s is not None
    assert result.metrics.settling_time_s <= config.max_settling_time_s
    assert result.metrics.overshoot_pct <= config.max_overshoot_pct
    assert result.metrics.peak_current_a <= config.max_current_a


def test_judge_fails_when_threshold_is_exceeded(tmp_path):
    adapter = MockActuatorAdapter()
    config = StepTestConfig(max_overshoot_pct=0.1)

    result = run_position_step_test(adapter, config, tmp_path, sleep=False)

    judged_result, reasons = judge_step_response(result.metrics, config)
    assert judged_result == "FAIL"
    assert any("Overshoot" in reason for reason in reasons)


def test_csv_contains_header_and_samples(tmp_path):
    adapter = MockActuatorAdapter()
    result = run_position_step_test(adapter, StepTestConfig(), tmp_path, sleep=False)

    with result.raw_data_path.open(newline="", encoding="utf-8-sig") as file:
        rows = list(csv.DictReader(file))

    assert rows
    assert rows[0]["test_id"] == result.test_id
    assert "actual_position_deg" in rows[0]
    assert "current_a" in rows[0]


def test_reports_include_result_and_device(tmp_path):
    adapter = MockActuatorAdapter()
    result = run_position_step_test(adapter, StepTestConfig(), tmp_path, sleep=False)

    md = result.report_md_path.read_text(encoding="utf-8-sig")
    html = result.report_html_path.read_text(encoding="utf-8")

    assert result.test_id in md
    assert result.device_info.device_id in md
    assert f"**{result.result}**" in md
    assert result.test_id in html
    assert result.result in html
