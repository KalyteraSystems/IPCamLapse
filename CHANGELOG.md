# Changelog

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
