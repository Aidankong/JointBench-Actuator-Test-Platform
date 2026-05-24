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
- Protocol: {{ result.device_info.protocol }}
- Transport: {{ result.device_info.transport_mode }}
- Firmware: {{ result.device_info.firmware_version }}
- ADS Host: {{ result.device_info.ads_host if result.device_info.ads_host else "N/A" }}
- AMS Net ID: {{ result.device_info.ams_net_id if result.device_info.ams_net_id else "N/A" }}
- AMS Port: {{ result.device_info.ams_port if result.device_info.ams_port is not none else "N/A" }}
- ADS Symbol Prefix: {{ result.device_info.ads_symbol_prefix if result.device_info.ads_symbol_prefix else "N/A" }}
- TwinCAT Route Status: {{ result.device_info.twincat_route_status if result.device_info.twincat_route_status else "N/A" }}
- Operation Enabled: {{ result.operation_enabled if result.operation_enabled is not none else "N/A" }}
- Node ID: {{ result.device_info.node_id if result.device_info.node_id is not none else "N/A" }}
- Slave Index: {{ result.device_info.slave_index if result.device_info.slave_index is not none else "N/A" }}
- Vendor ID: {{ "0x%08X"|format(result.device_info.vendor_id) if result.device_info.vendor_id is not none else "N/A" }}
- Product Code: {{ "0x%08X"|format(result.device_info.product_code) if result.device_info.product_code is not none else "N/A" }}
- Revision: {{ "0x%08X"|format(result.device_info.revision_number) if result.device_info.revision_number is not none else "N/A" }}
- Final Statusword: {{ "0x%04X"|format(result.final_statusword) if result.final_statusword is not none else "N/A" }}
- Final Error Code: {{ "0x%04X"|format(result.final_error_code) if result.final_error_code is not none else "N/A" }}

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

## Configuration Files

{% if result.config_files -%}
| Type | Path | SHA256 |
|---|---|---|
{% for key, path in result.config_files.items() -%}
| {{ key }} | `{{ path }}` | `{{ result.config_hashes.get(key, "N/A") }}` |
{% endfor -%}
{% else -%}
- None
{% endif %}
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
    <tr><th>Protocol</th><td>{{ result.device_info.protocol }}</td></tr>
    <tr><th>Transport</th><td>{{ result.device_info.transport_mode }}</td></tr>
    <tr><th>Firmware</th><td>{{ result.device_info.firmware_version }}</td></tr>
    <tr><th>ADS Host</th><td>{{ result.device_info.ads_host if result.device_info.ads_host else "N/A" }}</td></tr>
    <tr><th>AMS Net ID</th><td>{{ result.device_info.ams_net_id if result.device_info.ams_net_id else "N/A" }}</td></tr>
    <tr><th>AMS Port</th><td>{{ result.device_info.ams_port if result.device_info.ams_port is not none else "N/A" }}</td></tr>
    <tr><th>ADS Symbol Prefix</th><td>{{ result.device_info.ads_symbol_prefix if result.device_info.ads_symbol_prefix else "N/A" }}</td></tr>
    <tr><th>TwinCAT Route Status</th><td>{{ result.device_info.twincat_route_status if result.device_info.twincat_route_status else "N/A" }}</td></tr>
    <tr><th>Operation Enabled</th><td>{{ result.operation_enabled if result.operation_enabled is not none else "N/A" }}</td></tr>
    <tr><th>Node ID</th><td>{{ result.device_info.node_id if result.device_info.node_id is not none else "N/A" }}</td></tr>
    <tr><th>Slave Index</th><td>{{ result.device_info.slave_index if result.device_info.slave_index is not none else "N/A" }}</td></tr>
    <tr><th>Vendor ID</th><td>{{ "0x%08X"|format(result.device_info.vendor_id) if result.device_info.vendor_id is not none else "N/A" }}</td></tr>
    <tr><th>Product Code</th><td>{{ "0x%08X"|format(result.device_info.product_code) if result.device_info.product_code is not none else "N/A" }}</td></tr>
    <tr><th>Revision</th><td>{{ "0x%08X"|format(result.device_info.revision_number) if result.device_info.revision_number is not none else "N/A" }}</td></tr>
    <tr><th>Final Statusword</th><td>{{ "0x%04X"|format(result.final_statusword) if result.final_statusword is not none else "N/A" }}</td></tr>
    <tr><th>Final Error Code</th><td>{{ "0x%04X"|format(result.final_error_code) if result.final_error_code is not none else "N/A" }}</td></tr>
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

  <h2>Configuration Files</h2>
  {% if result.config_files %}
  <table>
    <tr><th>Type</th><th>Path</th><th>SHA256</th></tr>
    {% for key, path in result.config_files.items() -%}
    <tr><td>{{ key }}</td><td><code>{{ path }}</code></td><td><code>{{ result.config_hashes.get(key, "N/A") }}</code></td></tr>
    {% endfor %}
  </table>
  {% else %}
  <p>None</p>
  {% endif %}
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
