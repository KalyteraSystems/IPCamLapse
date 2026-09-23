using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPCamLapse.Tests;

public sealed class RetentionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ipcamlapse-retention-{Guid.NewGuid():N}");
    private readonly TestPaths _paths;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ManualTimeProvider _time = new(Now);
    private readonly TestSettings _settings = new(retentionDays: 7);

    public RetentionTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new TestPaths(_root);
        _dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "keys")));
    }

    [Fact]
    public async Task PreviewAndExecutionSelectTheSameSessions()
    {
        var (storage, sessions) = CreateServices();
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 100);
        await AddSessionAsync(sessions, "bbbbbbbb", SessionStatus.Cancelled, Now.AddDays(-10), bytes: 50);
        await AddSessionAsync(sessions, "cccccccc", SessionStatus.Completed, Now.AddDays(-1), bytes: 70);

        var preview = await storage.PreviewRetentionAsync();
        var result = await storage.ApplyRetentionAsync();

        Assert.True(preview.Enabled);
        Assert.Equal(2, preview.EligibleSessions);
        Assert.Equal(2, result.DeletedSessions);
        Assert.Equal(preview.EstimatedBytes, result.DeletedBytes);
        Assert.Empty(result.FailedSessionIds);
        // The one inside the window survives.
        Assert.Equal("cccccccc", Assert.Single(await sessions.GetAllSessionsAsync()).Id);
    }

    [Theory]
    [InlineData(SessionStatus.Ready)]
    [InlineData(SessionStatus.Scheduled)]
    [InlineData(SessionStatus.Capturing)]
    [InlineData(SessionStatus.Paused)]
    [InlineData(SessionStatus.Rendering)]
    public async Task UnfinishedSessionsNeverAgeIntoEligibility(SessionStatus status)
    {
        var (storage, sessions) = CreateServices();
        await AddSessionAsync(sessions, "aaaaaaaa", status, Now.AddDays(-365), bytes: 10);

        Assert.Equal(0, (await storage.PreviewRetentionAsync()).EligibleSessions);
        Assert.Equal(0, (await storage.ApplyRetentionAsync()).DeletedSessions);
        Assert.Single(await sessions.GetAllSessionsAsync());
    }

    [Fact]
    public async Task ASessionExactlyAtTheCutoffBecomesEligibleOnlyAfterTheBoundary()
    {
        var (storage, sessions) = CreateServices();
        // Completed exactly `retentionDays` ago: at the cutoff, not past it.
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-7), bytes: 10);

        Assert.Equal(0, (await storage.PreviewRetentionAsync()).EligibleSessions);

        _time.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(1, (await storage.PreviewRetentionAsync()).EligibleSessions);
        Assert.Equal(1, (await storage.ApplyRetentionAsync()).DeletedSessions);
    }

    [Fact]
    public async Task ZeroRetentionReportsCleanupDisabledAndDeletesNothing()
    {
        _settings.Current.RetentionDays = 0;
        var (storage, sessions) = CreateServices();
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-365), bytes: 10);

        var preview = await storage.PreviewRetentionAsync();

        Assert.False(preview.Enabled);
        Assert.Equal(0, preview.EligibleSessions);
        Assert.Equal(0, (await storage.ApplyRetentionAsync()).DeletedSessions);
        Assert.Single(await sessions.GetAllSessionsAsync());
    }

    [Fact]
    public async Task EstimatedBytesCoverTheSessionDirectoryAndItsMetadataFile()
    {
        var (storage, sessions) = CreateServices();
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 120);

        var metadataBytes = new FileInfo(Path.Combine(_paths.SessionsPath, "aaaaaaaa.json")).Length;

        var preview = await storage.PreviewRetentionAsync();

        Assert.False(preview.EstimateIsPartial);
        Assert.Equal(120 + metadataBytes, preview.EstimatedBytes);
    }

    [Fact]
    public async Task AnUnreadableFileFlagsThePreviewRatherThanCountingAsZero()
    {
        var files = new FakeFileSystem { FailLengthFor = path => path.EndsWith("frame.jpg", StringComparison.Ordinal) };
        var (storage, sessions) = CreateServices(files);
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 500);

        var preview = await storage.PreviewRetentionAsync();

        var metadataBytes = new FileInfo(Path.Combine(_paths.SessionsPath, "aaaaaaaa.json")).Length;

        Assert.True(preview.EstimateIsPartial);
        // The unreadable frame is left out, so the total is a floor: what could be read, and
        // never a silent zero for the whole session.
        Assert.Equal(metadataBytes, preview.EstimatedBytes);
        Assert.True(preview.EstimatedBytes > 0);
    }

    [Fact]
    public async Task APartialRunReportsTheFailedSessionAndKeepsTheRestAccurate()
    {
        var files = new FakeFileSystem
        {
            FailDeleteDirectoryFor = path => path.EndsWith("bbbbbbbb", StringComparison.Ordinal)
        };
        var (storage, sessions) = CreateServices(files);
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 100);
        await AddSessionAsync(sessions, "bbbbbbbb", SessionStatus.Completed, Now.AddDays(-30), bytes: 4000);

        var result = await storage.ApplyRetentionAsync();

        // The run continues past the failure and does not claim the failed session's bytes.
        Assert.Equal(1, result.DeletedSessions);
        Assert.True(result.DeletedBytes < 4000);
        Assert.Equal("bbbbbbbb", Assert.Single(result.FailedSessionIds));
        Assert.Equal("bbbbbbbb", Assert.Single(await sessions.GetAllSessionsAsync()).Id);
        Assert.True(File.Exists(Path.Combine(_paths.SessionsPath, "bbbbbbbb.json")));
    }

    [Fact]
    public async Task AFailureAfterTheDirectoryIsGoneStillReportsFailureAndRetriesToCompletion()
    {
        // The directory goes, then deleting the metadata throws: the session is half removed.
        var files = new FakeFileSystem { FailDeleteFile = true };
        var (storage, sessions) = CreateServices(files);
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 100);

        var first = await storage.ApplyRetentionAsync();

        Assert.Equal(0, first.DeletedSessions);
        Assert.Equal("aaaaaaaa", Assert.Single(first.FailedSessionIds));
        Assert.False(Directory.Exists(Path.Combine(_paths.SessionsPath, "aaaaaaaa")));
        // The metadata is still there, which is what makes the retry possible.
        Assert.True(File.Exists(Path.Combine(_paths.SessionsPath, "aaaaaaaa.json")));
        Assert.Single(await sessions.GetAllSessionsAsync());

        files.FailDeleteFile = false;
        var second = await storage.ApplyRetentionAsync();

        Assert.Equal(1, second.DeletedSessions);
        Assert.Empty(second.FailedSessionIds);
        Assert.False(File.Exists(Path.Combine(_paths.SessionsPath, "aaaaaaaa.json")));
        Assert.Empty(await sessions.GetAllSessionsAsync());
    }

    [Fact]
    public async Task AFailedMetadataLookupStillRemovesTheMetadata()
    {
        // File.Exists returns false both when a file is absent and when the lookup fails.
        // Treating that as proof of absence would skip the delete and still count a deletion.
        var files = new FakeFileSystem
        {
            ReportMissingFor = path => path.EndsWith("aaaaaaaa.json", StringComparison.Ordinal)
        };
        var (storage, sessions) = CreateServices(files);
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 100);

        var result = await storage.ApplyRetentionAsync();

        Assert.Equal(1, result.DeletedSessions);
        Assert.Empty(result.FailedSessionIds);
        // The point of the test: a counted deletion means the file is genuinely gone.
        Assert.False(File.Exists(Path.Combine(_paths.SessionsPath, "aaaaaaaa.json")));
        Assert.Empty(await sessions.GetAllSessionsAsync());
    }

    [Fact]
    public async Task AFailedDirectoryLookupStillRemovesTheDirectory()
    {
        var files = new FakeFileSystem
        {
            ReportMissingFor = path => path.EndsWith("aaaaaaaa", StringComparison.Ordinal)
        };
        var (storage, sessions) = CreateServices(files);
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 100);

        var result = await storage.ApplyRetentionAsync();

        Assert.Equal(1, result.DeletedSessions);
        Assert.Empty(result.FailedSessionIds);
        Assert.False(Directory.Exists(Path.Combine(_paths.SessionsPath, "aaaaaaaa")));
        Assert.Empty(await sessions.GetAllSessionsAsync());
    }

    [Fact]
    public async Task AMetadataFileLeftBehindByASilentDeleteIsReportedAsAFailure()
    {
        // A delete that reports success without removing anything: the postcondition is the
        // only thing standing between that and a session counted as deleted while on disk.
        var files = new FakeFileSystem
        {
            SilentlySkipDeleteFor = path => path.EndsWith("aaaaaaaa.json", StringComparison.Ordinal)
        };
        var (storage, sessions) = CreateServices(files);
        await AddSessionAsync(sessions, "aaaaaaaa", SessionStatus.Completed, Now.AddDays(-30), bytes: 100);

        var result = await storage.ApplyRetentionAsync();

        Assert.Equal(0, result.DeletedSessions);
        Assert.Equal("aaaaaaaa", Assert.Single(result.FailedSessionIds));
        Assert.True(File.Exists(Path.Combine(_paths.SessionsPath, "aaaaaaaa.json")));
        // Retained in memory, so a later run retries it.
        Assert.Single(await sessions.GetAllSessionsAsync());
    }

    [Fact]
    public async Task DeletingAMissingSessionIsNotAFailure()
    {
        var (_, sessions) = CreateServices();

        var result = await sessions.DeleteSessionAsync("aaaaaaaa");

        Assert.False(result.Deleted);
        Assert.False(result.IsFailure);
    }

    private (IStorageService Storage, ICaptureSessionService Sessions) CreateServices(FakeFileSystem? files = null)
    {
        var fileSystem = files ?? new FakeFileSystem();
        var sessions = new CaptureSessionService(
            NullLogger<CaptureSessionService>.Instance,
            _paths,
            _dataProtectionProvider,
            fileSystem);
        var storage = new StorageService(
            _paths,
            _settings,
            sessions,
            fileSystem,
            _time,
            NullLogger<StorageService>.Instance);
        return (storage, sessions);
    }

    private async Task AddSessionAsync(
        ICaptureSessionService sessions,
        string id,
        SessionStatus status,
        DateTimeOffset completedAt,
        int bytes)
    {
        await sessions.CreateSessionAsync(new CaptureSession
        {
            Id = id,
            Name = id,
            Status = status,
            CreatedAt = completedAt.UtcDateTime,
            CompletedAt = completedAt.UtcDateTime,
            Configuration = new CaptureConfiguration { CameraUrl = "http://camera/snapshot.jpg" }
        });

        var imagesPath = Path.Combine(_paths.SessionsPath, id, "images");
        Directory.CreateDirectory(imagesPath);
        await File.WriteAllBytesAsync(Path.Combine(imagesPath, "frame.jpg"), new byte[bytes]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Real file operations by default, with failures injected per path, so the tests do not
    /// depend on OS file locking or permissions (which behave differently across platforms, and
    /// not at all when CI runs elevated).
    /// </summary>
    private sealed class FakeFileSystem : ISessionFileSystem
    {
        private readonly SessionFileSystem _real = new();

        public Func<string, bool>? FailDeleteDirectoryFor { get; init; }
        public Func<string, bool>? FailLengthFor { get; init; }
        public bool FailDeleteFile { get; set; }

        /// <summary>
        /// Reports a path as absent although it exists, which is what Directory.Exists and
        /// File.Exists do when the lookup itself fails rather than when the path is missing.
        /// </summary>
        public Func<string, bool>? ReportMissingFor { get; init; }

        /// <summary>Accepts a delete and does nothing, leaving the path behind.</summary>
        public Func<string, bool>? SilentlySkipDeleteFor { get; init; }

        public bool DirectoryExists(string path)
            => ReportMissingFor?.Invoke(path.TrimEnd(Path.DirectorySeparatorChar)) != true
                && _real.DirectoryExists(path);

        public bool FileExists(string path)
            => ReportMissingFor?.Invoke(path) != true && _real.FileExists(path);

        public void DeleteDirectory(string path)
        {
            if (FailDeleteDirectoryFor?.Invoke(path.TrimEnd(Path.DirectorySeparatorChar)) == true)
                throw new IOException("Injected directory failure.");
            if (SilentlySkipDeleteFor?.Invoke(path.TrimEnd(Path.DirectorySeparatorChar)) == true)
                return;
            _real.DeleteDirectory(path);
        }

        public void DeleteFile(string path)
        {
            if (FailDeleteFile)
                throw new IOException("Injected file failure.");
            if (SilentlySkipDeleteFor?.Invoke(path) == true)
                return;
            _real.DeleteFile(path);
        }

        public IEnumerable<string> EnumerateFiles(string path) => _real.EnumerateFiles(path);

        public long GetFileLength(string path)
        {
            if (FailLengthFor?.Invoke(path) == true)
                throw new IOException("Injected length failure.");
            return _real.GetFileLength(path);
        }
    }

    private sealed class TestPaths : IDataPathProvider
    {
        public TestPaths(string root)
        {
            RootPath = Path.Combine(root, "data");
            SessionsPath = Path.Combine(RootPath, "sessions");
            ProfilesPath = Path.Combine(RootPath, "camera-profiles.json");
            SettingsPath = Path.Combine(RootPath, "settings.json");
            Directory.CreateDirectory(SessionsPath);
        }

        public string RootPath { get; }
        public string SessionsPath { get; }
        public string ProfilesPath { get; }
        public string SettingsPath { get; }
    }

    private sealed class TestSettings : IApplicationSettingsService
    {
        public TestSettings(int retentionDays)
        {
            Current = new ApplicationSettings { RetentionDays = retentionDays };
        }

        public ApplicationSettings Current { get; }

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
