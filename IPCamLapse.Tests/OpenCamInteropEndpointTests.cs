using System.Net;
using System.Text.Json;
using CloudNative.CloudEvents;
using IPCamLapse.Models;
using IPCamLapse.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenCamInterop;

namespace IPCamLapse.Tests;

public sealed class OpenCamInteropEndpointTests
{
    [Fact]
    public async Task ReturnsNotFoundWhenSessionDoesNotExist()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();

            using var response = await client.GetAsync(
                "/api/sessions/deadbeef/events/cloudevents");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ReturnsEmptyCloudEventsBatchForKnownSession()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            await CreateSessionAsync(factory, "a1b2c3d4");

            using var response = await client.GetAsync(
                "/api/sessions/a1b2c3d4/events/cloudevents");

            response.EnsureSuccessStatusCode();
            Assert.Equal(
                StructuredCloudEventJson.BatchContentType,
                response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
            Assert.Empty(await DeserializeBatchAsync(response));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ReturnsLatestEventsInAppendOrderWithStableIds()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(factory, "b1c2d3e4");
            var frames = factory.Services.GetRequiredService<IFrameCatalogService>();
            var firstAt = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
            var secondAt = firstAt.AddSeconds(1);
            var thirdAt = firstAt.AddSeconds(2);

            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = firstAt,
                Kind = CaptureEventKind.State,
                Message = "Ready"
            });
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = secondAt,
                Kind = CaptureEventKind.State,
                Message = "Capturing"
            });
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = thirdAt,
                Kind = CaptureEventKind.State,
                Message = "Paused"
            });

            using var firstResponse = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/cloudevents?limit=2");
            firstResponse.EnsureSuccessStatusCode();
            var firstBatch = await DeserializeBatchAsync(firstResponse);

            using var secondResponse = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/cloudevents?limit=2");
            secondResponse.EnsureSuccessStatusCode();
            var secondBatch = await DeserializeBatchAsync(secondResponse);

            Assert.Equal(
                new[] { $"{session.Id}:2", $"{session.Id}:3" },
                firstBatch.Select(cloudEvent => cloudEvent.Id).ToArray());
            Assert.Equal(
                firstBatch.Select(cloudEvent => cloudEvent.Id).ToArray(),
                secondBatch.Select(cloudEvent => cloudEvent.Id).ToArray());
            Assert.Equal(
                new DateTimeOffset(secondAt),
                firstBatch[0].Time);
            Assert.Equal(
                new DateTimeOffset(thirdAt),
                firstBatch[1].Time);
            Assert.Equal(2, GetData(firstBatch[0]).GetProperty("sequence").GetInt64());
            Assert.Equal(3, GetData(firstBatch[1]).GetProperty("sequence").GetInt64());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task MalformedNonEmptyLogLineConsumesSequenceNumber()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(factory, "c1d2e3f4");
            var frames = factory.Services.GetRequiredService<IFrameCatalogService>();

            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                Kind = CaptureEventKind.State,
                Message = "Capturing"
            });
            await File.AppendAllTextAsync(
                Path.Combine(session.StoragePath!, "events.jsonl"),
                "{ malformed event" + Environment.NewLine);
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                Kind = CaptureEventKind.Frame,
                FrameNumber = 1,
                FileName = "frame_00000001_20260904_100000_000.jpg"
            });

            using var response = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/cloudevents");
            response.EnsureSuccessStatusCode();
            var batch = await DeserializeBatchAsync(response);

            Assert.Equal(
                new[] { $"{session.Id}:1", $"{session.Id}:3" },
                batch.Select(cloudEvent => cloudEvent.Id).ToArray());
            Assert.Equal(1, GetData(batch[0]).GetProperty("sequence").GetInt64());
            Assert.Equal(3, GetData(batch[1]).GetProperty("sequence").GetInt64());
            Assert.DoesNotContain(batch, cloudEvent => cloudEvent.Id == $"{session.Id}:2");
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task MapsEveryCaptureKindWithCamelCaseData()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(factory, "d1e2f3a4");
            var frames = factory.Services.GetRequiredService<IFrameCatalogService>();
            var capturedAt = new DateTime(2026, 9, 4, 11, 0, 0, DateTimeKind.Utc);
            const string fileName = "frame_00000007_20260904_110000_000.jpg";

            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = capturedAt,
                Kind = CaptureEventKind.Frame,
                FrameNumber = 7,
                FileName = fileName
            });
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = capturedAt.AddSeconds(1),
                Kind = CaptureEventKind.Failure,
                Attempt = 2,
                Message = "Camera request timed out."
            });
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                At = capturedAt.AddSeconds(2),
                Kind = CaptureEventKind.State,
                Message = "Rendering"
            });

            using var response = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/cloudevents");
            response.EnsureSuccessStatusCode();
            var batch = await DeserializeBatchAsync(response);

            Assert.Equal(3, batch.Count);
            Assert.All(batch, cloudEvent =>
            {
                Assert.Equal("1.0", cloudEvent.SpecVersion.VersionId);
                Assert.Equal(
                    new Uri($"urn:ipcamlapse:session:{session.Id}"),
                    cloudEvent.Source);
                Assert.Equal("application/json", cloudEvent.DataContentType);
                Assert.Equal(
                    new Uri("urn:opencaminterop:schema:ipcamlapse-capture-event:1"),
                    cloudEvent.DataSchema);
            });

            var frameEvent = batch[0];
            Assert.Equal(IPCamLapseInteropEventTypes.FrameCaptured, frameEvent.Type);
            Assert.Equal($"frames/{fileName}", frameEvent.Subject);
            Assert.Equal(new DateTimeOffset(capturedAt), frameEvent.Time);
            var frameData = GetData(frameEvent);
            AssertPropertyNames(
                frameData,
                "adapter",
                "fileName",
                "frameNumber",
                "kind",
                "sequence",
                "sessionId");
            Assert.Equal(7, frameData.GetProperty("frameNumber").GetInt32());
            Assert.Equal(fileName, frameData.GetProperty("fileName").GetString());

            var failureEvent = batch[1];
            Assert.Equal(IPCamLapseInteropEventTypes.CaptureFailed, failureEvent.Type);
            Assert.Equal($"sessions/{session.Id}", failureEvent.Subject);
            var failureData = GetData(failureEvent);
            AssertPropertyNames(
                failureData,
                "adapter",
                "attempt",
                "failureCode",
                "kind",
                "sequence",
                "sessionId");
            Assert.Equal(2, failureData.GetProperty("attempt").GetInt32());
            Assert.Equal("timeout", failureData.GetProperty("failureCode").GetString());

            var stateEvent = batch[2];
            Assert.Equal(IPCamLapseInteropEventTypes.SessionStateChanged, stateEvent.Type);
            Assert.Equal($"sessions/{session.Id}", stateEvent.Subject);
            var stateData = GetData(stateEvent);
            AssertPropertyNames(
                stateData,
                "adapter",
                "kind",
                "sequence",
                "sessionId",
                "state");
            Assert.Equal("rendering", stateData.GetProperty("state").GetString());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task OmitsSecretsPathsUrlsAndRawMessages()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            const string password = "interop-password-sentinel";
            const string username = "interop-user-sentinel";
            const string url = "http://192.168.1.44/snapshot.jpg?token=interop-url-sentinel";
            var session = await CreateSessionAsync(
                factory,
                "e1f2a3b4",
                new CaptureConfiguration
                {
                    CameraUrl = url,
                    Username = username,
                    Password = password
                });
            var frames = factory.Services.GetRequiredService<IFrameCatalogService>();
            const string fileName = "frame_00000001_20260904_120000_000.jpg";
            var absoluteFramePath = $@"C:\Users\private-user\captures\{fileName}";

            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                Kind = CaptureEventKind.Frame,
                FrameNumber = 1,
                FileName = absoluteFramePath
            });
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                Kind = CaptureEventKind.Failure,
                Attempt = 1,
                Message = $"Camera connection failed. {password} {url}"
            });
            await frames.AppendEventAsync(session.Id, new CaptureEvent
            {
                Kind = CaptureEventKind.State,
                Message = $"Failed: {username} {session.StoragePath}"
            });

            using var response = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/cloudevents");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain(password, body, StringComparison.Ordinal);
            Assert.DoesNotContain(username, body, StringComparison.Ordinal);
            Assert.DoesNotContain(url, body, StringComparison.Ordinal);
            Assert.DoesNotContain("private-user", body, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ipcamlapse-interop-endpoint-tests-",
                body,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cameraUrl", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storagePath", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("message", body, StringComparison.OrdinalIgnoreCase);

            var batch = StructuredCloudEventJson.DeserializeBatch(
                await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(fileName, GetData(batch[0]).GetProperty("fileName").GetString());
            Assert.Equal(
                "connection-failed",
                GetData(batch[1]).GetProperty("failureCode").GetString());
            Assert.Equal("failed", GetData(batch[2]).GetProperty("state").GetString());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ClampsRequestedLimitToCloudEventsBatchMaximum()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var factory = new IPCamLapseFactory(root);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(factory, "f1a2b3c4");
            var frames = factory.Services.GetRequiredService<IFrameCatalogService>();
            for (var index = 1; index <= 300; index++)
            {
                await frames.AppendEventAsync(session.Id, new CaptureEvent
                {
                    Kind = CaptureEventKind.State,
                    Message = "Capturing"
                });
            }

            using var response = await client.GetAsync(
                $"/api/sessions/{session.Id}/events/cloudevents?limit=500");
            response.EnsureSuccessStatusCode();
            var batch = await DeserializeBatchAsync(response);

            Assert.Equal(StructuredCloudEventJson.MaxBatchEvents, batch.Count);
            Assert.Equal($"{session.Id}:45", batch[0].Id);
            Assert.Equal($"{session.Id}:300", batch[^1].Id);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task<CaptureSession> CreateSessionAsync(
        IPCamLapseFactory factory,
        string id,
        CaptureConfiguration? configuration = null)
    {
        var sessions = factory.Services.GetRequiredService<ICaptureSessionService>();
        return await sessions.CreateSessionAsync(new CaptureSession
        {
            Id = id,
            Name = "OpenCamInterop endpoint test",
            Configuration = configuration ?? new CaptureConfiguration()
        });
    }

    private static async Task<IReadOnlyList<CloudEvent>> DeserializeBatchAsync(
        HttpResponseMessage response)
    {
        return StructuredCloudEventJson.DeserializeBatch(
            await response.Content.ReadAsByteArrayAsync());
    }

    private static JsonElement GetData(CloudEvent cloudEvent)
    {
        return Assert.IsType<JsonElement>(cloudEvent.Data);
    }

    private static void AssertPropertyNames(JsonElement data, params string[] expected)
    {
        var expectedNames = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var actualNames = data
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"ipcamlapse-interop-endpoint-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class IPCamLapseFactory : WebApplicationFactory<Program>
    {
        private readonly string _root;

        public IPCamLapseFactory(string root)
        {
            _root = root;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:DataPath", Path.Combine(_root, "data"));
        }
    }
}
