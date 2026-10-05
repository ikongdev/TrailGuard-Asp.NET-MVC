# TrailGuard demo participant seeder

This one-time console utility creates only the 50 fictional Participant accounts defined in its source. Their deterministic email and user-name namespace is participant01@trailguard.com through participant50@trailguard.com. It does not create roles, migrate a database, seed startup data, or call any ML, registration, payment, medical, or event workflow.

It requires an explicitly selected Supabase target and a shared password. The password is never written to source, the roster CSV, or console output.

From the repository root in Windows CMD:

~~~cmd
dotnet user-secrets set --project TrailGuard.csproj "Database:Target" "Supabase"
dotnet user-secrets set --project TrailGuard.csproj "DemoParticipants:Password" "TrailGuardDemo!2026"
dotnet run --project Verification\TrailGuardDemoParticipantsSeeder\TrailGuardDemoParticipantsSeeder.csproj
~~~

Use another strong shared password instead of the example if desired. To set it only for the current CMD session:

~~~cmd
set "DemoParticipants__Password=TrailGuardDemo!2026"
dotnet run --project Verification\TrailGuardDemoParticipantsSeeder\TrailGuardDemoParticipantsSeeder.csproj
~~~

The utility first shows the safe Supabase endpoint description and all 50 planned profiles, validates data/schema/namespace, writes a password-free roster to artifacts\demo-participants-roster.csv, and then applies the authorized seed. Use --preflight-only to stop before password validation and writes.
