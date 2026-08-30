# Releasing

## Prepare

1. Update the project version and release-facing documentation.
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

Create and push an annotated `v<version>` tag on the release commit. The release workflow builds self-contained Windows x64, Linux x64, and Linux ARM64 archives and creates the GitHub release. The container workflow publishes versioned Linux AMD64 and ARM64 images to GitHub Container Registry.

## Verify

- Download and extract the Windows x64, Linux x64, and Linux ARM64 archives.
- Start the app and complete a demo capture.
- Confirm the System page reports writable storage and FFmpeg availability where installed.
- Pull the tagged container image and check `/api/system/health` through a loopback-only port.
- Compare release checksums before preparing downstream package manifests.

## WinGet

WinGet manifests must use immutable GitHub release URLs and the SHA-256 hash of the released Windows archive. Submit the manifest only after the release is public and the archive passes the verification steps above.
