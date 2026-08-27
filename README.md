# IPCamLapse

IPCamLapse is a local-first .NET web app that captures JPEG snapshots from IP cameras and turns them into downloadable H.264 timelapse videos with FFmpeg.

It is designed for home labs, construction projects, gardens, workshops, and other private-network cameras. Multiple capture sessions can run independently, survive restarts, and report progress live in the browser.

> **Release status:** early preview (`0.1.0`). Back up important footage and review the [security model](#security-model) before use.

## Highlights

- Multiple start, pause, resume, cancel, and delete workflows
- Built-in presets from a five-minute test to a year-long capture
- Custom capture interval, duration, and target video length
- Live progress and latest-frame previews with SignalR
- Partial previews while a capture is still running
- Automatic H.264 MP4 generation with browser streaming and downloads
- JSON session persistence with camera passwords protected at rest
- Private/loopback camera targets and strict TLS by default
- Windows, Linux, and macOS support wherever .NET 10 and FFmpeg run

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A recent [FFmpeg](https://ffmpeg.org/download.html) build
- An IP camera with an HTTP or HTTPS JPEG snapshot endpoint

IPCamLapse looks for `ffmpeg.exe` or `ffmpeg` beside the built application first, then checks `/usr/bin`, `/usr/local/bin`, and `/opt/homebrew/bin` on Unix-like systems.

## Quick start

```console
git clone https://github.com/KalyteraSystems/IPCamLapse.git
cd IPCamLapse
dotnet restore --locked-mode
dotnet run --project IPCamLapse --urls http://127.0.0.1:5080
```

Open <http://127.0.0.1:5080>, choose **New session**, and enter the camera's snapshot URL. An IP-literal URL such as `http://192.168.1.25/snapshot.jpg` works with the secure defaults.

For a portable Windows deployment, copy `ffmpeg.exe` next to `IPCamLapse.exe`. On Debian/Ubuntu use `sudo apt-get install ffmpeg`; on macOS with Homebrew use `brew install ffmpeg`.

## Security model

IPCamLapse handles camera credentials and can fetch network resources, so its defaults are intentionally restrictive:

- The web UI accepts loopback connections only. Do not bind it directly to a LAN or the internet.
- Camera URLs must use HTTP or HTTPS and target a private, loopback, or link-local IP literal by default.
- Hostnames and public IP addresses require explicit configuration. Hostname allow-listing is planned; enabling hostnames today also introduces DNS-rebinding risk.
- TLS certificates are validated by default. Invalid or self-signed certificates can be accepted for one camera only through an explicit checkbox.
- Camera test credentials are sent in a protected POST body, not a URL query string.
- Passwords in session JSON are protected with ASP.NET Core Data Protection. The local application process can still access them, and moving data without the matching Data Protection keys can make them unreadable.
- Snapshot responses are size-limited and must look like JPEG images.
- State-changing API requests require an anti-forgery token.

For remote access, place IPCamLapse behind an authenticated TLS reverse proxy that connects to the app over loopback. The reverse proxy must replace, not bypass, the loopback boundary. See [SECURITY.md](SECURITY.md) for vulnerability reporting and supported assumptions.

## Configuration

Settings follow standard ASP.NET Core configuration conventions. Environment variables use double underscores, for example `CameraAccess__MaxSnapshotBytes=10485760`.

| Setting | Default | Purpose |
|---|---:|---|
| `CameraAccess:AllowHostnames` | `false` | Permit DNS hostnames in camera URLs |
| `CameraAccess:AllowPublicAddresses` | `false` | Permit publicly routable camera targets |
| `CameraAccess:MaxSnapshotBytes` | `20971520` | Maximum accepted snapshot size in bytes |

Only enable broader camera access when you understand the server-side request forgery implications. The maximum snapshot size must be between 1 KiB and 100 MiB.

## Data and video output

Runtime data is stored below `IPCamLapse/data/sessions/` and excluded from Git:

```text
data/sessions/
├── <session-id>.json
└── <session-id>/
    ├── images/frame_000001_....jpg
    ├── partial_timelapse.mp4
    └── timelapse.mp4
```

When the app restarts, a previously running session is restored as paused. Video generation uses FFmpeg with H.264, `yuv420p`, 1280×720 output, CRF 23, and fast-start metadata.

## Development

```console
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```

The solution includes unit tests for camera URL policy, local-only access, protected credential persistence, and storage path containment. CI runs the build and test suite on Windows and Ubuntu.

## Roadmap

- Authenticated remote-access mode with documented proxy deployments
- Explicit hostname allowlists and DNS-rebinding-resistant connection handling
- Camera adapters and better snapshot diagnostics
- Retention limits and disk-space controls
- Container packaging after the remote-access security model is complete
- Broader integration and FFmpeg test coverage

## Contributing

Issues and pull requests are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), follow the [Code of Conduct](CODE_OF_CONDUCT.md), and use a private GitHub Security Advisory for vulnerabilities.

## License

Licensed under the [Apache License 2.0](LICENSE). Bundled browser libraries retain their respective licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
