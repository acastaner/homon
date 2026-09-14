using Homon.Api.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Homon.Api.Configuration;

/// <summary>
/// The document transformer that turns the generated route list into a specification
/// somebody who has never seen this repository can work from — the backup scripts' author,
/// for one.
/// </summary>
/// <remarks>
/// Served in every environment and anonymously: a caller who has not got a key yet is
/// exactly the caller who needs to read this to find out how to send one.
/// </remarks>
internal sealed class OpenApiDocumentation : IOpenApiDocumentTransformer
{
    /// <summary>Where the specification is served. Versioned with the API it describes.</summary>
    internal const string Route = "/api/v1/openapi.json";

    private const string SchemeName = "ApiKey";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Homon",
            Version = "v1",
            Description = Description,
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description =
                $"An API key minted by the administrator and spelled `{ApiKeyRules.Prefix}…`. "
                + "Send it as `Authorization: Bearer <key>` (or `X-Api-Key: <key>`). Keys are for "
                + "automations — the backup scripts — and cannot sign in as the administrator.",
        };

        // Declared once at the document level: every route either accepts the bearer
        // scheme or ignores it, and none refuses a request for carrying one.
        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
            },
        ];

        return Task.CompletedTask;
    }

    private const string Description = """
        Homon is a self-hosted home dashboard: the status of the household's services, links
        to them, a few explanation pages, the weather and the family calendar.

        **Reading.** Read endpoints are anonymous unless the administrator has switched
        `Auth:RequireSignInForReaders` on; `GET /api/v1/meta` says which, and is always
        anonymous.

        **Administering.** The administrator signs in with `POST /api/v1/auth/sign-in` and
        receives a session cookie. Every write endpoint requires that session.

        **Automations.** A script reports to Homon with an API key, minted by the
        administrator and sent as `Authorization: Bearer hmn_…`. A key can report — a backup
        run, say — and read; it can never administer.
        """;
}
