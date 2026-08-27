# Architecture

IPCamLapse is a local-first ASP.NET Core application. Razor Pages serve the interface, minimal APIs handle live actions and downloads, and a hosted service owns capture work. Runtime data stays in the configured data directory.

## Main components

| Component | Responsibility |
|---|---|
| Razor Pages | Sessions, camera profiles, storage settings, system checks, gallery, and video controls |
| Minimal APIs | Session actions, camera tests, frames, status, rendering, and downloads |
| `CaptureBackgroundService` | Session lifecycle, fixed-timeline capture, retries, scheduling, and rendering transitions |
| `CaptureSessionService` | Migration-safe JSON persistence and protected one-time camera credentials |
| `CameraProfileService` | Reusable camera endpoints and protected passwords |
| `CameraService` | Bounded HTTP snapshot requests, JPEG validation, diagnostics, and demo frames |
| `FrameCatalogService` | Frame discovery, downloads, range selection, and the append-only event log |
| `VideoService` | FFmpeg concat input, resize/crop filters, overlays, quality, and MP4 output |
| `StorageService` | Usage estimates, disk reserve, storage budget, and retention cleanup |
| `SystemHealthService` | FFmpeg, write-permission, and free-space checks |
| SignalR hub | Live state, progress, and camera diagnostics |

## Capture lifecycle

```text
Ready ──start──> Capturing <──resume── Paused
                    │                    ▲
                    ├──outside window──> Scheduled
                    │                    │
                    ├──duration met──> Rendering ──success──> Completed
                    │                       │
                    └──failure limit────────┴──error───────> Failed
```

`Cancelled` is a terminal user action. A scheduled session moves between `Scheduled` and `Capturing` without consuming active capture time outside its window.

## Timing model

Each session stores completed active seconds and the start of its current active segment. Pausing closes that segment, so wall-clock time spent paused or waiting for a schedule does not advance progress.

Capture deadlines advance from the previous deadline, not from the end of the last camera request. If a request takes longer than an interval, missed slots are skipped and the next future point on the original timeline is selected. This prevents drift during long-running captures.

## Data layout

```text
data/
├── camera-profiles.json
├── settings.json
└── sessions/
    ├── <session-id>.json
    └── <session-id>/
        ├── events.jsonl
        ├── images/
        │   └── frame_<number>_<utc-timestamp>.<jpg|png>
        ├── partial_timelapse.mp4
        └── timelapse.mp4
```

Session JSON files are written atomically. Paths loaded from disk are normalized back into the session directory. Passwords are protected with ASP.NET Core Data Protection before persistence.

## Security boundary

The supported deployment is one trusted machine serving the UI over loopback. Camera requests default to literal private, loopback, or link-local addresses. HTTP responses are bounded and validated before they become frames. All state-changing API calls require antiforgery validation.

The application has no user authentication and should not be bound directly to a LAN or the internet.

## Tests

Unit tests cover URL policy, persistence boundaries, deterministic pause/resume timing, fixed capture deadlines, and schedule windows. The integration suite starts the application in memory, runs a demo capture, crosses the render boundary, and downloads the resulting video through the HTTP API.
