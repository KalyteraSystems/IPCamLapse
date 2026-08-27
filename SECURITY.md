# Security Policy

## Reporting a vulnerability

Do not disclose suspected vulnerabilities in public issues.

Use **Report a vulnerability** on the Security tab. Include the affected version or commit, reproduction steps, impact, and suggested mitigation. Maintainers will coordinate disclosure after a fix is available.

## Supported versions

IPCamLapse is currently an early preview. Security fixes are applied to the latest commit on the default branch; older commits and unofficial builds are not supported.

## Deployment assumptions

The supported configuration is a trusted local machine serving the UI over loopback and fetching cameras on a trusted private network. The app does not yet provide user authentication.

Binding IPCamLapse directly to a LAN or the public internet is unsupported. If remote access is necessary, use an authenticated TLS reverse proxy that reaches IPCamLapse over loopback and review all forwarded-header and access-control settings. Enabling hostname or public-address camera targets broadens the server-side request forgery exposure and should be treated as an advanced, high-trust configuration.

Camera passwords are protected at rest with ASP.NET Core Data Protection, not end-to-end encrypted. Anyone who controls the host or application process may be able to access credentials and captured media.
