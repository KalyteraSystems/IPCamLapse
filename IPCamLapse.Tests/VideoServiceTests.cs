using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPCamLapse.Tests;

public sealed class VideoServiceTests
{
    [Fact]
    public async Task InvalidSettingsAreRejectedBeforeFfmpegStarts()
    {
        var service = new VideoService(NullLogger<VideoService>.Instance);
        var result = await service.CreateTimeLapseAsync(new VideoRenderRequest(
            ["first.png", "second.png"],
            "video.mp4",
            5,
            new VideoSettings { Width = 319 }));

        Assert.False(result.Success);
        Assert.Contains("dimensions", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
