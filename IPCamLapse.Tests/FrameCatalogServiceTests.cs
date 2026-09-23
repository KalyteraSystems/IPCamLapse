using System.Text;
using System.Text.Json;
using IPCamLapse.Models;
using IPCamLapse.Services;

namespace IPCamLapse.Tests;

public sealed class FrameCatalogServiceTests : IDisposable
{
    private const string SessionId = "a1b2c3d4";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"ipcamlapse-frame-catalog-{Guid.NewGuid():N}");
    private readonly string _eventsPath;
    private readonly FrameCatalogService _service;

    public FrameCatalogServiceTests()
    {
        Directory.CreateDirectory(_root);
        _eventsPath = Path.Combine(_root, "events.jsonl");
        _service = new FrameCatalogService(new TestSessions(_root));
    }

    [Fact]
    public async Task NewestUsesPhysicalOrderAndMalformedTailDoesNotConsumeLimit()
    {
        var older = Event("older on disk", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Event("newer on disk", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await File.WriteAllTextAsync(
            _eventsPath,
            JsonSerializer.Serialize(older) + "\n" +
            JsonSerializer.Serialize(newer) + "\n" +
            "{truncated");

        var events = await _service.GetEventsAsync(SessionId, 1);

        Assert.Single(events);
        Assert.Equal("newer on disk", events[0].Message);
    }

    [Fact]
    public async Task IncludesValidFinalRecordWithoutTrailingNewline()
    {
        await File.WriteAllTextAsync(_eventsPath, JsonSerializer.Serialize(Event("final", DateTime.UtcNow)));

        var events = await _service.GetEventsAsync(SessionId, 10);

        Assert.Single(events);
        Assert.Equal("final", events[0].Message);
    }

    [Fact]
    public async Task HandlesCrLfAndUtf8CodePointAcrossReadBufferBoundary()
    {
        var line = BuildLineWithSplitUtf8CodePoint();
        await File.WriteAllBytesAsync(_eventsPath, Encoding.UTF8.GetBytes(line + "\r\n"));

        var events = await _service.GetEventsAsync(SessionId, 10);

        Assert.Single(events);
        Assert.Contains("Ж", events[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitsLargeFileToNewestOneOrFiveHundredValidRecords()
    {
        await using (var stream = new FileStream(_eventsPath, FileMode.CreateNew, FileAccess.Write))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            for (var number = 1; number <= 2_000; number++)
                await writer.WriteLineAsync(JsonSerializer.Serialize(Event($"event-{number}", DateTime.UtcNow)));
        }

        var one = await _service.GetEventsAsync(SessionId, 1);
        var fiveHundred = await _service.GetEventsAsync(SessionId, 500);

        Assert.Single(one);
        Assert.Equal("event-2000", one[0].Message);
        Assert.Equal(500, fiveHundred.Count);
        Assert.Equal("event-2000", fiveHundred[0].Message);
        Assert.Equal("event-1501", fiveHundred[^1].Message);
    }

    [Fact]
    public async Task AppendMaintainsIndexAndNextTailReadDoesNotScanHistory()
    {
        await using (var stream = new FileStream(_eventsPath, FileMode.CreateNew, FileAccess.Write))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            for (var number = 1; number <= 2_000; number++)
                await writer.WriteLineAsync(JsonSerializer.Serialize(Event($"event-{number}", DateTime.UtcNow)));
        }

        await _service.GetEventRecordsAsync(SessionId, 1);
        await _service.AppendEventAsync(SessionId, Event("event-2001", DateTime.UtcNow));
        var records = await _service.GetEventRecordsAsync(SessionId, 1);

        Assert.Single(records);
        Assert.Equal(2_001, records[0].Sequence);
        Assert.Equal("event-2001", records[0].Event.Message);
        Assert.True(_service.LastEventLogBytesRead <= 4_096);
    }

    [Fact]
    public async Task HonorsRequestCancellationInTailReader()
    {
        await File.WriteAllTextAsync(_eventsPath, JsonSerializer.Serialize(Event("event", DateTime.UtcNow)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.GetEventsAsync(SessionId, 10, cancellation.Token));
    }

    private static CaptureEvent Event(string message, DateTime at) => new()
    {
        At = at,
        Kind = CaptureEventKind.State,
        Message = message
    };

    private static string BuildLineWithSplitUtf8CodePoint()
    {
        for (var suffixLength = 0; suffixLength < 5_000; suffixLength++)
        {
            var line = JsonSerializer.Serialize(
                Event("prefix-Ж" + new string('x', suffixLength), DateTime.UtcNow),
                new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            if (bytes.Length <= 4_096)
                continue;
            var boundary = bytes.Length - 4_096;
            var marker = Encoding.UTF8.GetBytes("Ж");
            var markerIndex = bytes.AsSpan().IndexOf(marker);
            if (boundary > markerIndex && boundary < markerIndex + marker.Length)
                return line;
        }

        throw new InvalidOperationException("Could not construct split UTF-8 fixture.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestSessions : ICaptureSessionService
    {
        private readonly CaptureSession _session;

        public TestSessions(string storagePath) => _session = new CaptureSession
        {
            Id = SessionId,
            Name = "Test session",
            StoragePath = storagePath
        };

        public Task<CaptureSession?> GetSessionAsync(string id) =>
            Task.FromResult<CaptureSession?>(id == SessionId ? _session : null);

        public Task<CaptureSession> CreateSessionAsync(CaptureSession session) => Task.FromResult(session);
        public Task<List<CaptureSession>> GetAllSessionsAsync() => Task.FromResult(new List<CaptureSession> { _session });
        public Task UpdateSessionAsync(CaptureSession session) => Task.CompletedTask;
        public Task DeleteSessionAsync(string id) => Task.CompletedTask;
        public Task<string> GetSessionStoragePathAsync(string id) => Task.FromResult(_root());
        public Task<string[]> GetSessionImagesAsync(string id) => Task.FromResult(Array.Empty<string>());
        public Task<string?> GetLatestImageAsync(string id) => Task.FromResult<string?>(null);

        private string _root() => _session.StoragePath!;
    }
}
