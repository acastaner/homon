# Plan 015: Let an administrator sign in over plain HTTP on a LAN-first deployment

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If anything under "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`
> (Step 9).
>
> **Drift check (run first)**:
> ```bash
> git diff --stat 5ff263a..HEAD -- \
>   src/Homon.Api/Program.cs src/Homon.Api/Configuration/AuthOptions.cs \
>   src/Homon.Web/nginx.conf compose.prod.yaml .env.example \
>   tests/Homon.Api.Tests/HomonApiFactory.cs tests/Homon.Api.Tests/AuthenticationEndpointTests.cs \
>   tests/Homon.Api.Tests/TestClient.cs tests/Homon.Api.Tests/SignInThrottleTests.cs
> ```
> Empty output means no drift. If any of those files changed, compare the "Current state"
> excerpts below against the live code before proceeding; on a mismatch, treat it as a STOP
> condition.

## Status

- **Priority**: P1 — this is a live production defect; the maintainer cannot administer the
  running deployment at all except through an SSH tunnel.
- **Effort**: M
- **Risk**: MEDIUM. Touches the session cookie's security attribute and the production reverse
  proxy. `src/Homon.Web/nginx.conf` **is not exercised by any suite** (see "What the gate does
  not cover"), so its change is verified by hand.
- **Depends on**: none
- **Category**: bug / security
- **Planned at**: commit `5ff263a`, 2026-09-18
- **Reviewed**: 2026-09-18 (review-plan, against `5ff263a`). Every excerpt below was
  re-read from the live files; three line numbers were corrected, a third test was added
  (Step 6) to cover D5, and the dependency on `ReverseProxy:KnownNetwork` was made explicit
  in D1/D5 and in the post-deploy check.

## Why this matters

Homon was deployed to its first real host on 2026-09-18 (`clockmaster`, rootless Docker, port
8102, reached on the LAN as `http://homon.lan.acastaner.fr:8102`). **The administrator cannot
sign in.** The form reports no error; the page simply returns to "This page changes what the
household sees. Sign in to continue."

It is not a credentials bug. Observed against the live deployment:

- a wrong password returns `401` with the RFC 9457 body `"The email address or password is
  incorrect."` — so the endpoint, the hash and the bootstrap administrator all work;
- `GET /api/v1/meta` reports `"environment":"Production"` and `"administratorConfigured":true`;
- the correct password returns `204` with `Set-Cookie: homon.sid=…; secure`.

That `secure` attribute is the whole defect. A browser **silently discards** a `Secure` cookie
delivered over a plain-HTTP origin — `localhost` is the only exemption. The sign-in succeeds,
the cookie never lands, the next request is anonymous, and the SPA shows the signed-out prompt
again. Nothing is logged because nothing failed.

Because `Auth:RequireSignInForReaders` defaults to `false`, the dashboard itself renders fine
for an anonymous reader — which is what makes this look like a login bug rather than a cookie
bug, and why it survived to production.

This contradicts the deployment shape the project documents for itself. `compose.prod.yaml`'s
own header describes what sits in front as "a reverse proxy with the household's trusted-network
rules, a WAF, **nothing on a LAN**", and `README.md` sells a self-hosted household dashboard.
For the "nothing on a LAN" case there is currently **no URL at which an administrator can hold a
session**.

### Why this is two changes and not one

The obvious one-line fix — relax `CookieSecurePolicy` — is wrong on its own, and would quietly
weaken the deployment that *does* use TLS.

`src/Homon.Web/nginx.conf:54` sets `proxy_set_header X-Forwarded-Proto $scheme`. That nginx
listens on plain `:80` (line 21), so `$scheme` is **always** `http` and the directive
**overwrites** whatever a WAF in front sent. The API therefore believes every request arrived
over HTTP, including the ones where TLS terminated at the WAF. Switch the cookie policy to
`SameAsRequest` without fixing that, and the cookie loses `Secure` on the internet-facing path
too — the one place it genuinely matters.

So nginx must learn to honour a forwarded scheme **first**; only then can the cookie policy
safely follow the request's real scheme.

## Current state

### The files that matter

- `src/Homon.Api/Program.cs` — the `Configure<CookieAuthenticationOptions>` block (the defect),
  the options registrations, and the forwarded-headers block after `builder.Build()`.
- `src/Homon.Api/Configuration/AuthOptions.cs` — the `Auth` section; today it carries exactly
  one property.
- `src/Homon.Web/nginx.conf` — the production proxy; the `$homon_cache` map is the exemplar for
  where a `map` belongs in this file.
- `compose.prod.yaml`, `.env.example` — how a production knob is surfaced.
- `tests/Homon.Api.Tests/HomonApiFactory.cs` — the database-less factory. **It forces
  `Environments.Development`**, which is why no existing test exercises the Production cookie
  path.
- `tests/Homon.Api.Tests/SignInThrottleTests.cs` — the exemplar for a test that needs its own
  factory with different configuration.
- `tests/Homon.Api.Tests/AuthenticationEndpointTests.cs` — the exemplar for asserting on
  `Set-Cookie`.
- `tests/Homon.Api.Tests/TestClient.cs` — the helpers Step 6's tests call. **Read it, do not
  change it**; it is excerpted below because Step 6 uses it without introducing it.
- `compose.prod.yaml:120` — `ReverseProxy__KnownNetwork`, which decides whether the API honours
  `X-Forwarded-Proto` at all. D1 and D5 depend on it; see the excerpt below.

### Excerpts, as they exist at `5ff263a`

`src/Homon.Api/Program.cs:293-306` — the defect is the last three lines:

```csharp
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
```

`src/Homon.Api/Configuration/AuthOptions.cs` — the whole class today:

```csharp
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When true, read endpoints refuse anonymous requests (401). When false — the default
    /// — anyone who can reach the API can read it. <c>GET /api/v1/meta</c> and
    /// <c>/health</c> are anonymous either way.
    /// </summary>
    public bool RequireSignInForReaders { get; set; }
}
```

`src/Homon.Api/Program.cs:211-213` — how `AuthOptions` is registered:

```csharp
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();
```

`src/Homon.Api/Program.cs:373-379` — **the pattern this plan must follow**, quoted because it is
the repo's standing rule (`CLAUDE.md`: "Configuration read before `builder.Build()` misses test
overrides. Resolve `IOptions<T>` from the built container"):

```csharp
        // Read live from DI rather than capturing builder.Configuration into a local here:
        // this block runs before builder.Build(), so a snapshot taken at this point predates
        // any configuration WebApplicationFactory splices in for tests — every test host
        // would silently fall back to the appsettings.json default regardless of its own
        // override. IOptions<T> is resolved from the built container, after that splice.
        var throttle = httpContext.RequestServices
            .GetRequiredService<IOptions<SignInThrottleOptions>>().Value;
```

`src/Homon.Web/nginx.conf:15-18` — the existing map, and the exemplar for where a new one goes:

```nginx
map $uri $homon_cache {
    default      "no-cache";
    ~^/assets/   "public, max-age=31536000, immutable";
}
```

`src/Homon.Web/nginx.conf:44-58` — the comment and the location to change:

```nginx
    # X-Forwarded-For / -Proto are what the API's forwarded-headers middleware reads when
    # ReverseProxy__KnownNetwork names this container's network — that is how the sign-in
    # rate limiter partitions on the real client instead of on this proxy's address.
    # $proxy_add_x_forwarded_for APPENDS to whatever the client sent, which is why the API
    # trusts exactly one hop and takes the rightmost entry.
    location /api/ {
        proxy_pass http://api:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 60s;
        client_max_body_size 8m;
    }
```

`compose.prod.yaml:96` — where the new environment line goes (directly under this one):

```yaml
      Auth__RequireSignInForReaders: ${HOMON_REQUIRE_SIGN_IN_FOR_READERS:-false}
```

`tests/Homon.Api.Tests/HomonApiFactory.cs:36` — the line that makes a Production test need its
own factory:

```csharp
        builder.UseEnvironment(Environments.Development);
```

`tests/Homon.Api.Tests/TestClient.cs` — the two helpers Step 6 calls. Note the client's base
address comes from `WebApplicationFactoryClientOptions` and is `http://localhost`, and that
`SignInAsync` posts a **relative** URI, so re-pointing `client.BaseAddress` changes the scheme
the host sees (Step 6's third test relies on exactly that):

```csharp
    public static HttpClient Create(HomonApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    public static Task<HttpResponseMessage> SignInAsync(
        this HttpClient client,
        string email = HomonApiFactory.AdministratorEmail,
        string password = HomonApiFactory.AdministratorPassword,
        bool keepSignedIn = false) =>
        client.PostAsJsonAsync("/api/v1/auth/sign-in", new { email, password, keepSignedIn });
```

`compose.prod.yaml:120` — the setting that switches the forwarded-headers block on. Without it
the API never reads `X-Forwarded-Proto` and every request looks like plain HTTP to the cookie
policy, whatever nginx sends:

```yaml
      ReverseProxy__KnownNetwork: ${HOMON_PROXY_NETWORK:-172.16.0.0/12}
```

### What the gate does not cover

**`nginx.conf` is never executed by any suite.** `src/Homon.Web/playwright.config.ts` runs the
e2e suite against `vite build && vite preview`, and the preview server proxies `/api` itself
(`vite.config.ts`). Its own header says so: driving the dev server "says nothing about what
nginx will hand a reader." Step 1's change therefore has **no automated test** — it is verified
by `nginx -t` inside the built image and by a manual header check (Step 1's verification, and
"Post-deploy verification" at the end). Do not invent an nginx test harness for this plan.

## Decisions — implement these exactly; the reasoning is the point

**D1 — nginx honours an upstream `X-Forwarded-Proto`, via a `map`, with an explicit allow-list.**
Only the literal values `http` and `https` are passed through; **anything else falls back to
`$scheme`**. A bare `default $http_x_forwarded_proto` would forward a comma-joined list (`https,
http`) or junk straight into `System.Net.IPNetwork`-adjacent parsing downstream; the allow-list
makes the failure mode "behaves as today" rather than "undefined". The map goes next to
`$homon_cache` — **`http` context, outside the `server { }` block**, which is where
`$homon_cache` already sits and the only context in which nginx accepts `map` at all. Put it
inside `server { }` or inside a `location` and `nginx -t` fails with *"map directive is not
allowed here"*.

**Precondition, and it is load-bearing**: the API only reads `X-Forwarded-Proto` when
`ReverseProxy:KnownNetwork` is set and matches the network nginx reaches it from
(`Program.cs:430-443`; `compose.prod.yaml:120` defaults it to `172.16.0.0/12`). If that value is
wrong for the host, the forwarded-headers middleware never runs, `Request.Scheme` is `http` for
*every* request, and Step 1's map achieves nothing. Do not change that setting in this plan —
but the post-deploy check at the end is what proves it is right.

**D2 — do not key the decision on the client's source address.** The tempting hardening is "only
trust `X-Forwarded-Proto` from the WAF's IP". It does not work on the target host: under
**rootless Docker the published port is SNATed**, so `$remote_addr` inside the web container is
the same proxy address for a LAN client and for the WAF alike. There is nothing to
discriminate on. The residual spoof is acceptable and self-inflicted: a LAN client that sends
`X-Forwarded-Proto: https` over plain HTTP causes the API to mark *its own* cookie `Secure`, and
its own browser then refuses to store it. It gains that client nothing and affects nobody else.
The reverse — a WAN client stripping `Secure` — is not reachable, because the WAF sets the
header itself and replaces whatever the client sent.

**D3 — the cookie policy becomes `SameAsRequest` only when explicitly opted in.** New flag
`Auth:AllowPlainTextSessions`, **default `false`**. Secure-by-default is preserved: a host that
does nothing keeps today's behaviour exactly. Development keeps `SameAsRequest` unconditionally,
as now.

**D4 — read the flag from `IOptions<AuthOptions>` resolved from the built container, never from
`builder.Configuration`.** This is `CLAUDE.md`'s standing rule and the reason Step 3 uses
`AddOptions<CookieAuthenticationOptions>(…).Configure<IOptions<AuthOptions>>(…)` rather than
adding a line inside the existing delegate. Getting this wrong produces a change that works in
production and is untestable — the new tests in Step 6 would pass or fail for the wrong reason.

**D5 — `SameAsRequest`, not "never `Secure`".** The flag must not hard-code
`CookieSecurePolicy.None`. With D1 in place, `SameAsRequest` is correct on *both* paths from one
setting: the WAF-fronted request is seen as HTTPS and still gets a `Secure` cookie, while the
LAN request does not. A blanket "off" would give up the WAN path's protection to fix the LAN's.

The WAN half of that sentence is true only while `ReverseProxy:KnownNetwork` matches (D1's
precondition) — that is why Step 6 adds a third test pinning `SameAsRequest`'s HTTPS behaviour
in-process, where no proxy is involved, and why the post-deploy check exercises the WAN path
against the real deployment.

**D6 — leave `ForwardLimit = 1` and the `X-Forwarded-For` handling alone.** The forwarded-headers
block at `Program.cs:430-443` is correct as written and this plan does not touch it. Changing
`ForwardLimit` to accommodate D1 would start trusting a client-supplied `X-Forwarded-For`.

**D7 — this plan does not add TLS to the LAN name.** That is the maintainer's follow-up
(recorded in Maintenance notes). The flag is designed to be set back to `false` once it exists,
with no other change.

## Commands you will need

```bash
./ci/run-ci.sh api        # dotnet build (Release = the formatting gate), migrate, dotnet test
./ci/run-ci.sh web        # npm ci, lint, build (= typecheck), vitest
./ci/run-ci.sh            # everything; run before calling this plan done
```

`./ci/run-ci.sh api` fails on a skipped test, so `HOMON_TEST_CONNECTION` must be set as usual for
the `[DatabaseFact]` tests. The tests this plan adds are **not** `[DatabaseFact]` — they use the
database-less factory.

## Scope

**In scope** — exactly these files:

- `src/Homon.Web/nginx.conf`
- `src/Homon.Api/Configuration/AuthOptions.cs`
- `src/Homon.Api/Program.cs` (the cookie options registration only)
- `compose.prod.yaml`
- `.env.example`
- `tests/Homon.Api.Tests/PlainTextSessionTests.cs` (new)
- `docs/ARCHITECTURE.md`, `docs/deployment-runbook.md`, `plans/README.md`

**Explicitly out of scope** — do not touch, even if it looks related:

- `src/Homon.Api/Program.cs`'s forwarded-headers block (D6), the rate limiter, and every other
  part of the cookie delegate (`Name`, `HttpOnly`, `SameSite`, `ExpireTimeSpan`,
  `OnValidatePrincipal`, `OnRedirectToLogin`).
- `src/Homon.Api/Endpoints/AuthenticationEndpoints.cs` — the sign-in endpoint is correct; it
  already returns 204 and sets the cookie.
- `tests/Homon.Api.Tests/HomonApiFactory.cs` — **do not change its `UseEnvironment`**. Other
  tests depend on Development. Derive a new factory instead (Step 6).
- `tests/Homon.Api.Tests/TestClient.cs` — read it, do not change it. Step 6's third test needs a
  client on an `https` base address; it sets `client.BaseAddress` on the returned client rather
  than adding a parameter here, because every other test in the suite uses these helpers.
- Anything under `src/Homon.Web/src/` — there is no SPA change in this plan. The sign-in form is
  not at fault.
- `src/Homon.Domain/`, `src/Homon.Infrastructure/`, any migration.

## Git workflow

Work on a branch, as every prior plan did:

```bash
git switch -c plan/015-lan-first-sign-in
```

Commit per step with the repo's message style (`Auth: …`, `Web: …`, `Plans: …`, each naming
`(plan 015)`). Do not merge to `main` and do not push — merging is the maintainer's call.

## Steps

### Step 1: Teach nginx to honour a forwarded scheme

In `src/Homon.Web/nginx.conf`, add a second `map` immediately after the `$homon_cache` map
(after line 18, so **before** `server {` on line 20 — `map` is only legal in the `http` context,
and this file is included from inside `http { }`), with this comment — the reasoning is the
point (`CLAUDE.md`: "a bare setting is a setting somebody will 'simplify' back"):

```nginx
# The scheme the CLIENT used, which is not necessarily the scheme this server was reached
# with. Behind a WAF, TLS terminates there and this nginx is reached over plain :80, so
# $scheme reads "http" for a request the browser made over HTTPS — and the API, reading
# X-Forwarded-Proto, would mark the session cookie non-Secure on exactly the deployment
# where Secure matters most. Honour an upstream value when there is one, fall back to
# $scheme when there is not (the LAN case, where the fallback is the correct answer).
#
# Only the two literal values are passed through: a comma-joined list from a double proxy,
# or anything unexpected, falls back to $scheme rather than travelling on uninspected.
#
# Deliberately NOT keyed on $remote_addr. Under rootless Docker the published port is
# SNATed, so a LAN client and the WAF arrive from the same address inside this container —
# there is nothing to discriminate on. A client CAN therefore claim "https" over plain HTTP;
# the only effect is that the API marks that client's own cookie Secure and its own browser
# then refuses to store it. Stripping Secure on the WAF path is not reachable, because the
# WAF sets this header itself and replaces whatever the client sent.
map $http_x_forwarded_proto $homon_forwarded_proto {
    default  $scheme;
    "http"   "http";
    "https"  "https";
}
```

Then change the one directive inside `location /api/`:

```diff
-        proxy_set_header X-Forwarded-Proto $scheme;
+        proxy_set_header X-Forwarded-Proto $homon_forwarded_proto;
```

Leave `location = /api/health` alone — it proxies a health check that has no session.

**Verification** — the config must still parse. There is no nginx on the build host, so use the
image the production stack uses:

```bash
docker run --rm --name homon-ci-nginx-check \
  -v "$PWD/src/Homon.Web/nginx.conf:/etc/nginx/conf.d/default.conf:ro" \
  nginx:alpine nginx -t
```

Expected: `syntax is ok` and `test is successful`.

**`--name homon-ci-nginx-check` is not decoration and must not be dropped.** This repo installs
`ci/guard-docker.py` as a PreToolUse hook on Bash (`.claude/settings.json`); it refuses any
`docker` command containing a disturbing verb unless the command names the `homon-ci` Compose
project, and `--rm` matches that list. Without the name the command is blocked outright, with a
message about containers belonging to other projects — that is the guard working, not a
permissions glitch, and the answer is the name, never a workaround.

Mounting the file at `conf.d/default.conf` is also what makes the new `map` legal: `conf.d/*` is
included from inside nginx's `http { }` block, which is the context `map` requires.

If Docker is unavailable in your environment, say so in your report and move on — Step 1 is then
verified by the maintainer at deploy time.

### Step 2: Add the flag to `AuthOptions`

In `src/Homon.Api/Configuration/AuthOptions.cs`, add a second property below
`RequireSignInForReaders`, matching the existing doc-comment style:

```csharp
    /// <summary>
    /// When true, the session cookie is marked <c>Secure</c> only for requests that actually
    /// arrived over HTTPS, instead of always. When false — the default — it is always marked
    /// <c>Secure</c> outside Development.
    /// </summary>
    /// <remarks>
    /// Set this for a LAN-first deployment reached over plain HTTP, where an always-Secure
    /// cookie is discarded by the browser outright and no administrator can hold a session.
    /// It is only safe in combination with a front end that forwards the client's real scheme:
    /// <c>src/Homon.Web/nginx.conf</c> passes an upstream <c>X-Forwarded-Proto</c> through, so a
    /// WAF-fronted request is still seen as HTTPS and still receives a <c>Secure</c> cookie.
    /// Set it back to false once the plain-HTTP origin gains TLS; nothing else has to change.
    /// </remarks>
    public bool AllowPlainTextSessions { get; set; }
```

**Verification**: `./ci/run-ci.sh api` — expect a green build (Release is the formatting gate;
`TreatWarningsAsErrors` and `IDE0055` are live).

### Step 3: Make the cookie policy follow the request's scheme when opted in

In `src/Homon.Api/Program.cs`, **remove** the `SecurePolicy` assignment from the existing
delegate:

```diff
         options.Cookie.SameSite = SameSiteMode.Lax;
-        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
-            ? CookieSecurePolicy.SameAsRequest
-            : CookieSecurePolicy.Always;
```

Then, **immediately after that whole `builder.Services.Configure<CookieAuthenticationOptions>(…)`
statement closes**, add a separate registration. It is separate precisely because it needs DI:

```csharp
// SecurePolicy is configured apart from the block above because it depends on AuthOptions, and
// AuthOptions must be resolved from the built container: a value read from builder.Configuration
// here would predate the configuration a WebApplicationFactory splices in, so every test host
// would silently see the appsettings default instead of its own override. Same rule as the
// sign-in rate limiter below.
//
// SameAsRequest, not None: with nginx forwarding the client's real scheme
// (src/Homon.Web/nginx.conf), one setting is correct on both paths — a WAF-fronted request is
// seen as HTTPS and still gets a Secure cookie, while a plain-HTTP LAN request gets one the
// browser will actually store. A blanket "never Secure" would give the WAN path away to fix the
// LAN's.
builder.Services
    .AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<IOptions<AuthOptions>>((options, auth) =>
        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment() || auth.Value.AllowPlainTextSessions
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always);
```

`Microsoft.Extensions.Options` is already imported in this file (the rate limiter uses
`IOptions<T>`); add a `using` only if the build says otherwise.

**Verification**:

```bash
./ci/run-ci.sh api
```

Expect green, and expect **every existing test in `AuthenticationEndpointTests` to still pass** —
they run in Development, where behaviour is unchanged.

### Step 4: Surface the knob in `compose.prod.yaml`

Add directly beneath the `Auth__RequireSignInForReaders` line (`compose.prod.yaml:96`):

```yaml
      # A LAN-first deployment is reached over plain HTTP, where an always-Secure session
      # cookie is discarded by the browser and nobody can sign in. "true" marks the cookie
      # Secure only for requests that genuinely arrived over HTTPS — so a WAF-fronted origin
      # keeps its Secure cookie while the LAN one works at all. Set it back to false once the
      # plain-HTTP origin gains TLS. The web container's nginx forwards the client's real
      # scheme, which is what makes one setting correct on both paths.
      Auth__AllowPlainTextSessions: ${HOMON_ALLOW_PLAINTEXT_SESSIONS:-false}
```

Note the `:-false` default, not `:?` — this variable is optional, and an existing `.env` that
predates it must keep working unchanged.

### Step 5: Document it in `.env.example`

Add after the `HOMON_REQUIRE_SIGN_IN_FOR_READERS` block (`.env.example:48`):

```bash
# Whether the session cookie may be issued over plain HTTP. `false` — the default — always
# marks it Secure, which a browser will only store on an HTTPS origin. Set it to `true` for a
# LAN-first deployment reached as http://<name>:8102, where an always-Secure cookie means the
# sign-in form appears to do nothing: it succeeds, the browser discards the cookie, and the
# next request is anonymous again. It does NOT weaken a TLS origin — the web container forwards
# the client's real scheme, so a WAF-fronted request still gets a Secure cookie. Set it back to
# false once the origin gains TLS.
HOMON_ALLOW_PLAINTEXT_SESSIONS=false
```

### Step 6: The tests

Create `tests/Homon.Api.Tests/PlainTextSessionTests.cs`. Follow `SignInThrottleTests` for the
derived-factory shape and `AuthenticationEndpointTests` for the `Set-Cookie` assertions.

**The trap this step exists to navigate**: `HomonApiFactory` forces
`Environments.Development`, so no current test can observe the Production cookie policy. You must
override the environment — and a Production host **runs two extra validators that a Development
one does not**, both reached through `AddHomonInfrastructure(configuration,
builder.Environment.IsProduction())`:

- `Email:ResendApiToken` must be non-empty, or startup fails with *"Email:ResendApiToken is not
  configured…"*. `HomonApiFactory` sets it to `null`, so the derived factory **must** set one.
- `Weather:Provider` must not be `Fake`. It defaults to `OpenMeteo`, so nothing to do — but do
  not set it to `Fake`.

A dummy token is enough: nothing sends mail in these tests, and the sender is resolved lazily.

```csharp
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Homon.Api.Tests;

/// <summary>
/// The session cookie's Secure attribute, which decides whether a LAN-first deployment can sign
/// in at all. Production is the interesting environment here, and <see cref="HomonApiFactory"/>
/// deliberately runs as Development — so both factories below override it, and supply the one
/// extra setting a Production host validates on start.
/// </summary>
public class PlainTextSessionTests
{
    [Fact]
    public async Task Production_marks_the_session_cookie_secure_by_default()
    {
        using var factory = new ProductionFactory();
        using var client = TestClient.Create(factory);

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.Contains("secure", SessionCookie(signIn), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allowing_plain_text_sessions_drops_secure_for_a_plain_http_request()
    {
        using var factory = new PlainTextProductionFactory();
        using var client = TestClient.Create(factory);

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.DoesNotContain("secure", SessionCookie(signIn), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allowing_plain_text_sessions_keeps_secure_for_an_https_request()
    {
        using var factory = new PlainTextProductionFactory();
        using var client = TestClient.Create(factory);

        // SameAsRequest, not None (D5): the flag must not cost a TLS origin its Secure cookie.
        // The in-process host takes Request.Scheme straight from the request URI, so an https
        // base address is the same signal the forwarded-headers middleware produces in
        // production when nginx passes X-Forwarded-Proto: https through. SignInAsync posts a
        // relative URI, so this is all it takes.
        client.BaseAddress = new Uri("https://localhost");

        var signIn = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.Contains("secure", SessionCookie(signIn), StringComparison.OrdinalIgnoreCase);
    }

    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("homon.sid=", StringComparison.Ordinal));

    /// <summary>
    /// Production, with the settings a Production host validates on start that
    /// <see cref="HomonApiFactory"/> leaves unset because Development does not check them.
    /// </summary>
    private class ProductionFactory : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(Environments.Production);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // A Production host refuses to start without one rather than silently
                    // logging alerts instead of sending them. Never used: nothing here sends.
                    ["Email:ResendApiToken"] = "re_test_token",
                }));
        }
    }

    private sealed class PlainTextProductionFactory : ProductionFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:AllowPlainTextSessions"] = "true",
                }));
        }
    }
}
```

The `TestClient` reaches the in-process host over `http://localhost`, so the request scheme is
`http` — which is exactly the condition under test.

**Verification**:

```bash
./ci/run-ci.sh api
```

All three new tests must pass. **Test 1 failing means Step 3 did not take effect** (the flag is
being read from the wrong place, or the old `SecurePolicy` line was left in and is overwriting
it); **test 2 failing while test 1 passes means D4 was not followed** — the configuration
override is not reaching the options, which is the exact failure `CLAUDE.md` warns about;
**test 3 failing while 1 and 2 pass means the policy was hard-coded to `CookieSecurePolicy.None`
instead of `SameAsRequest`**, which D5 forbids — that variant fixes the LAN by giving away the
WAN path's cookie protection.

### Step 7: Record the decision in `docs/ARCHITECTURE.md`

Sections are claimed at execution time. Find the highest and take the next:

```bash
grep -o '^### 3\.[0-9]*' docs/ARCHITECTURE.md | sort -t. -k2 -n | tail -1
```

At `5ff263a` the highest is `### 3.20`, so this plan takes **`### 3.21`** — confirm before
writing. Add a section titled for the decision, recording: that the cookie's `Secure` attribute
follows the request scheme when `Auth:AllowPlainTextSessions` is set; that nginx forwards the
client's real scheme with an allow-list map, which is what makes one flag correct on both a
WAF-fronted and a plain-HTTP origin; that source-IP keying was rejected because rootless Docker
SNATs the published port; and that the default is `false`, so a host that does nothing keeps
today's behaviour. Record the **accepted residual risk** too, in one sentence: with the flag on,
the session cookie travels in clear on that network, so anyone who can sniff the LAN segment can
replay the session — accepted because the alternative is no administration at all, and retired
by putting TLS on the origin. Match the prose style of the surrounding sections.

### Step 8: Update the deployment runbook

In `docs/deployment-runbook.md` the headings are `## The maintainer's host` (line 14),
`## First bring-up` (25), `## Updating` (54), `## The key ring` (68) and `## ICMP` (80). Put this
under `## First bring-up`, after the `.env` step, since that is where the variable is set. Add
a short note: a deployment reached over plain HTTP must set `HOMON_ALLOW_PLAINTEXT_SESSIONS=true`
or the administrator cannot sign in — the form succeeds and silently returns to the signed-out
state. Mention that `http://localhost` is exempt, so an SSH tunnel
(`ssh -L 8102:127.0.0.1:8102 <host>`) is the workaround on a host that has not set the flag.

### Step 9: Reconcile the index

In `plans/README.md`, set this plan's row Status to `DONE (<date>, <commit>)` following the
existing format, and add a line to the "Dependency notes" section recording that 015 introduced
`Auth:AllowPlainTextSessions` and the nginx forwarded-scheme map.

## Test plan

| What | Where | Pattern to follow |
| --- | --- | --- |
| Production defaults to a `Secure` cookie | `PlainTextSessionTests` test 1 | `AuthenticationEndpointTests`' `Set-Cookie` assertions |
| The flag drops `Secure` over plain HTTP | `PlainTextSessionTests` test 2 | `SignInThrottleTests`' derived factory |
| The flag *keeps* `Secure` over HTTPS (D5) | `PlainTextSessionTests` test 3 | same, plus an `https` `client.BaseAddress` |
| Development is unchanged | the existing `AuthenticationEndpointTests` — they must still pass untouched | — |
| nginx still parses | `nginx -t` in Step 1 | — (no suite covers this file) |

Do **not** add an e2e spec for this. The Playwright suite runs against `vite preview`, not nginx,
and it already signs in over `http://localhost`, which is exempt from the `Secure` rule — a spec
there would pass identically before and after this change and would prove nothing.

## Done criteria

Machine-checkable:

```bash
./ci/run-ci.sh                     # PASS — web api e2e
git diff --stat main..HEAD         # only the files listed under Scope
grep -c 'homon_forwarded_proto' src/Homon.Web/nginx.conf      # 2 (the map, and the proxy_set_header)
grep -c 'AllowPlainTextSessions' src/Homon.Api/Configuration/AuthOptions.cs   # >= 1

# SecurePolicy is assigned in exactly one place, and that place reads the flag. These two
# together are what prove the old assignment was removed rather than duplicated — do not
# substitute a count of `CookieSecurePolicy`, which changes with how the ternary is wrapped.
grep -c 'options.Cookie.SecurePolicy' src/Homon.Api/Program.cs   # 1
grep -c 'AllowPlainTextSessions' src/Homon.Api/Program.cs        # 1

grep -c 'HOMON_ALLOW_PLAINTEXT_SESSIONS' .env.example compose.prod.yaml   # 1 each
```

`./ci/run-ci.sh api` must report **three** new passing tests in `PlainTextSessionTests` and no
skips.

Plus: `plans/README.md` row updated, a new `### 3.21` in `docs/ARCHITECTURE.md`, and
`docs/deployment-runbook.md` carrying the note from Step 8.

## STOP conditions

Stop and report — do not improvise — if any of these happen:

- **The drift check is non-empty and the "Current state" excerpts no longer match.** This plan
  was written against `5ff263a`.
- **A Production-environment test factory will not start**, with a validation message other than
  the `Email:ResendApiToken` one Step 6 already handles. Report the exact message rather than
  adding configuration keys until it boots — a new Production-only validator is information the
  maintainer needs, and silently satisfying it may hide a real requirement.
- **Test 2 passes but test 1 fails.** That means the cookie is never `Secure`, i.e. the policy was
  hard-coded to `None` or `SameAsRequest` unconditionally. D3 and D5 both forbid that; re-read
  Step 3.
- **Test 3 fails while 1 and 2 pass.** The flag turned `Secure` off outright instead of making it
  follow the request scheme. That is the one variant D5 exists to rule out; re-read Step 3 rather
  than adjusting the test.
- **You find yourself editing `HomonApiFactory.cs`** to make the new tests work. It is out of
  scope; other tests depend on its Development environment. Derive a factory instead.
- **You find yourself changing `ForwardLimit`, `KnownIPNetworks`, or `$proxy_add_x_forwarded_for`.**
  D6 forbids it; that would start trusting a client-supplied `X-Forwarded-For`.
- **`nginx -t` fails** on the new map. If the message is *"map directive is not allowed here"*,
  the map landed inside `server { }` or a `location` — move it above line 20, beside
  `$homon_cache`. Do not "simplify" it into the `location` block: the file's own header comment
  explains at length why configuration lives outside the locations here.
- **A verification fails twice** after one reasonable fix attempt.

## Post-deploy verification (for the maintainer, not the executor)

This defect is only fully provable against the real deployment. **`nginx.conf` is baked into the
`ghcr.io/acastaner/homon-web` image at build time** (`compose.prod.yaml:155`), so Step 1 does
nothing until a new image is built, pushed and pulled — bump `HOMON_VERSION` and re-run the
update procedure; editing the file on the host has no effect. Then the asymmetry is the proof
that both halves work:

```bash
# LAN, plain HTTP: expect 204 and a Set-Cookie WITHOUT `secure`
curl -is -X POST http://<lan-name>:8102/api/v1/auth/sign-in \
  -H 'Content-Type: application/json' -d '{"email":"…","password":"…"}' | grep -i '^set-cookie'

# WAN, through the WAF: expect 204 and a Set-Cookie WITH `secure`
curl -is -X POST https://<wan-name>/api/v1/auth/sign-in \
  -H 'Content-Type: application/json' -d '{"email":"…","password":"…"}' | grep -i '^set-cookie'
```

If the WAN response has no `secure`, there are exactly two causes and both are configuration,
not code:

1. the WAF is not sending `X-Forwarded-Proto: https` — fix it at the WAF rather than loosening
   D1's allow-list map; or
2. `HOMON_PROXY_NETWORK` does not cover the network the web container reaches the api container
   from, so the API's forwarded-headers block never runs and no `X-Forwarded-Proto` is honoured
   (D1's precondition). The address the api container sees is the web container's, so check it
   with

   ```bash
   docker compose -f compose.prod.yaml ps -q web \
     | xargs docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}'
   ```

   and confirm that address falls inside `HOMON_PROXY_NETWORK` (default `172.16.0.0/12`).

Setting `HOMON_ALLOW_PLAINTEXT_SESSIONS=false` again restores the old behaviour on both paths
while either is investigated.

## Maintenance notes

For whoever owns this next:

- **The flag is a bridge, not a destination.** The right end state is TLS on the plain-HTTP
  origin — for a name under a domain you control, a DNS-01 certificate works even when the A
  record points at a private address, so no internal CA has to be installed on phones. When that
  lands, set `HOMON_ALLOW_PLAINTEXT_SESSIONS=false`; D5 means nothing else changes, and Step 1's
  nginx map stays correct and useful permanently.
- **The map is an allow-list on purpose.** A future PR that "simplifies" it to
  `default $http_x_forwarded_proto` reintroduces uninspected client input into the scheme
  decision. D1 says why.
- **Anything a reviewer should scrutinise**: that `SecurePolicy` is assigned in exactly one place;
  that the new registration resolves `IOptions<AuthOptions>` and does not close over
  `builder.Configuration` (D4); that `HomonApiFactory` is unchanged; and that the default in both
  `compose.prod.yaml` and `AuthOptions` is `false`.
- **Related, deliberately not fixed here**: `compose.prod.yaml`'s
  `net.ipv4.ping_group_range: "0 2147483647"` cannot be applied under rootless Docker and stops
  the api container from starting there. It is a separate defect with a separate fix — see
  **plan 016**, which should ship in the same release as this one.
