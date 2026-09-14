using Homon.Api.Configuration;
using Homon.Infrastructure.Administration;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Homon.Api.Endpoints;

/// <summary>
/// <c>GET /api/v1/meta</c>: the API's own identification, polled by the SPA on load and
/// read anonymously by anything else that reaches the host.
/// </summary>
internal static class MetaEndpoints
{
    internal static RouteGroupBuilder MapMetaEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        parent.MapGet("/meta", GetMeta)
            .AllowAnonymous()
            .WithName("GetApiMetadata")
            .WithSummary("Identifies the API, the release it was built from, and how readers are admitted.");

        return parent;
    }

    private static Ok<ApiMetaResponse> GetMeta(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IOptions<AuthOptions> auth,
        AdministratorAuthenticator administrator) =>
        TypedResults.Ok(new ApiMetaResponse(
            Name: "Homon",
            ApiVersion: "v1",

            // The release this container was built from — `Homon:Version`, supplied in
            // production as the environment variable `Homon__Version` by compose.prod.yaml,
            // which reads it from HOMON_VERSION in the host's .env. deploy.sh pins that on
            // every deploy, so this is how you confirm which build is actually answering.
            // A *runtime* value, unlike the SPA's: the api image is version-agnostic, which
            // is what lets the migrator service run the same image. "dev" when unset.
            Release: configuration["Homon:Version"] is { Length: > 0 } version ? version : "dev",
            Environment: environment.EnvironmentName,
            RequireSignInForReaders: auth.Value.RequireSignInForReaders,

            // False on a fresh installation. The SPA uses it to explain why sign-in refuses
            // everyone, instead of letting the admin guess at a typo in their password.
            AdministratorConfigured: administrator.IsConfigured,
            Openapi: OpenApiDocumentation.Route));

    /// <summary>The body of <c>GET /api/v1/meta</c>.</summary>
    public sealed record ApiMetaResponse(
        string Name,
        string ApiVersion,
        string Release,
        string Environment,
        bool RequireSignInForReaders,
        bool AdministratorConfigured,
        string Openapi);
}
