using System.Net;
using System.Security.Cryptography;
using System.Text;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Security;

namespace Homon.Api.Tests;

public class HttpProbeRunnerTests
{
    [Fact]
    public async Task A_200_with_no_expected_status_code_is_a_success()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK));

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("HTTP 200", result.Detail);
        Assert.NotNull(result.LatencyMs);
    }

    [Fact]
    public async Task A_204_with_no_expected_status_code_is_a_success_2xx_generally_not_just_200()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.NoContent));

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("HTTP 204", result.Detail);
    }

    [Fact]
    public async Task A_completed_response_after_redirects_is_evaluated_on_its_final_status()
    {
        // AllowAutoRedirect itself lives on the named HttpClient's primary handler
        // (InfrastructureServiceCollectionExtensions.AddHomonMonitoring), not in this runner —
        // a raw StubHttpMessageHandler bypasses that handler entirely, so redirect-chasing
        // itself is not exercised at this seam. What IS this runner's own job, and what this
        // asserts, is that whatever status the client finally hands back is evaluated the
        // same way a same one-hop response would be.
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK));

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("HTTP 200", result.Detail);
    }

    [Fact]
    public async Task A_404_with_no_expected_status_code_is_a_failure()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.NotFound));

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("HTTP 404", result.Detail);
    }

    [Fact]
    public async Task A_503_with_no_expected_status_code_is_a_failure()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.ServiceUnavailable));

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("HTTP 503", result.Detail);
    }

    [Fact]
    public async Task A_configured_expected_status_code_replaces_the_2xx_rule()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.Unauthorized));

        var options = MakeOptions(expectedStatusCode: 401);
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("HTTP 401", result.Detail);
    }

    [Fact]
    public async Task A_negated_expected_status_code_match_is_a_failure()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK));

        var options = MakeOptions(expectedStatusCode: 200, expectedStatusCodeNegate: true);
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("HTTP 200 (expected not 200)", result.Detail);
    }

    [Fact]
    public async Task Body_text_present_is_a_success_when_status_passes()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, "all systems ok"));

        var options = MakeOptions(expectedBodyText: "systems ok");
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("HTTP 200", result.Detail);
    }

    [Fact]
    public async Task Negated_body_text_mismatch_present_is_a_failure()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, "maintenance mode"));

        var options = MakeOptions(expectedBodyText: "maintenance mode", expectedBodyTextNegate: true);
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("body contained \"maintenance mode\"", result.Detail);
    }

    [Fact]
    public async Task Status_passes_but_body_fails_reports_the_bodys_own_detail_not_the_status_detail()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, "nope"));

        var options = MakeOptions(expectedBodyText: "all good");
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("body did not contain \"all good\"", result.Detail);
    }

    [Fact]
    public async Task A_delayed_response_past_the_probes_own_timeout_reports_that_timeout()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = async (_, cancellationToken) =>
        {
            // Hangs until the runner's own linked CancelAfter(1s) fires — the domain-validated
            // minimum, so this test's real wait is ~1 s, not 10.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return MakeResponse(HttpStatusCode.OK);
        };

        var options = MakeOptions(timeoutSeconds: HttpProbeOptions.MinTimeoutSeconds);
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.LatencyMs);
        Assert.Equal("timeout after 1 s", result.Detail);
    }

    [Fact]
    public async Task A_body_past_the_cap_with_the_expected_text_beyond_it_is_a_mismatch()
    {
        var (runner, handler, _) = MakeRunner();
        var padding = new string('a', HttpProbeRunner.MaxBodyBytes);
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, padding + "needle"));

        var options = MakeOptions(expectedBodyText: "needle");
        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("body did not contain \"needle\"", result.Detail);
    }

    [Fact]
    public async Task Bearer_credential_sets_the_authorization_header()
    {
        var (runner, handler, protector) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK));

        var credential = new HttpCredential { Type = HttpCredentialType.Bearer, ProtectedSecret = "protected:my-token" };
        var options = MakeOptions(credential: credential);
        await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.Equal("Bearer my-token", handler.LastRequest!.Headers.GetValues("Authorization").Single());
        Assert.Equal("protected:my-token", protector.LastUnprotected);
    }

    [Fact]
    public async Task Basic_credential_sets_a_base64_authorization_header()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK));

        var credential = new HttpCredential
        {
            Type = HttpCredentialType.Basic,
            Username = "admin",
            ProtectedSecret = "protected:hunter2",
        };
        var options = MakeOptions(credential: credential);
        await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:hunter2"));
        Assert.Equal(expected, handler.LastRequest!.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task An_unreadable_credential_is_a_failed_observation_not_an_escaped_exception()
    {
        var (runner, handler, protector) = MakeRunner();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK));
        protector.ThrowOnUnprotect = true;

        var credential = new HttpCredential { Type = HttpCredentialType.Bearer, ProtectedSecret = "protected:whatever" };
        var options = MakeOptions(credential: credential);

        var result = await runner.RunAsync(MakeProbe(options), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.LatencyMs);
        Assert.Equal("credentials unreadable — re-enter them", result.Detail);
    }

    [Fact]
    public async Task A_connection_failure_reports_the_connection_failed_detail()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "Connection refused");

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.LatencyMs);
        Assert.StartsWith("connection failed:", result.Detail);
    }

    [Fact]
    public async Task An_unignored_TLS_failure_reports_the_TLS_certificate_error_detail()
    {
        var (runner, handler, _) = MakeRunner();
        handler.Handler = (_, _) => throw new HttpRequestException(
            HttpRequestError.SecureConnectionError, "The SSL connection could not be established");

        var result = await runner.RunAsync(MakeProbe(MakeOptions()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.LatencyMs);
        Assert.StartsWith("TLS certificate error:", result.Detail);
    }

    private static (HttpProbeRunner Runner, StubHttpMessageHandler Handler, FakeSecretProtector Protector) MakeRunner()
    {
        var handler = new StubHttpMessageHandler();
        var httpClientFactory = new FakeHttpClientFactory(HttpProbeRunner.HttpClientName, handler);
        var protector = new FakeSecretProtector();

        return (new HttpProbeRunner(httpClientFactory, protector), handler, protector);
    }

    private static Probe MakeProbe(HttpProbeOptions options) => new()
    {
        Id = Guid.NewGuid(),
        Name = "svc",
        Host = "svc.local",
        Kind = ProbeKind.Http,
        HttpOptions = options,
    };

    private static HttpProbeOptions MakeOptions(
        HttpProbeMethod method = HttpProbeMethod.Get,
        string path = "api/health",
        int timeoutSeconds = 10,
        int? expectedStatusCode = null,
        bool expectedStatusCodeNegate = false,
        string? expectedBodyText = null,
        bool expectedBodyTextNegate = false,
        HttpCredential? credential = null)
    {
        return new HttpProbeOptions
        {
            Method = method,
            Path = path,
            TimeoutSeconds = timeoutSeconds,
            ExpectedStatusCode = expectedStatusCode,
            ExpectedStatusCodeNegate = expectedStatusCodeNegate,
            ExpectedBodyText = expectedBodyText,
            ExpectedBodyTextNegate = expectedBodyTextNegate,
            Credential = credential ?? new HttpCredential(),
        };
    }

    private static HttpResponseMessage MakeResponse(HttpStatusCode statusCode, string? body = null) =>
        new(statusCode) { Content = new StringContent(body ?? string.Empty) };

    /// <summary>Round-trips through a fake "protected:{plaintext}" wrapper — not real crypto.</summary>
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public bool ThrowOnUnprotect { get; set; }

        public string? LastUnprotected { get; private set; }

        public string Protect(string plaintext) => $"protected:{plaintext}";

        public string Unprotect(string protectedValue)
        {
            if (ThrowOnUnprotect)
            {
                throw new CryptographicException("key ring unavailable");
            }

            LastUnprotected = protectedValue;
            return protectedValue.StartsWith("protected:", StringComparison.Ordinal)
                ? protectedValue["protected:".Length..]
                : protectedValue;
        }
    }
}
