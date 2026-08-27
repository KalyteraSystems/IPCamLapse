using System.Net;
using System.Net.Http.Headers;
using IPCamLapse.Models;
using IPCamLapse.Options;
using IPCamLapse.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPCamLapse.Tests;

public sealed class CameraServiceTests
{
    [Fact]
    public async Task AcceptsJpegAndUsesRequestScopedBasicAuthentication()
    {
        AuthenticationHeaderValue? observedAuthorization = null;
        var factory = new RecordingHttpClientFactory(request =>
        {
            observedAuthorization = request.Headers.Authorization;
            return JpegResponse([0xFF, 0xD8, 0xFF, 0xD9]);
        });
        var service = CreateService(factory);

        var result = await service.CaptureSnapshotAsync(new CameraEndpoint(
            "Test camera",
            "http://192.168.1.25/snapshot.jpg",
            "camera",
            "password",
            false,
            false));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Data);
        Assert.Equal("CameraStrict", factory.RequestedName);
        Assert.Equal("Basic", observedAuthorization?.Scheme);
        Assert.NotNull(observedAuthorization?.Parameter);
    }

    [Fact]
    public async Task InvalidCertificateModeMustBeExplicitlySelected()
    {
        var factory = new RecordingHttpClientFactory(_ => JpegResponse([0xFF, 0xD8, 0xFF, 0xD9]));
        var service = CreateService(factory);

        await service.CaptureSnapshotAsync(new CameraEndpoint(
            "Test camera",
            "https://192.168.1.25/snapshot.jpg",
            null,
            null,
            true,
            false));

        Assert.Equal("CameraInsecure", factory.RequestedName);
    }

    [Fact]
    public async Task RejectsOversizedSnapshot()
    {
        var payload = new byte[2_048];
        payload[0] = 0xFF;
        payload[1] = 0xD8;
        payload[2] = 0xFF;
        var factory = new RecordingHttpClientFactory(_ => JpegResponse(payload));
        var service = CreateService(factory, maxSnapshotBytes: 1_024);

        var result = await service.CaptureSnapshotAsync(new CameraEndpoint(
            "Test camera",
            "http://192.168.1.25/snapshot.jpg",
            null,
            null,
            false,
            false));

        Assert.False(result.Success);
        Assert.Contains("size limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsPayloadWithoutJpegSignature()
    {
        var factory = new RecordingHttpClientFactory(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("not an image"u8.ToArray())
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return response;
        });
        var service = CreateService(factory);

        var result = await service.CaptureSnapshotAsync(new CameraEndpoint(
            "Test camera",
            "http://192.168.1.25/snapshot.jpg",
            null,
            null,
            false,
            false));

        Assert.False(result.Success);
        Assert.Contains("JPEG", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static CameraService CreateService(
        RecordingHttpClientFactory factory,
        long maxSnapshotBytes = 20 * 1024 * 1024)
    {
        var options = new CameraAccessOptions { MaxSnapshotBytes = maxSnapshotBytes };
        return new CameraService(
            factory,
            new CameraUrlPolicy(Microsoft.Extensions.Options.Options.Create(options)),
            Microsoft.Extensions.Options.Options.Create(options),
            new DemoFrameGenerator(),
            NullLogger<CameraService>.Instance);
    }

    private static HttpResponseMessage JpegResponse(byte[] payload)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return response;
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return new HttpClient(new StubHandler(_responseFactory), disposeHandler: true);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responseFactory(request));
    }
}
