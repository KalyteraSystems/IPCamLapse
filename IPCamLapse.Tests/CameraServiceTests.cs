using System.Net;
using System.Net.Http.Headers;
using IPCamLapse.Models;
using IPCamLapse.Options;
using IPCamLapse.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPCamLapse.Tests;

public sealed class CameraServiceTests
{
    private static readonly byte[] Png = [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00
    ];

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

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    public async Task AcceptsPngAndPreservesItsFormat(string mediaType)
    {
        var factory = new RecordingHttpClientFactory(_ => ImageResponse(Png, mediaType));
        var result = await CreateService(factory).CaptureSnapshotAsync(Endpoint());

        Assert.True(result.Success, result.Error);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(".png", result.Extension);
        Assert.Equal(Png, result.Data);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/jpg")]
    [InlineData("application/octet-stream")]
    public async Task AcceptsJpegForEverySupportedJpegMediaType(string mediaType)
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var factory = new RecordingHttpClientFactory(_ => ImageResponse(jpeg, mediaType));
        var result = await CreateService(factory).CaptureSnapshotAsync(Endpoint());

        Assert.True(result.Success, result.Error);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(".jpg", result.Extension);
    }

    [Theory]
    [MemberData(nameof(RejectedImageResponses))]
    public async Task RejectsInvalidTruncatedOrMismatchedImages(
        string mediaType,
        byte[] payload)
    {
        var factory = new RecordingHttpClientFactory(_ => ImageResponse(payload, mediaType));
        var result = await CreateService(factory).CaptureSnapshotAsync(Endpoint());

        Assert.False(result.Success);
        Assert.Contains("signature", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string, byte[]> RejectedImageResponses => new()
    {
        { "image/jpeg", Png },
        { "image/png", [0xFF, 0xD8, 0xFF, 0xD9] },
        { "image/jpeg", [0xFF, 0xD8] },
        { "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A] },
        { "application/octet-stream", [0x89, 0x50, 0x4E] }
    };

    [Fact]
    public async Task RejectsUnsupportedDeclaredContentType()
    {
        var factory = new RecordingHttpClientFactory(_ => ImageResponse(Png, "image/gif"));
        var result = await CreateService(factory).CaptureSnapshotAsync(Endpoint());

        Assert.False(result.Success);
        Assert.Contains("Unsupported content type", result.Error, StringComparison.OrdinalIgnoreCase);
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
    public async Task RejectsUnknownLengthBodyAsSoonAsItCrossesLimit()
    {
        var payload = new byte[2_048];
        Png.CopyTo(payload, 0);
        var factory = new RecordingHttpClientFactory(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(payload)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });
        var result = await CreateService(factory, maxSnapshotBytes: 1_024)
            .CaptureSnapshotAsync(Endpoint());

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

    [Fact]
    public async Task CallerCancellationPropagatesOperationCanceledException()
    {
        var client = new HttpClient(new DelayingHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        var service = CreateService(new FixedHttpClientFactory(client));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CaptureSnapshotAsync(Endpoint(), cancellation.Token));
    }

    [Fact]
    public async Task HttpClientTimeoutReturnsSafeFailureResult()
    {
        var client = new HttpClient(new DelayingHandler()) { Timeout = TimeSpan.FromMilliseconds(10) };
        var service = CreateService(new FixedHttpClientFactory(client));

        var result = await service.CaptureSnapshotAsync(Endpoint());

        Assert.False(result.Success);
        Assert.Equal("Camera request timed out.", result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ErrorsAndLogsDoNotExposeCameraSecrets(bool isHttpRequestException)
    {
        const string sentinel = "SENTINEL-DO-NOT-LOG";
        var logger = new RecordingLogger();
        var factory = new RecordingHttpClientFactory(_ => throw isHttpRequestException
            ? new HttpRequestException($"failed request containing {sentinel}")
            : new InvalidOperationException($"failed operation containing {sentinel}"));
        var service = CreateService(factory, logger: logger);
        var endpoint = new CameraEndpoint(
            "Test camera",
            $"http://192.168.1.25/{sentinel}/snapshot.jpg?token={sentinel}",
            $"user-{sentinel}",
            $"password-{sentinel}",
            false,
            false);

        var result = await service.CaptureSnapshotAsync(endpoint);

        Assert.False(result.Success);
        Assert.DoesNotContain(sentinel, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, logger.RenderedRecord, StringComparison.Ordinal);
    }

    private static CameraService CreateService(
        IHttpClientFactory factory,
        long maxSnapshotBytes = 20 * 1024 * 1024,
        ILogger<CameraService>? logger = null)
    {
        var options = new CameraAccessOptions { MaxSnapshotBytes = maxSnapshotBytes };
        return new CameraService(
            factory,
            new CameraUrlPolicy(Microsoft.Extensions.Options.Options.Create(options)),
            Microsoft.Extensions.Options.Options.Create(options),
            new DemoFrameGenerator(),
            logger ?? NullLogger<CameraService>.Instance);
    }

    private static CameraEndpoint Endpoint() => new(
        "Test camera",
        "http://192.168.1.25/snapshot.jpg",
        null,
        null,
        false,
        false);

    private static HttpResponseMessage JpegResponse(byte[] payload)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return response;
    }

    private static HttpResponseMessage ImageResponse(byte[] payload, string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _payload;

        public UnknownLengthContent(byte[] payload) => _payload = payload;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class RecordingLogger : ILogger<CameraService>
    {
        public string RenderedRecord { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            RenderedRecord = formatter(state, exception);
            if (exception is not null)
                RenderedRecord += Environment.NewLine + exception;
        }
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

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class DelayingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
