# Payment reminder PostgreSQL verification

This executable runs the production reminder processor, candidate query, EF/Npgsql
store, and real Identity role lookup against a newly created local PostgreSQL
database. It does not start the web application or hosted worker. Only email
delivery, time, and supporting environment/options are controlled. No SMTP sender
is registered, and all accounts and registrations are synthetic.

From the repository root (requires the existing .NET 10 dependencies and an
accessible native local PostgreSQL service):

```powershell
Get-PSDrive -Name C
dotnet run --project Verification/TrailGuardPaymentReminderPostgresVerification -- --probe-local
dotnet run --project Verification/TrailGuardPaymentReminderPostgresVerification -- --run
dotnet run --project Verification/TrailGuardPaymentDeadlineReminderVerification
dotnet build
npm run build
git diff --check
```

Credentials are read privately from the main project's existing User Secrets
`ConnectionStrings:DefaultConnection`. Alternatively, provide an existing local
connection through the process environment variable
`TRAILGUARD_REMINDER_IT_LOCAL_CONNECTION`; never paste its value into logs or a
tracked file. The verifier does not read appsettings, `Database:Target`, or
Supabase configuration. The credential source's database is never opened.

Only literal loopback hosts or `localhost` are accepted; `localhost` is pinned to
`127.0.0.1`. The verifier copies only host/port/username/password, opens the
`postgres` maintenance database, and checks the actual database, server address,
client address, and server port. `--probe-local` stops after these read-only
checks. `--run` generates a fresh `trailguard_reminder_it_<GUID>` database name,
creates it without reusing any existing database, checks its endpoint again, and
applies the existing migrations using `MigrateAsync`. The local account must have
permission to create and drop its test database. No migrations are generated.

The run checks:

1. A full page of 50 rejected accounts (10 each inactive, unconfirmed, invalid
   email, missing role, conflicting roles) followed by a valid account sharing
   the same deadline. The valid account must be dispatched within the same poll;
   rejected accounts must acquire no tracking/suppression rows.
2. Repairing one account of each rejection type allows a later poll to process
   them; later polls do not redispatch successful reminders.
3. Twenty-five future retries and 25 active leases precede a due account. The
   real discovery query must return only the due account before its 50-row limit.
4. Separate contexts with distinct PostgreSQL backend connections contend for
   one reminder behind an asynchronous start barrier. Exactly one claim wins,
   with the expected persisted owner, lease expiry, and attempt count. A separate
   check holds the first production processor inside the controlled sender while
   a second independently scoped processor polls; the second must not dispatch.
5. Advancing controlled time beyond the lease lets a new owner claim. The old
   owner cannot overwrite either the new active lease or its completed result.

The harness uses the production legacy Npgsql timestamp configuration. Returned
`timestamptz` values are normalized to UTC when comparing instants; Philippine
wall-clock payment deadlines keep their original representation.

Concurrency/polls have 20-second cancellation bounds; connection attempts have a
5-second bound and database commands a 15-second bound. Gates coordinate the
contenders; arbitrary sleeps are not used. Each scenario advances synthetic time
to keep earlier fixtures outside its deadline window. Driver/configuration
exception details are withheld to avoid leaking private values; assertion
descriptions and PostgreSQL SQLSTATE values are safe to report. A nonzero exit
or missing scenario PASS means the run did not establish that scenario.

On normal completion or a handled failure, all scopes/connections are disposed
and only the database created by that invocation is dropped. Cleanup never uses
`FORCE`, terminates other sessions, or removes an existing database. A cleanup
failure prints the exact generated test database name and fails the command;
abrupt process termination may also leave that database behind. The verifier
does not install/start/stop PostgreSQL, manage Docker, or alter services. No
build outputs or private configuration belong in the verification diff.

These bounded checks provide local database evidence, not proof of exactly-once
inbox delivery, every PostgreSQL interleaving, or historical production delivery.
The existing controlled-store verifier remains complementary coverage.
