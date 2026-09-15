using Microsoft.Extensions.Configuration;
using Npgsql;

namespace TrailGuard.Services
{
    // Which physical database TrailGuard is configured to use for this run.
    public enum DatabaseTarget
    {
        Local,
        Supabase
    }

    // The outcome of resolving Database:Target: which target was selected, the
    // connection string to use, which SeedAdmin password key applies, and a
    // credential-free description of the endpoint safe to log.
    public sealed class ResolvedDatabaseTarget
    {
        public required DatabaseTarget Target { get; init; }
        public required string ConnectionString { get; init; }
        public required string SeedAdminPasswordKey { get; init; }
        public required string SafeEndpointDescription { get; init; }
    }

    // Single shared place that decides Local vs. Supabase from configuration.
    // Program.cs calls this before building the DbContext - the same top-level
    // statements EF Core design-time tooling (dotnet ef) executes - and DbSeeder
    // reads the resolved target via DI, so runtime, EF tooling, and the seeder
    // can never resolve the target two different ways.
    public static class DatabaseTargetResolver
    {
        private const string TargetConfigKey = "Database:Target";
        private const string LocalConnectionStringKey = "DefaultConnection";
        private const string SupabaseConnectionStringKey = "SupabaseConnection";
        private const string LocalSeedAdminPasswordKey = "SeedAdmin:Password";
        private const string SupabaseSeedAdminPasswordKey = "SeedAdmin:SupabasePassword";

        public static ResolvedDatabaseTarget Resolve(IConfiguration configuration)
        {
            var rawTarget = configuration[TargetConfigKey];

            DatabaseTarget target;
            if (rawTarget is null)
            {
                // Key entirely absent (as opposed to present-but-blank below):
                // preserve current behavior by defaulting to Local.
                target = DatabaseTarget.Local;
            }
            else if (string.Equals(rawTarget, nameof(DatabaseTarget.Local), StringComparison.OrdinalIgnoreCase))
            {
                target = DatabaseTarget.Local;
            }
            else if (string.Equals(rawTarget, nameof(DatabaseTarget.Supabase), StringComparison.OrdinalIgnoreCase))
            {
                target = DatabaseTarget.Supabase;
            }
            else
            {
                // An explicitly blank value ("" or whitespace) falls through to
                // here too - it is not null, so it does not default to Local.
                throw new InvalidOperationException(
                    $"Configuration key '{TargetConfigKey}' must be 'Local' or 'Supabase' " +
                    $"(found: {(string.IsNullOrWhiteSpace(rawTarget) ? "blank" : "an unrecognized value")}). " +
                    "Leave the key unset entirely to default to Local.");
            }

            var connectionStringKey = target == DatabaseTarget.Local ? LocalConnectionStringKey : SupabaseConnectionStringKey;
            var connectionString = configuration.GetConnectionString(connectionStringKey);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Never fall back to the other target's connection string here -
                // a missing Supabase setting must fail, not silently run against Local.
                throw new InvalidOperationException(
                    $"Database:Target is '{target}', which requires ConnectionStrings:{connectionStringKey}, " +
                    "but it is not configured. Set it via " +
                    $"'dotnet user-secrets set \"ConnectionStrings:{connectionStringKey}\" \"<connection string>\"' " +
                    $"(local) or the ConnectionStrings__{connectionStringKey} environment variable (deployment), " +
                    "then restart the application.");
            }

            // Parsed eagerly, here, so a malformed value fails fast with our own
            // sanitized message - never later from an unwrapped Npgsql exception
            // at first connection use, which could echo the raw input. The
            // original connectionString (not a value rebuilt from this builder)
            // is still what's returned/used for the real connection, so every
            // setting the caller supplied - SSL Mode included - is preserved
            // exactly as configured; this builder exists only to validate and
            // to source the safe log description below.
            var safeEndpointDescription = ParseAndDescribeSafely(connectionString, target, connectionStringKey);

            var seedAdminPasswordKey = target == DatabaseTarget.Local ? LocalSeedAdminPasswordKey : SupabaseSeedAdminPasswordKey;

            return new ResolvedDatabaseTarget
            {
                Target = target,
                ConnectionString = connectionString,
                SeedAdminPasswordKey = seedAdminPasswordKey,
                SafeEndpointDescription = safeEndpointDescription
            };
        }

        // Fixed, credential-free error text: only ever names the target and the
        // configuration key, never the underlying parser exception's message,
        // the raw connection string, or any single offending token - all of
        // which can contain or reveal a password. Host/Port/Database are the
        // only fields ever read back out of the parsed builder.
        private static string ParseAndDescribeSafely(string connectionString, DatabaseTarget target, string connectionStringKey)
        {
            NpgsqlConnectionStringBuilder builder;
            try
            {
                builder = new NpgsqlConnectionStringBuilder(connectionString);
            }
            catch
            {
                // Deliberately not passed as innerException, and the caught
                // exception's Message/Data is never read - either could
                // contain or echo the offending value.
                throw new InvalidOperationException(
                    $"ConnectionStrings:{connectionStringKey} (for Database:Target '{target}') is not a valid " +
                    "PostgreSQL connection string.");
            }

            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{connectionStringKey} (for Database:Target '{target}') must specify " +
                    "both a non-blank Host and a non-blank Database - this project always selects an explicit " +
                    "database, never a server default.");
            }

            return $"{target} ({builder.Host}:{builder.Port}/{builder.Database})";
        }
    }
}
