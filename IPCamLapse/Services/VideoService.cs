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

    public Task<bool> IsFfmpegAvailableAsync()
    {
        return Task.FromResult(File.Exists("/usr/bin/ffmpeg") || File.Exists("/usr/local/bin/ffmpeg"));
    }

    public async Task<string?> CreateTimeLapseAsync(string imagesFolder, string outputPath, double targetDurationSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
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
                var lines = imageFiles.Select(f => $"file '{f.Replace("'", "\\'")}'");
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
                    .ProcessAsynchronously(true, new FFOptions { BinaryFolder = "/usr/bin" });

                if (success && File.Exists(outputPath))
                {
                    _logger.LogInformation("Timelapse created successfully: {OutputPath}", outputPath);
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
