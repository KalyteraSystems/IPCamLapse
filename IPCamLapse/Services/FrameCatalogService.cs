using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IPCamLapse.Models;

namespace IPCamLapse.Services;

public interface IFrameCatalogService
{
    Task AppendEventAsync(string sessionId, CaptureEvent captureEvent, CancellationToken cancellationToken = default);
    Task<FramePage> GetFramesAsync(string sessionId, int offset, int limit);
    Task<IReadOnlyList<CaptureEvent>> GetEventsAsync(
        string sessionId,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SequencedCaptureEvent>> GetEventRecordsAsync(
        string sessionId,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetImagePathsAsync(string sessionId, int? startFrame = null, int? endFrame = null);
    Task<string?> ResolveFramePathAsync(string sessionId, string fileName);
}

public sealed class FrameCatalogService : IFrameCatalogService
{
	internal long LastEventLogBytesRead { get; private set; }
    private static readonly Regex FramePattern = new(
        "^frame_(?<number>[0-9]+)_(?<timestamp>[0-9]{8}_[0-9]{6}(?:_[0-9]{3})?)\\.(?:jpg|png)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ICaptureSessionService _sessions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventLocks = new();
    private readonly ConcurrentDictionary<string, EventLogIndex> _eventLogIndexes = new();

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
			var path = Path.Combine(session.StoragePath, "events.jsonl");
			var previousLength = File.Exists(path) ? new FileInfo(path).Length : 0;
            var line = JsonSerializer.Serialize(captureEvent) + Environment.NewLine;
            await File.AppendAllTextAsync(
				path,
                line,
                cancellationToken);
			var newLength = new FileInfo(path).Length;
			if (_eventLogIndexes.TryGetValue(path, out var index) && index.Length == previousLength)
				_eventLogIndexes[path] = new EventLogIndex(newLength, index.NonBlankLineCount + 1);
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

    public async Task<IReadOnlyList<CaptureEvent>> GetEventsAsync(
        string sessionId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = await GetEventRecordsAsync(sessionId, limit, cancellationToken);
        return records
            .Reverse()
            .Select(record => record.Event)
            .ToList();
    }

    public async Task<IReadOnlyList<SequencedCaptureEvent>> GetEventRecordsAsync(
        string sessionId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId);
        if (session?.StoragePath is null)
            return Array.Empty<SequencedCaptureEvent>();
        var path = Path.Combine(session.StoragePath, "events.jsonl");
        if (!File.Exists(path))
            return Array.Empty<SequencedCaptureEvent>();

        var take = Math.Clamp(limit, 1, 500);
        const int bufferSize = 4 * 1024;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var snapshotLength = stream.Length;
        var hasIndex = _eventLogIndexes.TryGetValue(path, out var cachedIndex) &&
            cachedIndex.Length == snapshotLength;
        var offset = snapshotLength;
        var buffer = new byte[bufferSize];
        var reversedLine = new List<byte>();
        var selected = new List<(long FromEnd, CaptureEvent Event)>(take);
        long nonBlankFromEnd = 0;
		LastEventLogBytesRead = 0;

        while (offset > 0 && (!hasIndex || selected.Count < take))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readSize = (int)Math.Min(buffer.Length, offset);
            offset -= readSize;
            stream.Position = offset;
            await stream.ReadExactlyAsync(buffer.AsMemory(0, readSize), cancellationToken);
			LastEventLogBytesRead += readSize;
            for (var index = readSize - 1; index >= 0; index--)
            {
                if (buffer[index] == (byte)'\n')
                {
                    ProcessCandidate(reversedLine);
                    reversedLine.Clear();
                }
                else
                {
                    reversedLine.Add(buffer[index]);
                }
            }
        }
        if (!hasIndex || selected.Count < take)
            ProcessCandidate(reversedLine);

        var totalNonBlank = hasIndex ? cachedIndex!.NonBlankLineCount : nonBlankFromEnd;
        if (!hasIndex)
            _eventLogIndexes[path] = new EventLogIndex(snapshotLength, totalNonBlank);

        return selected
            .AsEnumerable()
            .Reverse()
            .Select(item => new SequencedCaptureEvent(
                totalNonBlank - item.FromEnd + 1,
                item.Event))
            .ToList();

        void ProcessCandidate(List<byte> reversedBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reversedBytes.Count == 0)
                return;

            var bytes = reversedBytes.ToArray();
            Array.Reverse(bytes);
            var length = bytes.Length;
            if (length > 0 && bytes[length - 1] == (byte)'\r')
                length--;
            var line = Encoding.UTF8.GetString(bytes, 0, length);
            if (string.IsNullOrWhiteSpace(line))
                return;

            nonBlankFromEnd++;
            if (selected.Count == take)
                return;

            try
            {
                var captureEvent = JsonSerializer.Deserialize<CaptureEvent>(line);
                if (captureEvent is not null)
                    selected.Add((nonBlankFromEnd, captureEvent));
            }
            catch (JsonException)
            {
            }
        }
    }

    private sealed record EventLogIndex(long Length, long NonBlankLineCount);

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
