using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Security;

namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// Polls a <see cref="ProbeKind.Http"/> probe: builds a request from
/// <see cref="Probe.HttpOptions"/>, sends it through the shared named <see cref="HttpClient"/>
/// (see <c>InfrastructureServiceCollectionExtensions.AddHomonMonitoring</c>), and evaluates
/// status/body per plan 003's Decision 4.
/// </summary>
public sealed class HttpProbeRunner(IHttpClientFactory httpClientFactory, ISecretProtector secretProtector)
    : IProbeRunner
{
    /// <summary>The name <c>AddHttpClient</c> registers this runner's client under.</summary>
    public const string HttpClientName = "HomonHttpProbe";

    /// <summary>
    /// Set per request from <see cref="HttpProbeOptions.IgnoreCertificateErrors"/>. Read by
    /// the named client's <c>ServerCertificateCustomValidationCallback</c> — one pooled
    /// handler making a per-request decision, rather than a second client. Specific to this
    /// runner's own named client; a later <see cref="HttpClient"/> elsewhere does not inherit
    /// this callback automatically.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> IgnoreCertificateErrorsOption = new("Homon.IgnoreCertificateErrors");

    /// <summary>Largest response body read before testing <see cref="HttpProbeOptions.ExpectedBodyText"/>.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    public ProbeKind Kind => ProbeKind.Http;

    public async Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var options = probe.HttpOptions
            ?? throw new InvalidOperationException(
                $"HttpProbeRunner requires {nameof(Probe.HttpOptions)} on probe {probe.Id}.");

        string? authorizationHeader;
        try
        {
            authorizationHeader = BuildAuthorizationHeader(options.Credential);
        }
        catch (CryptographicException)
        {
            // The key ring cannot unprotect this probe's stored secret — typically because
            // the dataprotection-keys volume was lost. Turned into a failed observation here,
            // never let escape to the scheduler. See plan 003's Decision 2.
            return new ProbeResult(false, null, "credentials unreadable — re-enter them");
        }

        var scheme = options.UseHttps ? "https" : "http";
        var uri = new Uri($"{scheme}://{probe.Host}/{options.Path.TrimStart('/')}");
        var method = options.Method == HttpProbeMethod.Head ? HttpMethod.Head : HttpMethod.Get;

        using var request = new HttpRequestMessage(method, uri);
        if (authorizationHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }

        request.Options.Set(IgnoreCertificateErrorsOption, options.IgnoreCertificateErrors);

        var client = httpClientFactory.CreateClient(HttpClientName);

        // The probe's own configured timeout (Decision 4a) — not HttpClient.Timeout, which is
        // shared across every HTTP probe via the factory. Same linked-token-source shape as
        // ResendEmailSender.SendTimeout.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);

            var body = await ReadCappedBodyAsync(response, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var (succeeded, detail) = Evaluate(options, response.StatusCode, body);

            return new ProbeResult(succeeded, stopwatch.Elapsed.TotalMilliseconds, detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own CancelAfter fired, not the caller's (the scheduler's own outer
            // CancelAfter) — report this runner's more specific detail. If the caller's own
            // token fired instead, this rethrows unhandled and the scheduler's generic
            // "probe timed out" detail surfaces, per plan 003's Decision 4a.
            return new ProbeResult(false, null, $"timeout after {options.TimeoutSeconds} s");
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            return new ProbeResult(false, null, $"TLS certificate error: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            return new ProbeResult(false, null, $"connection failed: {exception.Message}");
        }
    }

    private string? BuildAuthorizationHeader(HttpCredential credential) => credential.Type switch
    {
        HttpCredentialType.None => null,
        HttpCredentialType.Bearer => $"Bearer {UnprotectOrEmpty(credential)}",
        HttpCredentialType.Basic => "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credential.Username}:{UnprotectOrEmpty(credential)}")),
        _ => null,
    };

    private string UnprotectOrEmpty(HttpCredential credential) =>
        credential.ProtectedSecret is null ? string.Empty : secretProtector.Unprotect(credential.ProtectedSecret);

    private static async Task<string> ReadCappedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[MaxBodyBytes];
        var totalRead = 0;
        int bytesRead;

        while (totalRead < buffer.Length
            && (bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)
                .ConfigureAwait(false)) > 0)
        {
            totalRead += bytesRead;
        }

        return Encoding.UTF8.GetString(buffer, 0, totalRead);
    }

    /// <summary>
    /// Status is evaluated first, the body check only when the status check passes — see
    /// plan 003's Decision 4 combination table.
    /// </summary>
    private static (bool Succeeded, string Detail) Evaluate(HttpProbeOptions options, HttpStatusCode statusCode, string body)
    {
        var actual = (int)statusCode;
        bool statusPassed;
        string statusFailureDetail;

        if (options.ExpectedStatusCode is { } expected)
        {
            var matches = actual == expected;
            statusPassed = options.ExpectedStatusCodeNegate ? !matches : matches;
            statusFailureDetail = $"HTTP {actual} (expected {(options.ExpectedStatusCodeNegate ? "not " : string.Empty)}{expected})";
        }
        else
        {
            statusPassed = actual is >= 200 and <= 299;
            statusFailureDetail = $"HTTP {actual}";
        }

        if (!statusPassed)
        {
            return (false, statusFailureDetail);
        }

        if (options.ExpectedBodyText is { Length: > 0 } text)
        {
            var contains = body.Contains(text, StringComparison.Ordinal);
            var bodyPassed = options.ExpectedBodyTextNegate ? !contains : contains;

            if (!bodyPassed)
            {
                var detail = options.ExpectedBodyTextNegate
                    ? $"body contained \"{text}\""
                    : $"body did not contain \"{text}\"";

                return (false, detail);
            }
        }

        return (true, $"HTTP {actual}");
    }
}
