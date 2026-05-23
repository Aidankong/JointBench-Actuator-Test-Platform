from __future__ import annotations

from dataclasses import asdict
from datetime import datetime
from pathlib import Path

from jinja2 import Template

from jointbench.models import TestResult


MD_TEMPLATE = Template(
    """# JointBench Test Report

## Summary

- Test ID: {{ result.test_id }}
- Result: **{{ result.result }}**
- Generated At: {{ generated_at }}
- Device: {{ result.device_info.device_id }}
- SN: {{ result.device_info.sn }}
- Adapter: {{ result.device_info.adapter_type }}
- Firmware: {{ result.device_info.firmware_version }}

## Test Configuration

| Item | Value |
|---|---:|
| Start Position | {{ config.start_position_deg }} deg |
| Target Position | {{ config.target_position_deg }} deg |
| Duration | {{ config.duration_s }} s |
| Sample Rate | {{ config.sample_rate_hz }} Hz |
| Settling Band | {{ config.settling_band_pct }} % |
| Max Overshoot | {{ config.max_overshoot_pct }} % |
| Max Settling Time | {{ config.max_settling_time_s }} s |
| Max Steady-State Error | {{ config.max_steady_state_error_deg }} deg |
| Max Current | {{ config.max_current_a }} A |
| Max Temperature | {{ config.max_temperature_c }} C |

## Metrics

| Metric | Value |
|---|---:|
{% for key, value in result.metric_rows() -%}
| {{ key }} | {{ value }} |
{% endfor %}

## Failure Reasons

{% if result.failure_reasons -%}
{% for reason in result.failure_reasons -%}
- {{ reason }}
{% endfor -%}
{% else -%}
- None
{% endif %}

## Artifacts

- Raw data: `{{ result.raw_data_path.name }}`
- Markdown report: `{{ result.report_md_path.name }}`
- HTML report: `{{ result.report_html_path.name }}`
"""
)


HTML_TEMPLATE = Template(
    """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>JointBench Report {{ result.test_id }}</title>
  <style>
    body { font-family: "Segoe UI", Arial, sans-serif; margin: 32px; color: #1f2933; background: #f7f8fa; }
    main { max-width: 980px; margin: 0 auto; background: #fff; border: 1px solid #dde2e8; padding: 28px; }
    h1, h2 { margin-top: 0; }
    .badge { display: inline-block; padding: 6px 10px; border-radius: 4px; font-weight: 700; }
    .PASS { background: #d9f7e8; color: #106b3d; }
    .FAIL, .INVALID, .ABORTED { background: #fde2e2; color: #9b1c1c; }
    table { border-collapse: collapse; width: 100%; margin: 12px 0 24px; }
    th, td { border: 1px solid #d7dde4; padding: 8px 10px; text-align: left; }
    th { background: #eef2f6; }
    code { background: #eef2f6; padding: 2px 4px; border-radius: 3px; }
  </style>
</head>
<body>
<main>
  <h1>JointBench Test Report</h1>
  <p><span class="badge {{ result.result }}">{{ result.result }}</span></p>
  <table>
    <tr><th>Test ID</th><td>{{ result.test_id }}</td></tr>
    <tr><th>Generated At</th><td>{{ generated_at }}</td></tr>
    <tr><th>Device</th><td>{{ result.device_info.device_id }}</td></tr>
    <tr><th>SN</th><td>{{ result.device_info.sn }}</td></tr>
    <tr><th>Adapter</th><td>{{ result.device_info.adapter_type }}</td></tr>
    <tr><th>Firmware</th><td>{{ result.device_info.firmware_version }}</td></tr>
  </table>

  <h2>Metrics</h2>
  <table>
    <tr><th>Metric</th><th>Value</th></tr>
    {% for key, value in result.metric_rows() -%}
    <tr><td>{{ key }}</td><td>{{ value }}</td></tr>
    {% endfor %}
  </table>

  <h2>Configuration</h2>
  <table>
    {% for key, value in config_dict.items() -%}
    <tr><th>{{ key }}</th><td>{{ value }}</td></tr>
    {% endfor %}
  </table>

  <h2>Failure Reasons</h2>
  {% if result.failure_reasons %}
  <ul>{% for reason in result.failure_reasons %}<li>{{ reason }}</li>{% endfor %}</ul>
  {% else %}
  <p>None</p>
  {% endif %}

  <h2>Artifacts</h2>
  <ul>
    <li><a href="{{ result.raw_data_path.name }}">Raw CSV data</a></li>
    <li><a href="{{ result.report_md_path.name }}">Markdown report</a></li>
  </ul>
</main>
</body>
</html>
"""
)


def build_reports(result: TestResult) -> tuple[Path, Path]:
    generated_at = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    context = {
        "result": result,
        "config": result.config,
        "config_dict": asdict(result.config),
        "generated_at": generated_at,
    }
    result.output_dir.mkdir(parents=True, exist_ok=True)
    result.report_md_path.write_text(MD_TEMPLATE.render(**context), encoding="utf-8-sig")
    result.report_html_path.write_text(HTML_TEMPLATE.render(**context), encoding="utf-8")
    return result.report_md_path, result.report_html_path
