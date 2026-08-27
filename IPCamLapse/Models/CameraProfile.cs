using System.ComponentModel.DataAnnotations;

namespace IPCamLapse.Models;

public sealed class CameraProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2048)]
    public string Url { get; set; } = string.Empty;

    [StringLength(128)]
    public string? Username { get; set; }

    [StringLength(512)]
    public string? Password { get; set; }

    public bool AllowInvalidCertificate { get; set; }
    public bool IsDemo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record CameraEndpoint(
    string Name,
    string Url,
    string? Username,
    string? Password,
    bool AllowInvalidCertificate,
    bool IsDemo);
