namespace Homon.Api.Tests;

/// <summary>
/// Scripts one response (or throws, or hangs) per test — the seam <c>HttpProbeRunnerTests</c>
/// uses instead of ever making a real outbound HTTP request. The gate must never reach the
/// network; every case is driven from this handler.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    /// <summary>Must be set before the handler is used — there is no default response.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Handler { get; set; }

    /// <summary>The most recent request the runner sent — for asserting headers, method, URI.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        if (Handler is null)
        {
            throw new InvalidOperationException($"{nameof(StubHttpMessageHandler)}.{nameof(Handler)} was not set.");
        }

        return await Handler(request, cancellationToken);
    }
}

/// <summary>
/// Returns an <see cref="HttpClient"/> wrapping a <see cref="StubHttpMessageHandler"/> for
/// <see cref="Homon.Infrastructure.Monitoring.HttpProbeRunner.HttpClientName"/> — the fake
/// <see cref="IHttpClientFactory"/> the plan's test description asks for.
/// </summary>
internal sealed class FakeHttpClientFactory(string clientName, HttpMessageHandler handler, Uri? baseAddress = null)
    : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        if (!string.Equals(name, clientName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected HttpClient name '{name}', expected '{clientName}'.");
        }

        // disposeHandler: false — the same handler instance is asserted against after the
        // call returns, and HttpClient disposes its handler by default.
        return new HttpClient(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }
}
