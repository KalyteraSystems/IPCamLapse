namespace IPCamLapse.Models;

public sealed class CameraConnectionRequest
{
    public string? ProfileId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool AllowInvalidCertificate { get; init; }
}
