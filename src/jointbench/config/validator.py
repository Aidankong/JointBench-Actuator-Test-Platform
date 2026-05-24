from __future__ import annotations

from jointbench.config.schemas import ProtocolConfigBundle, ProtocolType, ValidationIssue, ValidationReport


def validate_bundle(bundle: ProtocolConfigBundle) -> ValidationReport:
    issues: list[ValidationIssue] = []
    protocol = bundle.protocol

    if protocol is ProtocolType.MOCK:
        return ValidationReport(issues, motion_allowed=True)

    if protocol is ProtocolType.CANOPEN_CIA402:
        if not bundle.bus.channel:
            issues.append(ValidationIssue("error", "CANopen channel is required."))
        if bundle.bus.bitrate is None:
            issues.append(ValidationIssue("error", "CANopen bitrate is required."))
        if bundle.bus.node_id is None:
            issues.append(ValidationIssue("error", "CANopen node_id is required."))

    if protocol is ProtocolType.ETHERCAT_COE_CIA402:
        if not bundle.bus.interface:
            issues.append(ValidationIssue("error", "EtherCAT interface is required."))
        if bundle.bus.slave_index is None:
            issues.append(ValidationIssue("error", "EtherCAT slave_index is required."))
        if bundle.bus.cycle_time_ms is None:
            issues.append(ValidationIssue("error", "EtherCAT cycle_time_ms is required."))
        if "esi" not in bundle.artifacts:
            issues.append(ValidationIssue("warning", "ESI XML is not loaded; PDO validation will be unavailable."))

    if bundle.safety is None:
        issues.append(ValidationIssue("error", "Safety config is required for real bus protocols."))
    elif not bundle.safety.has_motion_limits:
        issues.append(ValidationIssue("error", "Safety min_position_deg and max_position_deg are required."))

    if not bundle.scaling.has_position_scaling:
        issues.append(ValidationIssue("error", "Position scaling requires encoder_counts_per_rev and gear_ratio."))

    for name, object_ref in bundle.device.object_map.required_items().items():
        if not object_ref:
            issues.append(ValidationIssue("error", f"CiA402 object map missing {name}."))

    if bundle.device.vendor_id is None:
        issues.append(ValidationIssue("warning", "Device vendor_id is not configured; identity match will be weaker."))
    if bundle.device.product_code is None:
        issues.append(ValidationIssue("warning", "Device product_code is not configured; identity match will be weaker."))

    motion_allowed = not [issue for issue in issues if issue.level == "error"]
    return ValidationReport(issues, motion_allowed=motion_allowed)
