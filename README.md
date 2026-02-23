# IPCamLapse

A full-featured **.NET 10 Razor Pages** web application that captures snapshots from any IP camera's HTTP snapshot endpoint and assembles them into downloadable timelapse videos using FFmpeg.

---

## Features

- **Multi-session management** — create, name, start, pause, resume, and delete independent timelapse sessions, all persisted across restarts
- **Flexible camera support** — any camera that exposes an HTTP/HTTPS snapshot URL; HTTP Basic authentication supported; self-signed TLS certificates accepted automatically
- **Built-in presets** — six recommended configurations from a 5-minute quick test to a full year-long timelapse, each pre-calculating the capture interval and video duration for smooth results
- **Custom configuration** — set any capture interval (≥ 5 s), total capture duration, and target video length
- **Live dashboard** — session cards update in real time via SignalR showing frame count, progress percentage, and time of last capture
- **Live frame preview** — the Details page shows the most recently captured frame, auto-refreshing while a session runs
- **Partial video generation** — generate and download/play a preview video from frames captured so far, without stopping the session
- **Final video generation** — H.264 MP4 produced automatically when the configured duration is reached; frame rate is calculated to match the target video length
- **In-browser video player** — HTML5 `<video>` element with HTTP range-request streaming directly from the server; no third-party hosting needed
- **Downloadable video** — one-click download of both partial and final videos
- **Responsive UI** — Bootstrap 5.3 dark sidebar layout with Bootstrap Icons; works on desktop and mobile
- **Disk-safe persistence** — sessions are written as JSON files and images are stored as numbered JPEG frames; a server restart resumes sessions from their last-known state (status moves to *Paused*)

---

## Prerequisites

| Requirement | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** or newer |
| [FFmpeg](https://ffmpeg.org/download.html) | Any recent build |

FFmpeg does **not** need to be installed system-wide — see [FFmpeg setup](#ffmpeg-setup) below.

---

## Quick Start

```bash
# 1. Clone the repository
git clone https://github.com/elkampu/IPCamLapse.git
cd IPCamLapse

# 2. (Optional) Place ffmpeg next to the built executable — see FFmpeg Setup below

# 3. Build and run
cd IPCamLapse
dotnet run
```

The application starts on `https://localhost:5001` (HTTPS) and `http://localhost:5000` by default.  
Open `https://localhost:5001` in your browser, click **New Session**, and fill in your camera details.

---

## FFmpeg Setup

IPCamLapse searches for the `ffmpeg` binary in the following order and uses the first one it finds:

| Priority | Location |
|---|---|
| **1** | Same directory as the application executable (`AppContext.BaseDirectory`) |
| 2 | `/usr/bin` |
| 3 | `/usr/local/bin` |
| 4 | `/opt/homebrew/bin` |

### Drop-in binary (recommended for Windows / portable installs)

1. Download a static FFmpeg build from <https://ffmpeg.org/download.html>
2. Copy `ffmpeg.exe` (Windows) or `ffmpeg` (Linux/macOS) into the folder where `IPCamLapse.exe` / `IPCamLapse.dll` lives:

```
IPCamLapse/
├── IPCamLapse.exe      ← your application
└── ffmpeg.exe          ← drop it here
```

The application automatically picks it up — no configuration required.

### System-wide install (Linux / macOS)

```bash
# Debian/Ubuntu
sudo apt-get install ffmpeg

# macOS (Homebrew)
brew install ffmpeg
```

If FFmpeg cannot be found at startup, video generation returns an error and logs:
> `ffmpeg binary not found. Place ffmpeg alongside the application executable or install it system-wide.`

---

## Built-in Presets

| Preset | Capture interval | Total duration | Target video |
|---|---|---|---|
| ☀️ 1-Day Highlight | every 5 min | 1 day | 30 s |
| 📅 1-Week Summary | every 30 min | 1 week | 60 s |
| 🗓️ 1-Month Overview | every 1 hour | 1 month | 90 s |
| 🏗️ 3-Month Project | every 2 hours | 3 months | 2 min |
| 🌍 1-Year Journey | every 4 hours | 1 year | 3 min |
| 🧪 Quick Test | every 10 s | 5 min | 10 s |

Selecting a preset pre-fills all fields on the Create Session form. All values can be adjusted freely before saving.

---

## Project Structure

```
IPCamLapse/
├── Hubs/
│   └── ProgressHub.cs              # SignalR hub — clients join per-session groups
├── Models/
│   ├── CaptureSession.cs           # Session entity + CaptureConfiguration + SessionStatus enum
│   └── TimeLapsePreset.cs          # Built-in preset definitions
├── Pages/
│   ├── Index.cshtml(.cs)           # Dashboard — live session cards
│   └── Sessions/
│       ├── Create.cshtml(.cs)      # New session form with preset picker
│       └── Details.cshtml(.cs)     # Live progress, frame preview, video player
├── Services/
│   ├── CameraService.cs            # HTTP snapshot fetcher (Basic auth, self-signed TLS)
│   ├── CaptureBackgroundService.cs # Per-session background capture loops
│   ├── CaptureSessionService.cs    # In-memory store + JSON disk persistence
│   └── VideoService.cs             # FFMpegCore pipeline → H.264 MP4
├── wwwroot/                        # Static assets (Bootstrap 5.3, Bootstrap Icons, SignalR client)
├── Program.cs                      # DI wiring + minimal API endpoints + SignalR mapping
└── IPCamLapse.csproj               # net10.0, FFMpegCore 5.1.0
```

---

## REST API

All endpoints are registered in `Program.cs`.

### Sessions

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/sessions/{id}/start` | Start or resume a session |
| `POST` | `/api/sessions/{id}/stop` | Pause a running session |
| `POST` | `/api/sessions/{id}/cancel` | Cancel and mark session as Cancelled |
| `DELETE` | `/api/sessions/{id}` | Cancel and permanently delete a session and its data |
| `GET` | `/api/sessions/{id}/status` | JSON status snapshot (progress, frame count, remaining time, video availability) |
| `POST` | `/api/sessions/{id}/generate-partial-video` | Trigger partial video generation from frames captured so far |

### Media

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/sessions/{id}/video` | Stream the final or partial MP4 (HTTP range requests supported) |
| `GET` | `/api/sessions/{id}/preview` | Latest captured JPEG frame |

### Camera utilities

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/camera/test?url=…&username=…&password=…` | Test connectivity to a camera URL |
| `GET` | `/api/camera/snapshot?url=…&username=…&password=…` | Fetch and return a single live snapshot |

### SignalR Hub — `/progressHub`

| Client method | Payload | When sent |
|---|---|---|
| `ProgressUpdate` | `{ sessionId, frameCount, progressPercent, lastCaptureAt, status }` | After each captured frame |
| `SessionCompleted` | `{ sessionId, hasVideo }` | When the configured duration is reached and the final video is ready |

Clients call `connection.invoke("JoinSession", sessionId)` to subscribe to a session's updates.

---

## Data Persistence

Session metadata (configuration, status, frame count, timestamps) is stored as a JSON file per session:

```
IPCamLapse/data/sessions/
├── {sessionId}.json        ← session metadata
└── {sessionId}/
    ├── images/
    │   ├── frame_000001_20240101_120000.jpg
    │   └── …
    ├── timelapse.mp4        ← final video (generated on completion)
    └── partial_timelapse.mp4  ← overwritten each time partial generation is triggered
```

The `data/` directory is excluded from version control by `.gitignore`.

On application restart, sessions previously in the *Running* state are loaded back as *Paused* and can be resumed manually.

---

## Video Generation

The FFMpegCore pipeline uses the **concat demuxer** to assemble frames in capture order, avoiding any filename glob ordering issues:

- **Codec:** H.264 (`libx264`)
- **Pixel format:** `yuv420p` (maximum player compatibility)
- **Scale:** 1280 × 720 (HD)
- **CRF:** 23 (good quality/size balance)
- **Fast-start:** `+faststart` flag moves the MP4 index to the front for instant browser playback
- **Audio:** none (`-an`)
- **Frame rate:** automatically calculated as `min(60, frameCount / targetDurationSeconds)`

---

## Configuration

`appsettings.json` / `appsettings.Development.json` follow standard ASP.NET Core conventions. No application-specific keys are required — all session data is derived at runtime.

To change the listening URL or port, set the `ASPNETCORE_URLS` environment variable or use `--urls` on the command line:

```bash
dotnet run --urls "http://0.0.0.0:8080"
```