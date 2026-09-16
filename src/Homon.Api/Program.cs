using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Homon.Api;
using Homon.Api.Authentication;
using Homon.Api.Cli;
using Homon.Api.Configuration;
using Homon.Api.Endpoints;
using Homon.Infrastructure;
using Homon.Infrastructure.Administration;
using Homon.Infrastructure.Identity;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// ---------------------------------------------------------------------------------------
// Out-of-band command: mint a password hash for the administrator.
//
//   dotnet run --project src/Homon.Api -- hash-password
//
// The password is read from stdin, never from argv, so it does not reach shell history or a
// process listing. Paste the printed hash into the Administrator PasswordHash setting —
// spelled "Administrator:PasswordHash" in user secrets and appsettings, and
// "Administrator__PasswordHash" as an environment variable. Only the environment provider
// translates the double underscore; JSON does not.
// ---------------------------------------------------------------------------------------
if (args is ["hash-password", ..])
{
    await Console.Error.WriteAsync("Password: ");
    var password = await Console.In.ReadLineAsync();

    if (string.IsNullOrWhiteSpace(password))
    {
        await Console.Error.WriteLineAsync("No password supplied.");
        return 1;
    }

    Console.WriteLine(AdministratorAuthenticator.HashPassword(password));
    return 0;
}


// ---------------------------------------------------------------------------------------
// Out-of-band command: bring the database's schema up to date.
//
//   dotnet run --project src/Homon.Api -- migrate
//
// A verb rather than a startup call: applying migrations is a deliberate act with a lock and
// a failure mode, and doing it implicitly on every boot means every replica races for that
// lock. compose.prod.yaml runs this as a one-shot `migrator` service from the same image
// before the API starts.
// ---------------------------------------------------------------------------------------
if (args is ["migrate", ..])
{
    using var scope = CommandHost(args).Build().Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

    var pending = (await database.Database.GetPendingMigrationsAsync()).ToList();

    if (pending.Count == 0)
    {
        Console.WriteLine("schema      : already up to date; nothing to apply.");
        return 0;
    }

    Console.WriteLine($"pending     : {pending.Count}");

    foreach (var migration in pending)
    {
        Console.WriteLine($"            : {migration}");
    }

    await database.Database.MigrateAsync();

    Console.WriteLine("applied     : all of them.");
    return 0;
}


// ---------------------------------------------------------------------------------------
// Out-of-band command: mint an API key for an automation.
//
//   dotnet run --project src/Homon.Api -- create-api-key --name "clockmaster restic" --scope read-write
//
// Prints the key exactly once; only its digest is stored. Until the administrator's key page
// exists (Backups module), this is the only way to mint one. No authentication, as with every
// verb here: what gates it is possession of the connection string. `--scope` defaults to
// `read` when omitted; `--expires <YYYY-MM-DD>` defaults to never.
// ---------------------------------------------------------------------------------------
if (args is ["create-api-key", ..])
{
    var (keyArguments, keyError) = CreateApiKeyArguments.Parse(args);

    if (keyError is not null)
    {
        await Console.Error.WriteLineAsync(keyError);
        return 1;
    }

    using var scope = CommandHost(args).Build().Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    var (key, presented) = await new ApiKeyIssuer(database, timeProvider)
        .IssueAsync(keyArguments!.Name, keyArguments.Scope, keyArguments.ExpiresAt);

    await Console.Error.WriteLineAsync(
        $"""
        name        : {key.Name}
        token id    : {key.TokenId}
        scope       : {key.Scope}
        expires     : {(key.ExpiresAt is { } expires ? expires.ToString("u") : "never")}
        key         : shown once, below, and never again. Store it where the script reads it.
        """);
    Console.WriteLine(presented);
    return 0;
}


// ---------------------------------------------------------------------------------------
// A typo in a verb used to fall through every `if (args is [...])` block above and boot the
// web server against whatever connection string happened to be configured. `--urls` and
// other host arguments must still reach WebApplication.CreateBuilder(args) below, so only an
// unrecognised *verb* is refused here: the first argument, and only when it does not start
// with `-`.
// ---------------------------------------------------------------------------------------
if (args.Length > 0
    && !args[0].StartsWith('-')
    && args[0] is not ("hash-password" or "migrate" or "create-api-key"))
{
    await Console.Error.WriteLineAsync(
        $"""
        '{args[0]}' is not a command. Expected one of:
          hash-password
          migrate
          create-api-key
        """);
    return 1;
}


// Host.CreateApplicationBuilder reads DOTNET_ENVIRONMENT; WebApplication.CreateBuilder reads
// ASPNETCORE_ENVIRONMENT, which is what launchSettings.json and compose set. Left to itself a
// command therefore boots as Production on a developer machine, skips user secrets, and fails
// with an uninitialised connection string. Honour both, and add the secrets store explicitly.
static HostApplicationBuilder CommandHost(string[] args)
{
    var host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Development,
    });

    host.Configuration.AddUserSecrets(
        System.Reflection.Assembly.GetExecutingAssembly(), optional: true);

    // And then the environment again, because a later source wins. Without this line the
    // secrets store just added outranks ConnectionStrings__Homon, so a command told in as
    // many words which database to use silently operates on the developer's own — and
    // reports success. ci/run-ci.sh relies on this: `migrate` against the CI database must
    // not touch the development one.
    host.Configuration.AddEnvironmentVariables();

    // EF logs the probe of __EFMigrationsHistory on an empty database as a *failed command*
    // at Error level, even though it handles the failure itself and creates the table. On a
    // first `migrate` that is two red "fail:" blocks in a CI log for a run that succeeded.
    // The verbs print their own summary, so command-level logging adds nothing here.
    host.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);

    host.Services.AddHomonInfrastructure(host.Configuration);

    // Plan 003's Decision 3 assumed IDataProtectionProvider resolves unconditionally once
    // *the host* is built, because Homon.Api's Microsoft.NET.Sdk.Web project carries a
    // FrameworkReference to Microsoft.AspNetCore.App. True for WebApplication.CreateBuilder
    // below, but this command host is a plain Host.CreateApplicationBuilder — the generic
    // host does not pre-register Data Protection the way the web host does, so
    // ISecretProtector (consumed unconditionally by AddHomonMonitoring's HttpProbeRunner
    // registration) fails ServiceProvider validation the moment any command builds this
    // host, `migrate` included. Registered here, unconditionally, same posture as the web
    // host's own registration below — this is the plan's own documented STOP-condition
    // remedy (plan 003), not a workaround: it keeps every probe secret's key ring on the
    // same footing regardless of which host reads it, rather than skipping secret
    // protection for command-line verbs.
    host.Services.AddDataProtection();

    return host;
}

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "Homon.SinglePageApp";

// ---- Options ---------------------------------------------------------------------------

builder.Services.AddOptions<FrontEndOptions>()
    .Bind(builder.Configuration.GetSection(FrontEndOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddOptions<SignInThrottleOptions>()
    .Bind(builder.Configuration.GetSection(SignInThrottleOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddOptions<ReverseProxyOptions>()
    .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
    .ValidateOnStart();

var frontEnd = builder.Configuration
    .GetSection(FrontEndOptions.SectionName)
    .Get<FrontEndOptions>() ?? new FrontEndOptions();

// ---- Database, email, administrator ----------------------------------------------------

builder.Services.AddHomonInfrastructure(builder.Configuration, builder.Environment.IsProduction());

// ---- Data protection ---------------------------------------------------------------------
// Session cookies and probe secrets are protected by this key ring. Left on the default it
// lives under the host user's home directory — fine on a developer machine, ephemeral inside
// a container, where replacing the API would invalidate every signed-in session and every
// stored secret. Configured only when a path is supplied, so tests and `dotnet run` keep the
// default and write nothing into the repository.

// Unconditional — see CommandHost's own AddDataProtection() call above for why this cannot
// stay inside the branch below (plan 003's Decision 3 assumed it could; `migrate` proved
// otherwise). The branch below only ever adds *where* keys persist, never *whether* the
// provider exists.
builder.Services.AddDataProtection();

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];

if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("Homon");
}

// ---- Identity --------------------------------------------------------------------------
// AddIdentityCore rather than AddIdentity: no cookie scheme is registered implicitly — the
// schemes below are configured deliberately, in one place. Roles are on, because the
// Administrator role is how a database-backed account will be told apart from a reader.

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 8;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<HomonDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Two ways in, one default. The policy scheme looks at what the request actually carries
// and forwards to the API key handler or to the cookie — so no endpoint, filter or policy
// has to know which it got.
builder.Services.AddAuthentication(HomonAuthenticationSchemes.Default)
    .AddPolicyScheme(
        HomonAuthenticationSchemes.Default,
        displayName: null,
        options => options.ForwardDefaultSelector = context =>
            ApiKeyAuthenticationHandler.Carries(context)
                ? HomonAuthenticationSchemes.ApiKey
                : IdentityConstants.ApplicationScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        HomonAuthenticationSchemes.ApiKey,
        displayName: null,
        _ => { })
    .AddIdentityCookies();

builder.Services.Configure<CookieAuthenticationOptions>(
    IdentityConstants.ApplicationScheme,
    options =>
    {
        options.Cookie.Name = "homon.sid";
        options.Cookie.HttpOnly = true;

        // The SPA and the API are served from one origin, so the cookie is first-party and
        // Lax is sufficient — and Lax, not Strict, so a link into the admin pages from an
        // alert email still arrives signed in.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // Eight hours, absolute, not extended by activity — an administrator's working day.
        // "Keep me signed in" opts into a longer, persistent cookie instead; the sign-in
        // endpoint sets that ticket's ExpiresUtc explicitly.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;

        // Identity's application cookie re-resolves every principal against the user store
        // through SecurityStampValidator once ValidationInterval has elapsed. The bootstrap
        // administrator has no user row *and* its subject id is not a Guid, so that lookup
        // throws FormatException rather than returning null — a 500 on every request from a
        // session older than the interval. Skip the check for that one subject; every other
        // principal takes the normal path.
        options.Events.OnValidatePrincipal = context =>
            context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                == AdministratorAuthenticator.SubjectId
                ? Task.CompletedTask
                : SecurityStampValidator.ValidatePrincipalAsync(context);

        // This is an API. Answer with status codes; never redirect to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// ---- Authorisation ---------------------------------------------------------------------

builder.Services.AddSingleton<IAuthorizationHandler, ReaderHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddHomonPolicies();

// ---- CORS ------------------------------------------------------------------------------
// Same-origin in every intended deployment, so this registers nothing unless a deployment
// names a second origin. In development the Vite proxy makes the app same-origin too.

builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (frontEnd.AllowedOrigins.Length is 0)
        {
            return;
        }

        policy.WithOrigins(frontEnd.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    }));

// ---- Rate limiting ----------------------------------------------------------------------
// Identity's lockout cannot protect the bootstrap administrator — it has no user row to
// count failures against — so the sign-in endpoint is rate limited instead.

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(AuthenticationEndpoints.SignInThrottlePolicy, httpContext =>
    {
        // Read live from DI rather than capturing builder.Configuration into a local here:
        // this block runs before builder.Build(), so a snapshot taken at this point predates
        // any configuration WebApplicationFactory splices in for tests — every test host
        // would silently fall back to the appsettings.json default regardless of its own
        // override. IOptions<T> is resolved from the built container, after that splice.
        var throttle = httpContext.RequestServices
            .GetRequiredService<IOptions<SignInThrottleOptions>>().Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            $"sign-in:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = throttle.PermitLimit,
                Window = TimeSpan.FromMinutes(throttle.WindowMinutes),
                QueueLimit = 0,
            });
    });
});

// ---- Web API ---------------------------------------------------------------------------

// Enums travel as their names. `kind: "administrator"` is what the SPA switches on, and a
// number there would be a second copy of the enum to keep in step.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

// The problem body's traceId, the X-Request-Id response header (below) and the JSON log
// scope all carry the same bare 32-hex TraceId, so a report is one grep away from its log
// line. CustomizeProblemDetails runs after the writer's own assignment, so this wins.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OpenApiDocumentation>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<HomonDbContext>("database");

// The dashboard's status payload grows with the number of probes and their history; the
// default MIME list (text/*, application/json, …) covers every endpoint here.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

// First in the pipeline, before even the exception handler, so every downstream component
// sees the real client address and scheme rather than the proxy's.
//
// KnownIPNetworks is cleared and set explicitly rather than left at its default (loopback
// only), because the peer this container sees inside the Compose network is the SPA
// container's address, not 127.0.0.1. ForwardLimit = 1 takes only the rightmost entry, which
// is correct against nginx's $proxy_add_x_forwarded_for: that directive appends to whatever
// the client already sent, so trusting more than one hop would trust the client. With
// ReverseProxy:KnownNetwork unset — development, tests, CI — this block adds nothing.
var reverseProxy = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;

if (!string.IsNullOrWhiteSpace(reverseProxy.KnownNetwork))
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    forwardedHeaders.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(reverseProxy.KnownNetwork));
    app.UseForwardedHeaders(forwardedHeaders);
}

app.UseExceptionHandler();
app.UseResponseCompression();
app.UseStatusCodePages();

// Registered via OnStarting rather than set directly, so the headers survive
// UseExceptionHandler's response reset on an unhandled exception and UseStatusCodePages'
// rewrite of an error response — both run upstream of this middleware, and OnStarting fires
// right before the response is actually sent, after either has had its say. No CSP here (this
// API serves JSON, not documents — the SPA's nginx carries one) and no HSTS (TLS terminates
// at the reverse proxy in front of the stack).
//
// The same callback stamps X-Request-Id: the bare 32-hex TraceId the problem body and the
// JSON log scope carry. Mint-only: an inbound header of the same name is ignored rather than
// echoed, because echoing a caller's value would break that three-way equality.
app.Use(async (context, next) =>
{
    var requestId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Request-Id"] = requestId;
        return Task.CompletedTask;
    });

    await next(context);
});

// Served in every environment, at a versioned path under /api/v1 rather than the package's
// default /openapi/v1.json, and anonymously: the specification is the contract for the
// scripts that report to this API, and their author has no source access.
app.MapOpenApi(OpenApiDocumentation.Route).AllowAnonymous();

app.UseCors(CorsPolicyName);

app.UseRateLimiter();
app.UseAuthentication();

// Immediately after authentication, and before anything can answer: a key that failed to
// authenticate must be refused rather than quietly demoted to an anonymous caller — see the
// middleware's remarks.
app.UseMiddleware<ApiKeyRefusalMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

// Every resource endpoint hangs off this group. Versioning is in the path so a future
// /api/v2 can be served alongside v1 rather than replacing it.
var v1 = app.MapGroup("/api/v1");

v1.MapMetaEndpoints();
v1.MapAuthenticationEndpoints();
v1.MapProbeEndpoints();
v1.MapProbeGroupEndpoints();
v1.MapStatusEndpoints();
v1.MapLinkEndpoints();

// Module endpoints register here as each module lands — see docs/MODULES.md:
v1.MapPageEndpoints();
//   v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();
v1.MapWeatherEndpoints();
//   v1.MapCalendarEndpoints();

using (var startup = app.Services.CreateScope())
{
    var administrator = startup.ServiceProvider.GetRequiredService<AdministratorAuthenticator>();

    if (!administrator.IsConfigured)
    {
        StartupLog.NoAdministratorConfigured(app.Logger);
    }
}

await app.RunAsync();

return 0;

/// <summary>
/// Named so Homon.Api.Tests can drive this host through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
