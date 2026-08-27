using System.Globalization;
using FFMpegCore;
using IPCamLapse.Models;

namespace IPCamLapse.Services;

public sealed record VideoRenderRequest(
    IReadOnlyList<string> ImagePaths,
    string OutputPath,
    double TargetDurationSeconds,
    VideoSettings Settings);

public sealed record VideoRenderResult(bool Success, string? Path, string? Error)
{
    public static VideoRenderResult Failed(string error) => new(false, null, error);
}

public interface IVideoService
{
    Task<VideoRenderResult> CreateTimeLapseAsync(
        VideoRenderRequest request,
        CancellationToken cancellationToken = default);
    Task<bool> IsFfmpegAvailableAsync();
}

public sealed class VideoService : IVideoService
{
    private readonly ILogger<VideoService> _logger;

    public VideoService(ILogger<VideoService> logger)
    {
        _logger = logger;
    }

    public Task<bool> IsFfmpegAvailableAsync()
    {
        return Task.FromResult(FindFfmpegBinaryFolder() is not null);
    }

    public async Task<VideoRenderResult> CreateTimeLapseAsync(
        VideoRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ImagePaths.Count < 2)
            return VideoRenderResult.Failed("At least two frames are required.");
        if (request.TargetDurationSeconds <= 0)
            return VideoRenderResult.Failed("Video length must be positive.");
        var settingsError = ValidateSettings(request.Settings);
        if (settingsError is not null)
            return VideoRenderResult.Failed(settingsError);
        var binaryFolder = FindFfmpegBinaryFolder();
        if (binaryFolder is null)
            return VideoRenderResult.Failed("FFmpeg was not found.");

        var fileListPath = Path.Combine(Path.GetTempPath(), $"ipcam-{Guid.NewGuid():N}.txt");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frameRate = request.Settings.FrameRate > 0
                ? request.Settings.FrameRate
                : Math.Clamp((request.ImagePaths.Count + 1) / request.TargetDurationSeconds, 1, 60);
            var frameDuration = 1d / frameRate;
            var lines = new List<string>(request.ImagePaths.Count * 2 + 1);
            foreach (var imagePath in request.ImagePaths)
            {
                lines.Add($"file '{EscapeConcatPath(imagePath)}'");
                lines.Add($"duration {frameDuration.ToString("F6", CultureInfo.InvariantCulture)}");
            }
            lines.Add($"file '{EscapeConcatPath(request.ImagePaths[^1])}'");
            await File.WriteAllLinesAsync(fileListPath, lines, cancellationToken);

            var outputDirectory = Path.GetDirectoryName(request.OutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return VideoRenderResult.Failed("Output path is invalid.");
            Directory.CreateDirectory(outputDirectory);
            var filter = BuildVideoFilter(request.Settings);
            _logger.LogInformation(
                "Rendering {FrameCount} frames to {OutputPath}",
                request.ImagePaths.Count,
                request.OutputPath);

            var success = await FFMpegArguments
                .FromFileInput(fileListPath, false, options => options
                    .WithCustomArgument("-f concat -safe 0"))
                .OutputToFile(request.OutputPath, true, options => options
                    .WithVideoCodec("libx264")
                    .WithFramerate(frameRate)
                    .WithConstantRateFactor(request.Settings.QualityCrf)
                    .WithCustomArgument($"-vf \"{filter}\"")
                    .WithCustomArgument("-pix_fmt yuv420p")
                    .WithCustomArgument("-movflags +faststart")
                    .WithCustomArgument("-an"))
                .ProcessAsynchronously(true, new FFOptions { BinaryFolder = binaryFolder });

            cancellationToken.ThrowIfCancellationRequested();
            return success && File.Exists(request.OutputPath)
                ? new VideoRenderResult(true, request.OutputPath, null)
                : VideoRenderResult.Failed("FFmpeg did not produce a video.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Video rendering failed");
            return VideoRenderResult.Failed("Video rendering failed. Check the system page and logs.");
        }
        finally
        {
            if (File.Exists(fileListPath))
                File.Delete(fileListPath);
        }
    }

    private static string BuildVideoFilter(VideoSettings settings)
    {
        var width = settings.Width - settings.Width % 2;
        var height = settings.Height - settings.Height % 2;
        var resize = settings.FitMode switch
        {
            VideoFitMode.Fill =>
                $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}",
            VideoFitMode.Stretch => $"scale={width}:{height}",
            _ =>
                $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2"
        };
        return settings.TimestampOverlay
            ? resize + ",drawtext=text='%{pts\\:hms}':x=20:y=h-th-20:fontcolor=white:fontsize=24:box=1:boxcolor=black@0.55"
            : resize;
    }

    private static string? ValidateSettings(VideoSettings settings)
    {
        if (settings.Width is < 320 or > 3840 || settings.Height is < 240 or > 2160 ||
            settings.Width % 2 != 0 || settings.Height % 2 != 0)
        {
            return "Video dimensions must be even and within the supported range.";
        }
        if (settings.FrameRate is < 0 or > 60)
            return "Video frame rate must be between 0 and 60.";
        if (settings.QualityCrf is < 18 or > 35)
            return "Video quality must be between 18 and 35.";
        if (!Enum.IsDefined(settings.FitMode))
            return "Video fit mode is invalid.";
        return null;
    }

    private static string EscapeConcatPath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/').Replace("'", "\\'");
    }

    private static string? FindFfmpegBinaryFolder()
    {
        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var appDirectory = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(appDirectory, executable)))
            return appDirectory;
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'));
        foreach (var directory in pathDirectories)
        {
            if (File.Exists(Path.Combine(directory, executable)))
                return directory;
        }
        foreach (var directory in new[] { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin" })
        {
            if (File.Exists(Path.Combine(directory, executable)))
                return directory;
        }
        return null;
    }
}
