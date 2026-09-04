# Roadmap

IPCamLapse is developed in small, testable releases. Issues are the source of truth for work that is ready to pick up.

## Shipped

- Explicit capture states and pause-safe timing
- Fixed-timeline capture with retry diagnostics
- Demo camera, scheduling, camera profiles, and storage policies
- Timeline gallery and configurable video rendering
- Self-contained Windows x64, Linux x64, and Linux ARM64 releases
- Container images for Linux AMD64 and ARM64
- End-to-end capture, render, and download tests
- Alpha OpenCamInterop event contracts, synthetic fixtures, Frigate/ONVIF transformers, and IPCamLapse CloudEvents export

## Next

- WinGet installation for Windows
- Small timeline and activity-log improvements ([good first issues](https://github.com/KalyteraSystems/IPCamLapse/labels/good%20first%20issue))
- Fixture-driven OpenCamInterop compatibility cases for genuinely distinct camera and NVR behavior
- An offline `inspect` and deterministic `replay` CLI prototype for sanitized event traces

## Later

- More camera-specific setup guides
- Optional authenticated remote access
- Import and export for settings and profiles
- Performance measurements for long capture sessions
- Transport-owned capture helpers only after the offline trace and replay contract is stable
- A separate OpenCamInterop repository only after there is an external consumer and meaningful multi-vendor trace coverage

## Proposing work

Open a feature request with the user problem, expected behavior, and any security or migration impact. Substantial changes should be discussed before implementation; focused fixes can go straight to a pull request.
