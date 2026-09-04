using CloudNative.CloudEvents;
using IPCamLapse.Models;
using OpenCamInterop;

namespace IPCamLapse.Services;

public interface IInteropCaptureEventMapper
{
    CloudEvent Map(string sessionId, SequencedCaptureEvent record);
}

public static class IPCamLapseInteropEventTypes
{
    public const string FrameCaptured = "com.kalyterasystems.ipcamlapse.frame.captured.v1";
    public const string CaptureFailed = "com.kalyterasystems.ipcamlapse.capture.failed.v1";
    public const string SessionStateChanged = "com.kalyterasystems.ipcamlapse.session.state.changed.v1";
}

public sealed record IPCamLapseInteropEventData(
    string Adapter,
    string SessionId,
    long Sequence,
    string Kind,
    int? FrameNumber,
    string? FileName,
    int? Attempt,
    string? State,
    string? FailureCode);

public sealed class InteropCaptureEventMapper : IInteropCaptureEventMapper
{
    private static readonly Uri DataSchema = new(
        "urn:opencaminterop:schema:ipcamlapse-capture-event:1",
        UriKind.Absolute);

    public CloudEvent Map(string sessionId, SequencedCaptureEvent record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(record);

        var captureEvent = record.Event;
        var eventType = captureEvent.Kind switch
        {
            CaptureEventKind.Frame => IPCamLapseInteropEventTypes.FrameCaptured,
            CaptureEventKind.Failure => IPCamLapseInteropEventTypes.CaptureFailed,
            CaptureEventKind.State => IPCamLapseInteropEventTypes.SessionStateChanged,
            _ => throw new ArgumentOutOfRangeException(nameof(record), "Unknown capture event kind.")
        };

        var data = new IPCamLapseInteropEventData(
            "ipcamlapse.activity-log.v1",
            sessionId,
            record.Sequence,
            captureEvent.Kind.ToString().ToLowerInvariant(),
            captureEvent.FrameNumber,
            SanitizeFileName(captureEvent.FileName),
            captureEvent.Attempt,
            captureEvent.Kind == CaptureEventKind.State
                ? ClassifyState(captureEvent.Message)
                : null,
            captureEvent.Kind == CaptureEventKind.Failure
                ? ClassifyFailure(captureEvent.Message)
                : null);

        var cloudEvent = new CloudEvent(CloudEventsSpecVersion.V1_0)
        {
            Id = $"{sessionId}:{record.Sequence}",
            Source = new Uri($"urn:ipcamlapse:session:{sessionId}", UriKind.Absolute),
            Type = eventType,
            Subject = captureEvent.Kind == CaptureEventKind.Frame && data.FileName is not null
                ? $"frames/{data.FileName}"
                : $"sessions/{sessionId}",
            Time = NormalizeTime(captureEvent.At),
            DataContentType = "application/json",
            DataSchema = DataSchema,
            Data = data
        };

        InteropCloudEventValidator.EnsureValid(cloudEvent);
        return cloudEvent;
    }

    private static DateTimeOffset NormalizeTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value),
            DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
        };
    }

    private static string? SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var lastSeparator = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        var fileName = value[(lastSeparator + 1)..];
        var isSafe = fileName.Length is > 0 and <= 255 &&
            fileName is not ("." or "..") &&
            fileName.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
        return isSafe ? fileName : null;
    }

    private static string ClassifyState(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "state-changed";
        if (message.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
            return "failed";

        return message.Trim().ToLowerInvariant() switch
        {
            "ready" => "ready",
            "capturing" => "capturing",
            "paused" => "paused",
            "completed" => "completed",
            "cancelled" => "cancelled",
            "scheduled" => "scheduled",
            "rendering" => "rendering",
            _ => "state-changed"
        };
    }

    private static string ClassifyFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "unknown";
        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "timeout";
        if (message.Contains("returned HTTP", StringComparison.OrdinalIgnoreCase))
            return "http-error";
        if (message.Contains("size limit", StringComparison.OrdinalIgnoreCase))
            return "size-limit";
        if (message.Contains("content type", StringComparison.OrdinalIgnoreCase))
            return "unsupported-content-type";
        if (message.Contains("JPEG image", StringComparison.OrdinalIgnoreCase))
            return "invalid-image";
        if (message.Contains("connection failed", StringComparison.OrdinalIgnoreCase))
            return "connection-failed";
        if (message.Contains("URL rejected", StringComparison.OrdinalIgnoreCase))
            return "url-rejected";
        if (message.Contains("storage", StringComparison.OrdinalIgnoreCase))
            return "storage-unavailable";

        return "unknown";
    }
}
