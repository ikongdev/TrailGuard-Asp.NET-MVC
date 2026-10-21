namespace TrailGuard.Services;

public static class TrailGuardNpgsqlConfiguration
{
    // Existing Local schema and Philippine wall-clock workflow timestamps use the
    // Npgsql legacy DateTime mapping. This must run before EF/Npgsql is initialized.
    public static void ApplyTimestampCompatibility() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
}
