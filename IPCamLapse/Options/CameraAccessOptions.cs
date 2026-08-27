namespace IPCamLapse.Options;

public sealed class CameraAccessOptions
{
    public const string SectionName = "CameraAccess";

    public bool AllowHostnames { get; init; }
    public bool AllowPublicAddresses { get; init; }
    public long MaxSnapshotBytes { get; init; } = 20 * 1024 * 1024;
}
