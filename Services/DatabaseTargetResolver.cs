using Microsoft.Extensions.Configuration;
using Npgsql;

namespace TrailGuard.Services
{

    public enum DatabaseTarget
    {
        Local,
        Supabase
    }




    public sealed class ResolvedDatabaseTarget
    {
        public required DatabaseTarget Target { get; init; }
        public required string ConnectionString { get; init; }
        public required string SeedAdminPasswordKey { get; init; }
        public required string SafeEndpointDescription { get; init; }
    }






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


                throw new InvalidOperationException(
                    $"Configuration key '{TargetConfigKey}' must be 'Local' or 'Supabase' " +
                    $"(found: {(string.IsNullOrWhiteSpace(rawTarget) ? "blank" : "an unrecognized value")}). " +
                    "Leave the key unset entirely to default to Local.");
            }

            var connectionStringKey = target == DatabaseTarget.Local ? LocalConnectionStringKey : SupabaseConnectionStringKey;
            var connectionString = configuration.GetConnectionString(connectionStringKey);

            if (string.IsNullOrWhiteSpace(connectionString))
            {


                throw new InvalidOperationException(
                    $"Database:Target is '{target}', which requires ConnectionStrings:{connectionStringKey}, " +
                    "but it is not configured. Set it via " +
                    $"'dotnet user-secrets set \"ConnectionStrings:{connectionStringKey}\" \"<connection string>\"' " +
                    $"(local) or the ConnectionStrings__{connectionStringKey} environment variable (deployment), " +
                    "then restart the application.");
            }









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






        private static string ParseAndDescribeSafely(string connectionString, DatabaseTarget target, string connectionStringKey)
        {
            NpgsqlConnectionStringBuilder builder;
            try
            {
                builder = new NpgsqlConnectionStringBuilder(connectionString);
            }
            catch
            {



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
