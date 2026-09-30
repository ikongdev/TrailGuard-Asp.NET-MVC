using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

Check(PickupPointCatalogHelper.TryNormalize("  Cubao Bus Terminal  ", out var name, out var normalized, out _), "A valid point should normalize.");
Check(name == "Cubao Bus Terminal" && normalized == "CUBAO BUS TERMINAL", "Normalization must trim and compare case-insensitively.");
foreach (var invalid in new[] { " ", "Cubao\nTerminal", "Cubao — Terminal", new string('x', PickupPointCatalogHelper.MaxNameLength + 1) })
    Check(!PickupPointCatalogHelper.TryNormalize(invalid, out _, out _, out _), "Invalid catalog names must be rejected.");

var current = new HashSet<string>(StringComparer.Ordinal) { "CUBAO BUS TERMINAL" };
var validNew = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = "cubao bus terminal", Time = "05:00" }], current);
Check(validNew == null, "A new schedule may use a current catalog point.");

var preservedCatalogCasing = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = "Cubao Bus Terminal", Time = "05:00" }], current);
Check(preservedCatalogCasing == null, "An existing row may retain its saved casing when it matches a catalog point.");

var arbitrary = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = "Unlisted point", Time = "05:00" }], current);
Check(arbitrary != null, "A new schedule must reject an arbitrary location.");

var original = new[] { new PickupScheduleHelper.EditablePickupSchedule { Location = "Legacy gate", Time = "05:00" } };
var preservedLegacy = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = "Legacy gate", Time = "06:00" }], current, original);
Check(preservedLegacy == null, "An Edit Event may preserve its own legacy location while changing its time.");

var duplicatedLegacy = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = "Legacy gate", Time = "05:00" }, new PickupScheduleInputModel { Location = "Legacy gate", Time = "06:00" }], current, original);
Check(duplicatedLegacy != null, "A single legacy row cannot authorize additional schedules.");

var longLegacyName = new string('L', PickupPointCatalogHelper.MaxNameLength + 1);
var longLegacy = new[] { new PickupScheduleHelper.EditablePickupSchedule { Location = longLegacyName, Time = "05:00" } };
var preservedLongLegacy = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = longLegacyName, Time = "06:00" }], current, longLegacy);
Check(preservedLongLegacy == null, "An Edit Event may preserve a pre-catalog location longer than the catalog limit.");

var arbitraryLongName = PickupPointCatalogHelper.ValidateScheduleLocations(
    [new PickupScheduleInputModel { Location = new string('A', PickupPointCatalogHelper.MaxNameLength + 1), Time = "05:00" }], current, longLegacy);
Check(arbitraryLongName != null, "A long location is allowed only when it is one of the event's own legacy rows.");

if (args.Contains("--database", StringComparer.Ordinal))
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets(typeof(DatabaseTargetResolver).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();
    var target = DatabaseTargetResolver.Resolve(configuration);
    Check(target.Target == DatabaseTarget.Local, "The database verification must run only against the Local target.");

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(target.ConnectionString)
        .Options;
    await using var connection = new NpgsqlConnection(target.ConnectionString);
    await connection.OpenAsync();

    async Task<bool> ExistsAsync(string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    Check(await ExistsAsync("SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260930014054_AddPickupPointsCatalog')"), "The Pickup Points migration must be recorded.");
    Check(await ExistsAsync("SELECT to_regclass('public.\"PickupPoints\"') IS NOT NULL"), "The PickupPoints table must exist.");
    Check(await ExistsAsync("SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'IX_PickupPoints_NormalizedName')"), "The catalog unique index must exist.");
    Check(await ExistsAsync("SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PickupPoints_Name_Valid')"), "The catalog check constraint must exist.");

    await using var context = new ApplicationDbContext(options);
    var eventsBefore = await context.Events.AsNoTracking()
        .OrderBy(eventItem => eventItem.Id)
        .Select(eventItem => new { eventItem.Id, eventItem.PickupPoints })
        .ToListAsync();
    var registrationsBefore = await context.EventRegistrations.AsNoTracking()
        .OrderBy(registration => registration.Id)
        .Select(registration => new { registration.Id, registration.PickupPoint })
        .ToListAsync();
    var verificationName = "Pickup Points Verification " + Guid.NewGuid().ToString("N");
    PickupPoint? created = null;
    try
    {
        Check(PickupPointCatalogHelper.TryNormalize(verificationName, out var createdName, out var createdNormalizedName, out _), "The isolated catalog verification name must be valid.");
        created = new PickupPoint { Name = createdName, NormalizedName = createdNormalizedName };
        context.PickupPoints.Add(created);
        await context.SaveChangesAsync();
        Check(created.Id > 0, "An isolated pickup point must be created.");

        var renamedName = verificationName + " Renamed";
        Check(PickupPointCatalogHelper.TryNormalize(renamedName, out var normalizedRename, out var normalizedRenameKey, out _), "The isolated rename must be valid.");
        created.Name = normalizedRename;
        created.NormalizedName = normalizedRenameKey;
        await context.SaveChangesAsync();
        Check((await context.PickupPoints.AsNoTracking().SingleAsync(point => point.Id == created.Id)).Name == normalizedRename, "An isolated pickup point must be renamed.");

        await using (var duplicateContext = new ApplicationDbContext(options))
        {
            duplicateContext.PickupPoints.Add(new PickupPoint { Name = normalizedRename.ToLowerInvariant(), NormalizedName = normalizedRenameKey });
            var rejected = false;
            try
            {
                await duplicateContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                rejected = true;
            }
            Check(rejected, "Database uniqueness must reject a case-insensitive duplicate pickup point.");
        }

        Check(await context.PickupPoints.FindAsync(int.MaxValue) == null, "A missing pickup point lookup must remain missing.");
    }
    finally
    {
        if (created != null)
        {
            context.Entry(created).State = EntityState.Deleted;
            await context.SaveChangesAsync();
        }
    }

    var eventsAfter = await context.Events.AsNoTracking()
        .OrderBy(eventItem => eventItem.Id)
        .Select(eventItem => new { eventItem.Id, eventItem.PickupPoints })
        .ToListAsync();
    var registrationsAfter = await context.EventRegistrations.AsNoTracking()
        .OrderBy(registration => registration.Id)
        .Select(registration => new { registration.Id, registration.PickupPoint })
        .ToListAsync();
    Check(eventsBefore.SequenceEqual(eventsAfter), "Catalog mutations must not alter existing event pickup text.");
    Check(registrationsBefore.SequenceEqual(registrationsAfter), "Catalog mutations must not alter existing registration pickup text.");
}

Console.WriteLine($"PASS: {assertions} assertions. This verifies catalog normalization and the server-side current/legacy schedule policy{(args.Contains("--database", StringComparer.Ordinal) ? ", Local migration schema, and isolated catalog CRUD" : " without a database or HTTP host")}.");
