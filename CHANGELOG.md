# Changelog

## Unreleased

### Added

- Alpha OpenCamInterop .NET library with validated CloudEvents models, Frigate object-event and ONVIF notification transformers, v1 JSON Schemas, and synthetic fixtures
- Read-only IPCamLapse activity export using CloudEvents batch JSON
- Offline OpenCamInterop EventLab inspect/verify/replay CLI, executable fixture manifest, and generated compatibility matrix
- Standalone OpenCamInterop solution, contributor boundary, and Windows/Ubuntu validation while IPCamLapse retains a source-integrated first-party copy
- `GET /healthz` container liveness endpoint (empty `204`, local-only), a Docker health check, and a container runtime smoke test in CI
- `CameraAccess:RequestTimeoutSeconds` setting for camera HTTP requests (default 30 seconds, range 1–120)
- Validated PNG camera snapshots alongside JPEG
- Test connection for saved and demo camera profiles from the Cameras page
- File size on each timeline frame, including frames added by Load more
- Retention preview on the Storage page: eligible sessions and estimated bytes freed before a run
- Active scheduling time zone and current offset on the New session and System check pages

### Changed

- Recurring daily and weekly windows use one time zone resolved at startup, while one-time starts stay stored as UTC instants. A wall time skipped by a spring-forward change moves forward by the DST gap, an ambiguous one-time start uses the earlier instant, and a recurring window that starts or ends in a repeated fall-back hour is split at the offset change so the wall-clock gap between the two occurrences is not captured
- Retention runs and manual session deletion no longer report success while files remain on disk; a session that could not be removed stays listed, and retention retries it on the next run
- Recent activity events are read from the end of the log file instead of loading the whole file
- Release publication creates a draft release, uploads every archive, and then publishes it; only the final release job can write repository contents
- Code ownership and the maintainer link point to Kalytera Systems
- Test dependencies updated to Microsoft.AspNetCore.Mvc.Testing 10.0.12 and Microsoft.NET.Test.Sdk 18.10.1

### Fixed

- A failed Timeline **Load more** request shows a retryable status instead of failing silently, and a retry cannot duplicate frames
- Running capture loops are awaited during application shutdown

### Security

- Interoperability inputs and batches are bounded; XML DTDs and duplicate JSON members are rejected
- Frigate fields are allowlisted, generic ONVIF values are redacted, and IPCamLapse exports omit credentials, URLs, paths, and raw messages

## 0.4.4 - 2026-09-04

### Added

- CodeQL security analysis for pushes, pull requests, and weekly scans
- Linux ARM64 self-contained release archive
- Activity-log download from the session page

### Changed

- Main-branch and versioned container images now publish for Linux AMD64 and ARM64
- Redundant container runs for the same ref are cancelled automatically

### Fixed

- Container replacements now retain Data Protection keys in the persistent data volume so saved camera passwords remain readable

Container users upgrading from v0.4.3 or earlier should follow the key-ring migration step in the README before replacing the old container.

## 0.4.3 - 2026-08-30

### Fixed

- Portable command aliases now follow their final executable link before locating web assets

## 0.4.2 - 2026-08-30

### Fixed

- WinGet command aliases now resolve web assets from the installed package directory

## 0.4.1 - 2026-08-30

### Fixed

- Self-contained and portable builds now find their web assets when launched from any working directory
- New installations keep runtime data outside the application directory while existing adjacent `data` directories continue to work

## 0.4.0 - 2026-08-27

### Added

- Non-root Docker image with FFmpeg and persistent storage
- Linux AMD64 and ARM64 images published to GitHub Container Registry
- Docker Compose setup bound to host loopback
- Roadmap and maintainer release guide

### Security

- Private bridge clients require an explicit opt-in and public client addresses remain blocked

## 0.3.0 - 2026-08-27

### Added

- Kalytera Systems interface and responsive timeline gallery
- Demo capture, camera profiles, schedules, storage policies, and video controls
- Self-contained Windows and Linux releases
- Unit and integration coverage for timing, capture, rendering, and downloads
