using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IPCamLapse.Models;

namespace IPCamLapse.Services;

public interface IFrameCatalogService
{
    Task AppendEventAsync(string sessionId, CaptureEvent captureEvent, CancellationToken cancellationToken = default);
    Task<FramePage> GetFramesAsync(string sessionId, int offset, int limit);
    Task<IReadOnlyList<CaptureEvent>> GetEventsAsync(string sessionId, int limit);
    Task<IReadOnlyList<string>> GetImagePathsAsync(string sessionId, int? startFrame = null, int? endFrame = null);
    Task<string?> ResolveFramePathAsync(string sessionId, string fileName);
}

public sealed class FrameCatalogService : IFrameCatalogService
{
    private static readonly Regex FramePattern = new(
        "^frame_(?<number>[0-9]+)_(?<timestamp>[0-9]{8}_[0-9]{6}(?:_[0-9]{3})?)\\.(?:jpg|png)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ICaptureSessionService _sessions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventLocks = new();

    public FrameCatalogService(ICaptureSessionService sessions)
    {
        _sessions = sessions;
    }

    public async Task AppendEventAsync(
        string sessionId,
        CaptureEvent captureEvent,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return;
        var gate = _eventLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var line = JsonSerializer.Serialize(captureEvent) + Environment.NewLine;
            await File.AppendAllTextAsync(
                Path.Combine(session.StoragePath, "events.jsonl"),
                line,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FramePage> GetFramesAsync(string sessionId, int offset, int limit)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        var frames = await ReadFramesAsync(sessionId);
        var items = frames
            .OrderByDescending(frame => frame.Number)
            .Skip(offset)
            .Take(limit)
            .ToList();
        return new FramePage(items, offset, limit, frames.Count);
    }

    public async Task<IReadOnlyList<CaptureEvent>> GetEventsAsync(string sessionId, int limit)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return Array.Empty<CaptureEvent>();
        var path = Path.Combine(session.StoragePath, "events.jsonl");
        if (!File.Exists(path))
            return Array.Empty<CaptureEvent>();

        var events = new List<CaptureEvent>();
        foreach (var line in (await File.ReadAllLinesAsync(path)).Reverse())
        {
            if (events.Count >= Math.Clamp(limit, 1, 500))
                break;
            try
            {
                var captureEvent = JsonSerializer.Deserialize<CaptureEvent>(line);
                if (captureEvent is not null)
                    events.Add(captureEvent);
            }
            catch (JsonException)
            {
            }
        }
        return events;
    }

    public async Task<IReadOnlyList<string>> GetImagePathsAsync(
        string sessionId,
        int? startFrame = null,
        int? endFrame = null)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return Array.Empty<string>();
        var frames = await ReadFramesAsync(sessionId);
        return frames
            .Where(frame => !startFrame.HasValue || frame.Number >= startFrame.Value)
            .Where(frame => !endFrame.HasValue || frame.Number <= endFrame.Value)
            .OrderBy(frame => frame.Number)
            .Select(frame => Path.Combine(session.StoragePath, "images", frame.FileName))
            .ToList();
    }

    public async Task<string?> ResolveFramePathAsync(string sessionId, string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            !FramePattern.IsMatch(fileName))
        {
            return null;
        }
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return null;
        var path = Path.Combine(session.StoragePath, "images", fileName);
        return File.Exists(path) ? path : null;
    }

    private async Task<List<FrameInfo>> ReadFramesAsync(string sessionId)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return new List<FrameInfo>();
        var imagesPath = Path.Combine(session.StoragePath, "images");
        if (!Directory.Exists(imagesPath))
            return new List<FrameInfo>();

        var frames = new List<FrameInfo>();
        foreach (var path in Directory.EnumerateFiles(imagesPath, "frame_*.*"))
        {
            var fileName = Path.GetFileName(path);
            var match = FramePattern.Match(fileName);
            if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var number))
                continue;
            var timestampText = match.Groups["timestamp"].Value;
            var formats = new[] { "yyyyMMdd_HHmmss_fff", "yyyyMMdd_HHmmss" };
            var capturedAt = DateTime.TryParseExact(
                timestampText,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : File.GetCreationTimeUtc(path);
            frames.Add(new FrameInfo(
                number,
                fileName,
                capturedAt,
                new FileInfo(path).Length,
                $"/api/sessions/{sessionId}/frames/{Uri.EscapeDataString(fileName)}",
                $"/api/sessions/{sessionId}/frames/{Uri.EscapeDataString(fileName)}?download=true"));
        }
        return frames;
    }
}
