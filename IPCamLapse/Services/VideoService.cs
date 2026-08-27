using FFMpegCore;
using FFMpegCore.Enums;

namespace IPCamLapse.Services;

public interface IVideoService
{
    Task<string?> CreateTimeLapseAsync(string imagesFolder, string outputPath, double targetDurationSeconds, CancellationToken cancellationToken = default);
    Task<bool> IsFfmpegAvailableAsync();
}

public class VideoService : IVideoService
{
    private readonly ILogger<VideoService> _logger;

    public VideoService(ILogger<VideoService> logger)
    {
        _logger = logger;
    }

    private static string? FindFfmpegBinaryFolder()
    {
        var ffmpegExe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        var appDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(appDir, ffmpegExe)))
            return appDir;

        foreach (var dir in new[] { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin" })
        {
            if (File.Exists(Path.Combine(dir, ffmpegExe)))
                return dir;
        }

        return null;
    }

    public Task<bool> IsFfmpegAvailableAsync()
    {
        return Task.FromResult(FindFfmpegBinaryFolder() is not null);
    }

    public async Task<string?> CreateTimeLapseAsync(string imagesFolder, string outputPath, double targetDurationSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
            var binaryFolder = FindFfmpegBinaryFolder();
            if (binaryFolder is null)
            {
                _logger.LogError("ffmpeg binary not found. Place ffmpeg alongside the application executable or install it system-wide.");
                return null;
            }

            var imageFiles = Directory.GetFiles(imagesFolder, "*.jpg")
                .OrderBy(f => f)
                .ToArray();

            if (imageFiles.Length < 2)
            {
                _logger.LogWarning("Not enough images to create timelapse in {ImagesFolder}: {Count} images", imagesFolder, imageFiles.Length);
                return null;
            }

            double frameRate = Math.Max(1, imageFiles.Length / targetDurationSeconds);
            frameRate = Math.Min(frameRate, 60);

            _logger.LogInformation("Creating timelapse: {ImageCount} images at {FrameRate:F2}fps -> {Duration}s video", imageFiles.Length, frameRate, targetDurationSeconds);

            var fileListPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_list_{Guid.NewGuid():N}.txt");
            try
            {
                double frameDuration = targetDurationSeconds / imageFiles.Length;
                var lines = imageFiles.SelectMany(f => new[]
                {
                    $"file '{f.Replace("'", "\\'")}'",
                    $"duration {frameDuration.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"
                });
                await File.WriteAllLinesAsync(fileListPath, lines, cancellationToken);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                var success = await FFMpegArguments
                    .FromFileInput(fileListPath, false, options => options
                        .WithCustomArgument("-f concat -safe 0"))
                    .OutputToFile(outputPath, true, options => options
                        .WithVideoCodec("libx264")
                        .WithFramerate(frameRate)
                        .WithConstantRateFactor(23)
                        .WithVideoFilters(filterOptions => filterOptions
                            .Scale(VideoSize.Hd))
                        .WithCustomArgument("-pix_fmt yuv420p")
                        .WithCustomArgument("-movflags +faststart")
                        .WithCustomArgument("-an"))
                    .ProcessAsynchronously(true, new FFOptions { BinaryFolder = binaryFolder });

                if (success && File.Exists(outputPath))
                {
                    _logger.LogInformation("Created timelapse {OutputPath}", outputPath);
                    return outputPath;
                }

                _logger.LogError("FFmpeg processing failed for {OutputPath}", outputPath);
                return null;
            }
            finally
            {
                if (File.Exists(fileListPath))
                    File.Delete(fileListPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating timelapse video");
            return null;
        }
    }
}
