using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using IPCamLapse.Options;
using Microsoft.Extensions.Options;

namespace IPCamLapse.Services;

public interface ICameraService
{
    Task<byte[]?> CaptureSnapshotAsync(
        string cameraUrl,
        string? username,
        string? password,
        bool allowInvalidCertificate = false,
        CancellationToken cancellationToken = default);
}

public class CameraService : ICameraService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICameraUrlPolicy _urlPolicy;
    private readonly CameraAccessOptions _options;
    private readonly ILogger<CameraService> _logger;

    public CameraService(
        IHttpClientFactory httpClientFactory,
        ICameraUrlPolicy urlPolicy,
        IOptions<CameraAccessOptions> options,
        ILogger<CameraService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _urlPolicy = urlPolicy;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]?> CaptureSnapshotAsync(
        string cameraUrl,
        string? username,
        string? password,
        bool allowInvalidCertificate = false,
        CancellationToken cancellationToken = default)
    {
        var validation = await _urlPolicy.ValidateAsync(cameraUrl, cancellationToken);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Rejected camera URL: {Reason}", validation.Error);
            return null;
        }

        var uri = validation.Uri!;
        var endpointLabel = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        try
        {
            var client = _httpClientFactory.CreateClient(
                allowInvalidCertificate ? "CameraInsecure" : "CameraStrict");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > 0 &&
                response.Content.Headers.ContentLength > _options.MaxSnapshotBytes)
            {
                _logger.LogWarning("Camera {CameraEndpoint} returned an oversized snapshot", endpointLabel);
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Camera {CameraEndpoint} returned unsupported content type {ContentType}", endpointLabel, mediaType);
                return null;
            }

            var data = await ReadBoundedAsync(response.Content, _options.MaxSnapshotBytes, cancellationToken);
            if (data is null || data.Length < 3 || data[0] != 0xFF || data[1] != 0xD8 || data[2] != 0xFF)
            {
                _logger.LogWarning("Camera {CameraEndpoint} did not return a valid JPEG snapshot", endpointLabel);
                return null;
            }

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture snapshot from {CameraEndpoint}", endpointLabel);
            return null;
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
