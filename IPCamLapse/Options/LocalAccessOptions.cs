namespace IPCamLapse.Options;

public sealed class LocalAccessOptions
{
    public const string SectionName = "LocalAccess";

    public bool AllowPrivateNetworks { get; init; }
}
