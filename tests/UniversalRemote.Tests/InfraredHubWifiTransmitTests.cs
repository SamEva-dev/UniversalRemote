using System.Net;
using System.Text;
using System.Text.Json;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Hub.Wifi;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class InfraredHubWifiTransmitTests
{
    private static readonly InfraredHubId HubId = new("living-room-hub");
    private static readonly InfraredSignal Signal = new(38_000, [9_000, 4_500, 560, 560]);

    [Fact]
    public async Task Accepted_response_sends_one_versioned_post_with_frequency_pattern_and_correlated_identity()
    {
        JsonElement captured = default;
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(WifiInfraredHubProtocol.TransmitPath, request.RequestUri!.AbsolutePath);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var document = JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            captured = document.RootElement.Clone();
            return JsonResponse(ResponseJson(
                captured.GetProperty("requestId").GetString()!,
                captured.GetProperty("hubId").GetString()!,
                "accepted"));
        });
        var client = Client(handler);

        var result = await client.TransmitAsync(Connection(), Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Accepted, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, captured.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(HubId.Value, captured.GetProperty("hubId").GetString());
        Assert.Equal(38_000, captured.GetProperty("carrierFrequencyHz").GetInt32());
        Assert.Equal(new[] { 9_000, 4_500, 560, 560 }, captured.GetProperty("patternMicroseconds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal(32, captured.GetProperty("requestId").GetString()!.Length);
    }

    [Theory]
    [InlineData("failed_before_send", InfraredHubTransmitOutcome.FailedBeforeSend)]
    [InlineData("hub_unavailable", InfraredHubTransmitOutcome.HubUnavailable)]
    [InlineData("frequency_unsupported", InfraredHubTransmitOutcome.FrequencyUnsupported)]
    [InlineData("invalid_signal", InfraredHubTransmitOutcome.InvalidSignal)]
    public async Task Explicit_hub_rejection_maps_to_non_ambiguous_outcome_without_retry(
        string wireOutcome,
        InfraredHubTransmitOutcome expected)
    {
        var handler = CorrelatedResponseHandler(wireOutcome, HttpStatusCode.UnprocessableEntity);

        var result = await Client(handler).TransmitAsync(Connection(), Signal);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Failure_after_sendasync_has_started_is_unknown_and_is_never_retried()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("simulated connection reset"));

        var result = await Client(handler).TransmitAsync(Connection(), Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Internal_http_timeout_after_attempt_is_unknown_and_is_never_retried()
    {
        var handler = new RecordingHandler((_, _) => throw new OperationCanceledException("simulated HttpClient timeout"));

        var result = await Client(handler).TransmitAsync(Connection(), Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Redirect_after_post_is_delivery_ambiguous_and_never_followed_or_retried()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://8.8.8.8/evil");
            return Task.FromResult(response);
        });

        var result = await Client(handler).TransmitAsync(Connection(), Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("hub")]
    [InlineData("schema")]
    [InlineData("json")]
    public async Task Uncorrelated_or_invalid_ack_is_unknown_and_never_retried(string corruption)
    {
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            var requestId = document.RootElement.GetProperty("requestId").GetString()!;
            var hubId = document.RootElement.GetProperty("hubId").GetString()!;
            return corruption switch
            {
                "request" => JsonResponse(ResponseJson("different-request", hubId, "accepted")),
                "hub" => JsonResponse(ResponseJson(requestId, "different-hub", "accepted")),
                "schema" => JsonResponse(ResponseJson(requestId, hubId, "accepted", schemaVersion: 2)),
                _ => JsonResponse("{broken")
            };
        });

        var result = await Client(handler).TransmitAsync(Connection(), Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Accepted_body_on_non_success_http_status_is_unknown()
    {
        var handler = CorrelatedResponseHandler("accepted", HttpStatusCode.ServiceUnavailable);

        var result = await Client(handler).TransmitAsync(Connection(), Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Unsupported_connection_api_is_failed_before_send_and_performs_no_post()
    {
        var handler = CorrelatedResponseHandler("accepted");
        var connection = new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, apiVersion: 2);

        var result = await Client(handler).TransmitAsync(connection, Signal);

        Assert.Equal(InfraredHubTransmitOutcome.FailedBeforeSend, result.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_before_send_performs_no_post()
    {
        var handler = CorrelatedResponseHandler("accepted");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(handler).TransmitAsync(Connection(), Signal, cancellation.Token));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Transport_without_saved_connection_is_hub_unavailable_and_does_not_enter_transmit_client()
    {
        var store = new InMemoryWifiInfraredHubConnectionStore();
        var transmitHandler = CorrelatedResponseHandler("accepted");
        var info = new WifiInfraredHubApiClient(new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse("{}")))));
        var transport = new WifiInfraredHubTransport(store, info, Client(transmitHandler));

        var result = await transport.TransmitAsync(HubId, Signal);

        Assert.Equal(InfraredHubTransmitOutcome.HubUnavailable, result.Outcome);
        Assert.Equal(0, transmitHandler.Calls);
    }

    [Fact]
    public async Task Transport_with_saved_connection_performs_exactly_one_transmit_post()
    {
        var store = new InMemoryWifiInfraredHubConnectionStore();
        await store.SaveAsync(Connection());
        var transmitHandler = CorrelatedResponseHandler("accepted");
        var info = new WifiInfraredHubApiClient(new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse("{}")))));
        var transport = new WifiInfraredHubTransport(store, info, Client(transmitHandler));

        var result = await transport.TransmitAsync(HubId, Signal);

        Assert.Equal(InfraredHubTransmitOutcome.Accepted, result.Outcome);
        Assert.Equal(1, transmitHandler.Calls);
    }

    private static WifiInfraredHubConnection Connection()
        => new(HubId, "192.168.1.44", 8081, WifiInfraredHubProtocol.ApiVersion);

    private static WifiInfraredHubTransmitClient Client(RecordingHandler handler)
        => new(new HttpClient(handler));

    private static RecordingHandler CorrelatedResponseHandler(
        string outcome,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(async (request, cancellationToken) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            var requestId = document.RootElement.GetProperty("requestId").GetString()!;
            var hubId = document.RootElement.GetProperty("hubId").GetString()!;
            return JsonResponse(ResponseJson(requestId, hubId, outcome), statusCode);
        });

    private static string ResponseJson(string requestId, string hubId, string outcome, int schemaVersion = 1)
        => $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "requestId": "{{requestId}}",
          "hubId": "{{hubId}}",
          "outcome": "{{outcome}}"
        }
        """;

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return responder(request, cancellationToken);
        }
    }
}
