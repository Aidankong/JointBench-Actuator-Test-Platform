from __future__ import annotations

import numpy as np

from jointbench.models import ActuatorState, StepResponseMetrics, TestConfig


def analyze_step_response(samples: list[ActuatorState], config: TestConfig) -> StepResponseMetrics:
    if len(samples) < 5:
        return StepResponseMetrics(None, None, None, 0.0, None, 0.0, 0.0, 0.0, None)

    t = np.array([s.timestamp_s for s in samples], dtype=float)
    pos = np.array([s.actual_position_deg for s in samples], dtype=float)
    current = np.array([s.current_a for s in samples], dtype=float)
    temp = np.array([s.temperature_c for s in samples], dtype=float)

    p0 = config.start_position_deg
    target = config.target_position_deg
    delta = target - p0
    abs_delta = abs(delta)
    if abs_delta < 1e-9:
        return StepResponseMetrics(None, None, None, 0.0, None, float(np.max(np.abs(current))), float(np.mean(np.abs(current))), float(np.max(temp)), None)

    direction = 1.0 if delta > 0 else -1.0
    moved = direction * (pos - p0)
    response_threshold = max(0.5, 0.02 * abs_delta)
    delay_idx = _first_index(moved >= response_threshold)
    response_delay = _time_at(t, delay_idx)

    rise_10_idx = _first_index(moved >= 0.10 * abs_delta)
    rise_90_idx = _first_index(moved >= 0.90 * abs_delta)
    rise_time = None
    if rise_10_idx is not None and rise_90_idx is not None and rise_90_idx >= rise_10_idx:
        rise_time = float(t[rise_90_idx] - t[rise_10_idx])

    if direction > 0:
        overshoot = max(0.0, (float(np.max(pos)) - target) / abs_delta * 100.0)
    else:
        overshoot = max(0.0, (target - float(np.min(pos))) / abs_delta * 100.0)

    band = max(0.05, config.settling_band_pct / 100.0 * abs_delta)
    within_band = np.abs(pos - target) <= band
    settling_idx = _settling_index(within_band, config.sample_rate_hz)
    settling_time = _time_at(t, settling_idx)

    tail_count = max(3, int(len(pos) * 0.1))
    tail = pos[-tail_count:]
    steady_state_error = float(np.mean(tail) - target)
    jitter = float(np.std(tail))

    return StepResponseMetrics(
        response_delay_s=response_delay,
        rise_time_s=rise_time,
        settling_time_s=settling_time,
        overshoot_pct=float(overshoot),
        steady_state_error_deg=steady_state_error,
        peak_current_a=float(np.max(np.abs(current))),
        average_current_a=float(np.mean(np.abs(current))),
        max_temperature_c=float(np.max(temp)),
        jitter_deg=jitter,
    )


def judge_step_response(
    metrics: StepResponseMetrics,
    config: TestConfig,
    *,
    aborted: bool = False,
    failure_reasons: list[str] | None = None,
) -> tuple[str, list[str]]:
    reasons = list(failure_reasons or [])
    if aborted:
        return "ABORTED", reasons or ["Test aborted before completion."]

    if metrics.settling_time_s is None or metrics.steady_state_error_deg is None:
        return "INVALID", reasons or ["Response did not produce enough valid analysis data."]

    if metrics.overshoot_pct > config.max_overshoot_pct:
        reasons.append(f"Overshoot {metrics.overshoot_pct:.2f}% > {config.max_overshoot_pct:.2f}%.")
    if metrics.settling_time_s > config.max_settling_time_s:
        reasons.append(f"Settling time {metrics.settling_time_s:.3f}s > {config.max_settling_time_s:.3f}s.")
    if abs(metrics.steady_state_error_deg) > config.max_steady_state_error_deg:
        reasons.append(
            f"Steady-state error {metrics.steady_state_error_deg:.3f}deg > {config.max_steady_state_error_deg:.3f}deg."
        )
    if metrics.peak_current_a > config.max_current_a:
        reasons.append(f"Peak current {metrics.peak_current_a:.2f}A > {config.max_current_a:.2f}A.")
    if metrics.max_temperature_c > config.max_temperature_c:
        reasons.append(f"Max temperature {metrics.max_temperature_c:.1f}C > {config.max_temperature_c:.1f}C.")

    return ("FAIL" if reasons else "PASS"), reasons


def _first_index(mask: np.ndarray) -> int | None:
    indexes = np.flatnonzero(mask)
    if indexes.size == 0:
        return None
    return int(indexes[0])


def _time_at(times: np.ndarray, index: int | None) -> float | None:
    if index is None:
        return None
    return float(times[index])


def _settling_index(within_band: np.ndarray, sample_rate_hz: float) -> int | None:
    min_tail = max(5, int(0.15 * sample_rate_hz))
    for idx in range(0, len(within_band) - min_tail + 1):
        tail = within_band[idx:]
        if len(tail) < min_tail:
            break
        if float(np.mean(tail)) >= 0.98:
            return idx
    return None
