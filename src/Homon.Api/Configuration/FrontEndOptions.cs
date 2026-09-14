using System.ComponentModel.DataAnnotations;

namespace Homon.Api.Configuration;

/// <summary>
/// How the API is reached from the browser. Homon serves the SPA and the API from
/// <b>one origin</b> — in development the Vite dev server proxies <c>/api</c>; in production
/// the SPA container's nginx does — so every request is same-origin: CORS never engages and
/// the session cookie is host-only.
/// </summary>
public sealed class FrontEndOptions
{
    public const string SectionName = "FrontEnd";

    /// <summary>
    /// Exact SPA origins permitted to call the API with credentials, for a deployment that
    /// puts the SPA on a second hostname. <b>Normally empty</b>: an empty array registers no
    /// CORS policy at all, which is the correct same-origin behaviour. Wildcards are not
    /// allowed — <c>AllowCredentials</c> and <c>AllowAnyOrigin</c> are mutually exclusive.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Absolute base URL of the SPA, used to build the links alert emails carry. It must
    /// point at the <i>front end</i>, not at this API. The development default is the Vite
    /// dev server; production supplies its own through <c>FrontEnd__PublicBaseUrl</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string PublicBaseUrl { get; set; } = "http://localhost:5300";
}
