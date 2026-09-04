# Contributing to IPCamLapse

## Before you start

- Search existing issues before opening a new one.
- Open an issue before making a substantial design change.
- Report security vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
- Keep pull requests focused. Separate unrelated refactors from behavior changes.

## Local setup

Install the .NET 10 SDK. FFmpeg is required for manual video-generation testing; the automated integration suite replaces the encoder at its service boundary.

```console
git clone https://github.com/KalyteraSystems/IPCamLapse.git
cd IPCamLapse
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

The demo camera is the fastest way to exercise capture behavior locally. Component boundaries, state transitions, and the data layout are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

To exercise the production container locally, run `docker compose up --build` and open <http://127.0.0.1:5080>. Keep the loopback-only host port when testing access-control changes.

Before opening a pull request, run:

```console
dotnet format --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```

## Your first contribution

1. Choose an unassigned [`good first issue`](https://github.com/KalyteraSystems/IPCamLapse/labels/good%20first%20issue) and comment with the approach you plan to take.
2. Fork the repository and create a focused branch from `main`.
3. Use the demo camera to exercise the affected workflow without camera hardware.
4. Open a draft pull request early if you want feedback before the implementation is complete.
5. Mark the pull request ready after the checks above pass and the acceptance criteria are covered.

GitHub may require maintainer approval before workflows run for a first-time contributor. A maintainer will acknowledge a new contribution within two business days and review a ready pull request within three business days. If either window is missed, a single polite reminder is welcome.

## Pull requests

- Explain the problem and the change.
- Add or update tests for behavior changes.
- Update documentation when configuration, security assumptions, or workflows change.
- Do not commit camera credentials, captured frames, videos, Data Protection keys, or other private data.
- Preserve the local-only and private-network defaults unless a reviewed security design replaces them.
- Confirm that CI passes on Windows and Ubuntu.
- Link the issue with `Fixes #123` when the pull request fully resolves it.

By contributing, you agree that your contribution is licensed under the Apache License 2.0.
