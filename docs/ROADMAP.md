# Roadmap

IPCamLapse is developed in small, testable releases. Issues are the source of truth for work that is ready to pick up.

## Shipped

Shipped means merged to `main`. The [Changelog](../CHANGELOG.md) separates tagged releases from unreleased changes.

- Explicit capture states and pause-safe timing
- Fixed-timeline capture with retry diagnostics
- Demo camera, scheduling, camera profiles, and storage policies
- Timeline gallery and configurable video rendering
- Self-contained Windows x64, Linux x64, and Linux ARM64 releases
- Container images for Linux AMD64 and ARM64
- End-to-end capture, render, and download tests
- Alpha OpenCamInterop event contracts, synthetic fixtures, Frigate/ONVIF transformers, and IPCamLapse CloudEvents export
- Offline OpenCamInterop EventLab `inspect`, manifest `verify`, and streaming `replay` commands
- Executable fixture inventory and generated behavior matrix with standalone Windows/Ubuntu checks
- WinGet portable package `KalyteraSystems.IPCamLapse` for Windows x64, starting with 0.4.4
- Activity-log download, tail reads of recent events, retryable timeline Load more, and per-frame file sizes
- Container liveness endpoint and health check, configurable camera timeout, and PNG snapshots
- Explicit schedule time zone with DST-safe recurring windows, camera-profile connection tests, and retention preview

## Next

- Small improvements tracked as [good first issues](https://github.com/KalyteraSystems/IPCamLapse/labels/good%20first%20issue)
- Keep IPCamLapse's OpenCamInterop source copy aligned with reviewed standalone releases
- Fixture-driven OpenCamInterop cases for genuinely distinct externally reported camera and NVR behavior

## Later

- Secure RTSP and RTSPS camera sources ([#12](https://github.com/KalyteraSystems/IPCamLapse/issues/12)); the tracking issue is currently blocked
- More camera-specific setup guides
- Optional authenticated remote access
- Import and export for settings and profiles
- Performance measurements for long capture sessions
- Transport-owned capture helpers only after the offline trace and replay contract is stable

## Proposing work

Open a feature request with the user problem, expected behavior, and any security or migration impact. Substantial changes should be discussed before implementation; focused fixes can go straight to a pull request.
