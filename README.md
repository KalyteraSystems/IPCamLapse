# IPCamLapse

IPCamLapse captures JPEG snapshots from IP cameras and creates H.264 timelapse videos with FFmpeg.

Current version: `0.1.0` (preview).

## Features

- Concurrent capture sessions with pause and resume
- Configurable intervals, capture lengths, and video lengths
- Live progress and latest-frame previews
- H.264 MP4 generation and downloads
- Local session storage with protected camera passwords

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [FFmpeg](https://ffmpeg.org/download.html)
- An HTTP or HTTPS JPEG snapshot endpoint

IPCamLapse checks for FFmpeg beside the application, then in `/usr/bin`, `/usr/local/bin`, and `/opt/homebrew/bin` on Unix systems.

## Run

```console
git clone https://github.com/KalyteraSystems/IPCamLapse.git
cd IPCamLapse
dotnet restore --locked-mode
dotnet run --project IPCamLapse --urls http://127.0.0.1:5080
```

Open <http://127.0.0.1:5080> and enter the camera's snapshot URL. For Windows, `ffmpeg.exe` can be placed beside `IPCamLapse.exe`.

## Security

- The web UI accepts loopback connections only and has no user authentication.
- Camera URLs are limited to HTTP or HTTPS private, loopback, or link-local IP addresses by default.
- TLS certificates are validated unless disabled for a specific camera.
- Passwords are protected with ASP.NET Core Data Protection.
- Snapshot responses are size-limited and validated as JPEG images.
- State-changing requests require an anti-forgery token.

Do not bind the app directly to a LAN or the internet. See [SECURITY.md](SECURITY.md) for remote-access assumptions and vulnerability reporting.

## Configuration

Environment variables use double underscores, such as `CameraAccess__MaxSnapshotBytes=10485760`.

| Setting | Default | Purpose |
|---|---:|---|
| `CameraAccess:AllowHostnames` | `false` | Allow DNS hostnames in camera URLs |
| `CameraAccess:AllowPublicAddresses` | `false` | Allow public camera addresses |
| `CameraAccess:MaxSnapshotBytes` | `20971520` | Limit snapshot size in bytes |

Allowing hostnames or public addresses increases server-side request forgery exposure. The snapshot limit must be between 1 KiB and 100 MiB.

## Data

Runtime data is stored below `IPCamLapse/data/sessions/` and excluded from Git. A running session is restored as paused after an application restart.

Videos use H.264, `yuv420p`, 1280×720 output, CRF 23, and fast-start metadata.

## Development

```console
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```

CI runs on Windows and Ubuntu. See [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

## License

[Apache License 2.0](LICENSE). Browser-library licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
