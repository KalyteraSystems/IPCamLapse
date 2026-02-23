namespace IPCamLapse.Services;

public interface ICameraService
{
    Task<byte[]?> CaptureSnapshotAsync(string cameraUrl, string? username, string? password, CancellationToken cancellationToken = default);
}

public class CameraService : ICameraService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CameraService> _logger;

    public CameraService(IHttpClientFactory httpClientFactory, ILogger<CameraService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<byte[]?> CaptureSnapshotAsync(string cameraUrl, string? username, string? password, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Camera");

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await client.GetAsync(cameraUrl, cts.Token);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.StartsWith("image/") && !contentType.Contains("octet-stream"))
            {
                _logger.LogWarning("Unexpected content type from camera: {ContentType}", contentType);
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture snapshot from {CameraUrl}", cameraUrl);
            return null;
        }
    }
}
