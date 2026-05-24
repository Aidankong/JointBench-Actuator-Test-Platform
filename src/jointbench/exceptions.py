from __future__ import annotations


class JointBenchError(Exception):
    """Base exception for JointBench domain failures."""


class ConfigurationError(JointBenchError):
    """Configuration is missing, malformed, or unsafe."""


class DeviceIdentityMismatch(JointBenchError):
    """The connected device does not match the configured profile."""


class ObjectMapError(JointBenchError):
    """Required CiA402 object mapping is missing."""


class CiA402StateError(JointBenchError):
    """The CiA402 state machine failed to reach the requested state."""


class CommunicationTimeout(JointBenchError):
    """The bus did not respond within the configured timeout."""


class SafetyLimitViolation(JointBenchError):
    """A command or feedback value violated configured safety limits."""
