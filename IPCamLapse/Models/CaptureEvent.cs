namespace IPCamLapse.Models;

public enum CaptureEventKind
{
    Frame,
    Failure,
    State
}

public sealed class CaptureEvent
{
    public DateTime At { get; set; } = DateTime.UtcNow;
    public CaptureEventKind Kind { get; set; }
    public int? FrameNumber { get; set; }
    public string? FileName { get; set; }
    public string? Message { get; set; }
    public int? Attempt { get; set; }
}

public sealed record SequencedCaptureEvent(long Sequence, CaptureEvent Event);

public sealed record FrameInfo(
    int Number,
    string FileName,
    DateTime CapturedAt,
    long SizeBytes,
    string PreviewUrl,
    string DownloadUrl);

public sealed record FramePage(
    IReadOnlyList<FrameInfo> Items,
    int Offset,
    int Limit,
    int Total);
