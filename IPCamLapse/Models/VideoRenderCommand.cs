namespace IPCamLapse.Models;

public sealed class VideoRenderCommand
{
    public int? StartFrame { get; init; }
    public int? EndFrame { get; init; }
    public VideoSettings? Settings { get; init; }
}
