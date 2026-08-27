using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using IPCamLapse.Models;
using IPCamLapse.Options;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Services;

public sealed record CameraCaptureResult(
    bool Success,
    byte[]? Data,
    string ContentType,
    string Extension,
    string? Error,
    HttpStatusCode? StatusCode,
    TimeSpan Duration)
{
    public static CameraCaptureResult Failed(string error, TimeSpan duration, HttpStatusCode? statusCode = null) =>
        new(false, null, string.Empty, string.Empty, error, statusCode, duration);
}

public interface ICameraService
{
    Task<CameraCaptureResult> CaptureSnapshotAsync(
        CameraEndpoint endpoint,
        CancellationToken cancellationToken = default);
}

public sealed class CameraService : ICameraService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICameraUrlPolicy _urlPolicy;
    private readonly CameraAccessOptions _options;
    private readonly IDemoFrameGenerator _demoFrames;
    private readonly ILogger<CameraService> _logger;

    public CameraService(
        IHttpClientFactory httpClientFactory,
        ICameraUrlPolicy urlPolicy,
        IOptions<CameraAccessOptions> options,
        IDemoFrameGenerator demoFrames,
        ILogger<CameraService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _urlPolicy = urlPolicy;
        _options = options.Value;
        _demoFrames = demoFrames;
        _logger = logger;
    }

    public async Task<CameraCaptureResult> CaptureSnapshotAsync(
        CameraEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (endpoint.IsDemo)
        {
            var frame = _demoFrames.CreateFrame();
            return new CameraCaptureResult(
                true,
                frame,
                "image/png",
                ".png",
                null,
                HttpStatusCode.OK,
                stopwatch.Elapsed);
        }

        var validation = await _urlPolicy.ValidateAsync(endpoint.Url, cancellationToken);
        if (!validation.IsValid)
            return CameraCaptureResult.Failed(validation.Error ?? "Camera URL rejected.", stopwatch.Elapsed);

        var uri = validation.Uri!;
        var endpointLabel = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        try
        {
            var client = _httpClientFactory.CreateClient(
                endpoint.AllowInvalidCertificate ? "CameraInsecure" : "CameraStrict");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrEmpty(endpoint.Username) && !string.IsNullOrEmpty(endpoint.Password))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{endpoint.Username}:{endpoint.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CameraCaptureResult.Failed(
                    $"Camera returned HTTP {(int)response.StatusCode}.",
                    stopwatch.Elapsed,
                    response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is > 0 &&
                response.Content.Headers.ContentLength > _options.MaxSnapshotBytes)
            {
                return CameraCaptureResult.Failed("Snapshot exceeds the configured size limit.", stopwatch.Elapsed);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return CameraCaptureResult.Failed(
                    $"Unsupported content type {mediaType}.",
                    stopwatch.Elapsed,
                    response.StatusCode);
            }

            var data = await ReadBoundedAsync(response.Content, _options.MaxSnapshotBytes, cancellationToken);
            if (data is null)
                return CameraCaptureResult.Failed("Snapshot exceeds the configured size limit.", stopwatch.Elapsed);
            if (data.Length < 3 || data[0] != 0xff || data[1] != 0xd8 || data[2] != 0xff)
                return CameraCaptureResult.Failed("Camera did not return a JPEG image.", stopwatch.Elapsed);

            return new CameraCaptureResult(
                true,
                data,
                "image/jpeg",
                ".jpg",
                null,
                response.StatusCode,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return CameraCaptureResult.Failed("Camera request timed out.", stopwatch.Elapsed);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Camera request failed for {CameraEndpoint}", endpointLabel);
            return CameraCaptureResult.Failed("Camera connection failed.", stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Snapshot capture failed for {CameraEndpoint}", endpointLabel);
            return CameraCaptureResult.Failed("Snapshot capture failed.", stopwatch.Elapsed);
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;
                if (output.Length + read > maxBytes)
                    return null;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
