# Assessment persistence and ML policy verification

Build using the existing .NET 10 dependencies. Default mode and `--model-check`
do not open a database. `--model-check` also verifies migration/model agreement.

Database modes require all three private process variables:

- `TRAILGUARD_IT_ADMIN_CONNECTION`: literal loopback maintenance database `postgres`.
- `TRAILGUARD_IT_APP_CONNECTION`: same loopback endpoint, database `trailguard_assessment_it`.
- `TRAILGUARD_IT_ALLOW_DESTRUCTIVE`: exactly `trailguard_assessment_it`.

Never paste credentials into commands, logs, or tracked files. Use existing private
local credentials, never Supabase or the owner's application database. The harness
refuses to reuse an existing test database. It creates only its named synthetic
database, applies existing migrations there, and drops only that invocation's
database after disposing its contexts. It does not use DROP FORCE or terminate
other sessions. No MVC host, production adapter, SMTP sender, or owner uploads are used.

```powershell
dotnet run --project Verification/TrailGuardAssessmentPersistenceIntegrationVerification -- --model-check
dotnet run --project Verification/TrailGuardAssessmentPersistenceIntegrationVerification -- --run-ml-policy
```

`--run-ml-policy` runs successful persistence, forced transactional rollback,
concurrent retakes, and the policy-A cases: all active/inactive assessment history,
every registration status, owner/Admin and unrelated Organizer checks, same-trail
edits retaining inactive-trail snapshots, completion during controlled inference,
and both assessment-versus-replacement orderings. Registration creation and stale
ownership also contend with Event edits. The wait tests inspect PostgreSQL's
`pg_blocking_pids` graph; callbacks alone are not treated as proof of a lock wait.
Inference uses the recorded adapter fixture, never Test data or a new holdout run.
`--run` additionally executes the broader existing registration/capacity/storage suite.

The tests cover synthetic controller/store/database scenarios, not browser middleware
authorization or every possible interleaving. They do not inspect or reconcile
pre-existing application records. Historical mismatches and original dataset/run
provenance remain separate follow-ups. The user manual needs a later policy-A update;
no PDF is edited by these checks.
