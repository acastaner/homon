using Homon.Domain.Auth;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Homon.Infrastructure.Persistence;

/// <summary>
/// The application's single database context: ASP.NET Core Identity's user and role
/// tables, the API keys, the probes and their groups, and — as each module lands — the
/// links, pages and backup reports.
/// </summary>
/// <remarks>
/// Derives from <see cref="IdentityDbContext{TUser, TRole, TKey}"/> so the role tables
/// exist: authorisation in Homon is role membership (<c>Administrator</c>), which is the
/// simplest model that still admits a second administrator later.
/// </remarks>
public class HomonDbContext(DbContextOptions<HomonDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Probe> Probes => Set<Probe>();

    public DbSet<ProbeObservation> ProbeObservations => Set<ProbeObservation>();

    public DbSet<ProbeGroup> ProbeGroups => Set<ProbeGroup>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HomonDbContext).Assembly);
    }
}
