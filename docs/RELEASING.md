# Releasing

## Prepare

1. Update `<Version>` in `IPCamLapse/IPCamLapse.csproj` and the release-facing documentation, including moving the `Unreleased` changelog entries under the new version. Release builds take their assembly and file version from this property, not from the Git tag.
2. Run the local verification suite:

   ```console
   dotnet restore --locked-mode
   dotnet build --configuration Release --no-restore
   dotnet test --configuration Release --no-build
   dotnet format --verify-no-changes --no-restore
   dotnet list package --vulnerable --include-transitive
   docker build --tag ipcamlapse:release-check .
   ```

3. Merge the release change only after Windows and Ubuntu CI pass.

## Publish

Create and push an annotated `v<version>` tag on the release commit. The release workflow builds self-contained Windows x64, Linux x64, and Linux ARM64 archives, creates a draft GitHub release with generated notes, uploads the archives, and then publishes the release. The container workflow publishes Linux AMD64 and ARM64 images tagged `v<version>`, `<version>`, and `<major>.<minor>` to GitHub Container Registry. The `latest` image tag follows `main`, not the most recent release.

## Verify

- Download and extract the Windows x64, Linux x64, and Linux ARM64 archives.
- Start the app and complete a demo capture.
- Confirm the System page reports writable storage and FFmpeg availability where installed.
- Pull the tagged container image, confirm Docker reports it healthy (the image probes `/healthz`), and check `/api/system/health` through a loopback-only port.
- Compare release checksums before preparing downstream package manifests.

## WinGet

The package identifier is `KalyteraSystems.IPCamLapse` (a portable package with a `Gyan.FFmpeg.Essentials` dependency) in `microsoft/winget-pkgs`; 0.4.4 is the first published version. WinGet manifests must use immutable GitHub release URLs and the SHA-256 hash of the released Windows archive. Submit a new version manifest only after the release is public and the archive passes the verification steps above.
