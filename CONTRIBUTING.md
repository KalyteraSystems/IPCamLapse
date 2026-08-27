# Contributing to IPCamLapse

Thank you for helping make IPCamLapse more useful and safer.

## Before you start

- Search existing issues before opening a new one.
- Use a discussion or issue for substantial design changes before investing in an implementation.
- Report security vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
- Keep pull requests focused. Separate unrelated refactors from behavior changes.

## Local setup

Install the .NET 10 SDK. FFmpeg is required for manual video-generation testing but not for the current unit suite.

```console
git clone https://github.com/KalyteraSystems/IPCamLapse.git
cd IPCamLapse
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

Before opening a pull request, run:

```console
dotnet format --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```

## Pull requests

- Explain the user-visible problem and the approach taken.
- Add or update tests for behavior changes.
- Update documentation when configuration, security assumptions, or workflows change.
- Do not commit camera credentials, captured frames, videos, Data Protection keys, or other private data.
- Preserve the local-only and private-network defaults unless a reviewed security design replaces them.
- Confirm that CI passes on Windows and Ubuntu.

By contributing, you agree that your contribution is licensed under the Apache License 2.0.
