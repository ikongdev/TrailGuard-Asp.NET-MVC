# Linux container packaging

These files target **linux/amd64**. They do not provision Azure or change application
workflows. MVC must not be started during offline packaging verification: its startup
seeder and role audit contact the configured database.

## Recorded hosting handoff (user-reported, 2026-10-02)

The supplied live deployment uses Azure for Students in `rg-trailguard`, Australia
East (`australiaeast`): Linux B2 single-worker plan `plan-trailguard` and web app
`trailguard-c9a8-20261002` at `trailguard-c9a8-20261002.azurewebsites.net`. Its ACR
is `trailguardacr20261002` (`trailguardacr20261002-fkf3btbzgeafgkgf.azurecr.io`).
The recorded registry manifest digests are:

| Service | Tag | Registry manifest digest |
| --- | --- | --- |
| MVC | `hosting-20261002-02` | `sha256:738c06e08b6e147474df190301eebe728426745b02acbc8acb79ee978460c5db` |
| Python v2 | `hosting-20261002-01` | `sha256:b261bf0048537578c2ad9ba61eb6f1c7951ca148d85639ae43af79af6a2f0d86` |

The user-assigned identity is `id-trailguard` (client ID
`89b7a41f-f2ec-488f-b9c7-dfdaa03f266c`, principal ID
`21d3f588-5d23-4b78-ba86-853fb3d7e314`). Its Data Protection configuration uses Blob
`https://sttrailguardc9a820261002.blob.core.windows.net/data-protection/keyring.xml`
and the versionless Key Vault wrapping key
`https://kvtrailguardc9a820261002.vault.azure.net/keys/trailguard-dp`. Key Vault
references are named `supabase-connection-string` and `supabase-storage-secret`; no
secret values are recorded here. Hosted storage uses
`https://ylckjuqymvyozammqrex.supabase.co` and namespace `supabase-main`.

Keep Npgsql at `SSL Mode=VerifyFull` with
`Root Certificate=/app/certificates/supabase-prod-ca-2021.crt`. The observed direct
proxy sender was `::ffff:169.254.130.1`; temporary proxy observation is disabled.
That one observation is not an Azure-wide stable-IP guarantee. Preserve the bounded
discovery procedure before changing its explicit trust boundary. Portal budget
`budget-trailguard-monthly` exists and notifies only; it does not stop charges.
The staged public CA file SHA-256 is
`700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7`.

The project owner reports live HTTPS 200/HSTS, login, actual ML assessment and SHAP,
public uploads, owner MVC-document access with anonymous denial, and login-cookie,
image, and ML continuity after restart. A Supabase Dashboard signed URL was separately
accessible by design. These are user-reported deployment observations, not tests run
by the packaging verification.

## Verification status (2026-10-01)

Docker Desktop's `desktop-linux` context was verified as `linux/amd64`. No host
software was installed. Clean candidate-tree builds completed on 2026-10-01:
`trailguard-v2:verify` (1,354,343,660 bytes) and `trailguard-mvc:verify`
(460,209,217 bytes). MVC was built but never started. Python was started with
`--network none`; all 20 frozen checksums, `pip check`, seven offline adapter checks,
the recorded prediction/SHAP fixture, health and model-info checks passed. The
measured container-create-to-health time was 16,102 ms, and cgroup memory peaked at
181,829,632 bytes during five sequential predictions. Those are local Docker Desktop
measurements, not an Azure sizing commitment.

Public registry manifests and image configurations confirmed the following existing
Linux/amd64 images. The Dockerfiles pin the platform manifest digests, not moving tags.

| Image tag | Linux/amd64 manifest digest | Registry compressed layer bytes |
| --- | --- | ---: |
| `mcr.microsoft.com/dotnet/sdk:10.0-noble` | `sha256:28e7a5db4f5d40cc805acd939a065668ba2e17d697a09153054dce98db240d0e` | 350895993 |
| `mcr.microsoft.com/dotnet/aspnet:10.0-noble` | `sha256:ed6a2d26633ddcd3d42a1d9f9866214ecbbc11ba6ac5e0e843da02c13da24072` | 95886394 |
| `python:3.14.6-slim-bookworm` | `sha256:ff83a535339812dd72e69c93b3c48ddf7c85a324d6330af5797c82a255dbeef4` | 44765772 |

The SDK image reports SDK 10.0.401 and runtime 10.0.12; the ASP.NET image reports
10.0.12. These match the app's .NET 10 target family. Python reports 3.14.6, matching
the frozen environment. Compressed base-layer totals are neither installed image
sizes nor memory measurements. Registry inspection is not runtime verification.

The non-chiseled Ubuntu Noble .NET runtime includes ICU and tzdata according to its
[official Dockerfile](https://github.com/dotnet/dotnet-docker/blob/main/src/runtime-deps/10.0/noble/amd64/Dockerfile).
Python uses glibc-based Debian Bookworm and installs libgomp1, tzdata and CA certificates.

## Build contexts and configuration

- MVC uses the repository root context and root `.dockerignore`. Only app sources
  and static assets are included. Existing CSS is copied unchanged; Node/Tailwind
  never runs. The runtime executes `dotnet TrailGuard.dll` as the base image's app
  user on HTTP 8080 with Philippine local time and globalization enabled.
- Python uses **`ml-services/trailguard-v2` as its context**, so its own `.dockerignore`
  applies. Only adapter sources, requirements and the complete frozen bundle enter
  the image. Tests are mounted separately. It runs as a non-root user, with one
  Uvicorn worker listening on **127.0.0.1:8011** for the planned shared-network sidecar.
  Publishing a Docker port does not make this loopback listener externally reachable.
- Local appsettings, secrets, uploads, virtual environments, caches, build output,
  node_modules and training datasets are excluded. Historical ML and verification
  projects stay in Git and outside the MVC runtime image.
- No appsettings file is packaged. Supply configuration through the hosting
  environment later. Required hosted keys include `Database__Target`,
  `ConnectionStrings__SupabaseConnection`, `Storage__Provider`, `Storage__Namespace`,
  `Storage__Supabase__Url`, `Storage__Supabase__SecretKey` and `TrailGuardV2Api__BaseUrl`.
  Hosted uploads must select Supabase and namespace `supabase-main`. The Supabase
  seeding password is needed only when creating the initial Admin. No values belong
  in the build context, image layers or build arguments.
- The MVC runtime packages the verified public Supabase Root 2021 CA at
  `/app/certificates/supabase-prod-ca-2021.crt`, readable by UID 1654. The hosted
  Npgsql connection string must retain `SSL Mode=VerifyFull` and
  `Root Certificate=/app/certificates/supabase-prod-ca-2021.crt`; neither a database
  credential nor private key is packaged.
- Data Protection persistence and proxy configuration are now implemented only when
  their explicit hosted settings are supplied. Azure resource settings remain a later,
  deployment-time task.

## App Service hosting configuration

Runtime images intentionally contain no `appsettings*.json` files. Configure these
values as App Service application settings (double underscores represent `:`). Do not
place any secret value in Git, Docker build arguments, image layers, or this document.

| Setting | Hosted value or intentional behavior |
| --- | --- |
| `ASPNETCORE_HTTP_PORTS` | Image default: `8080`. |
| `ASPNETCORE_HTTPS_PORT` | Set `443` in hosted App Service settings. This is the singular HTTPS-redirection destination, not `ASPNETCORE_HTTPS_PORTS`, which configures server listeners. |
| `WEBSITES_PORT` | Do **not** set for the planned sidecar-enabled `sitecontainers` site. This classic custom-container setting does not apply to sidecar-enabled apps. |
| `ASPNETCORE_ENVIRONMENT` | Image default: `Production`. |
| `TZ` | Image default: `Asia/Manila` in both containers. |
| `AllowedHosts` | Set the exact hosted site hostname(s); do not rely on the empty runtime default. |
| `Database__Target` | Set `Supabase`. The Local default is preserved only for development. |
| `ConnectionStrings__SupabaseConnection` | Required secret when the target is Supabase. Retain `SSL Mode=VerifyFull;Root Certificate=/app/certificates/supabase-prod-ca-2021.crt`; the image packages only this public Supabase CA file. |
| `Storage__Provider` | Set `Supabase`. |
| `Storage__Namespace` | Set `supabase-main`. |
| `Storage__Supabase__Url` / `Storage__Supabase__SecretKey` | Required hosted storage endpoint and secret. |
| `Storage__Supabase__PublicImagesBucket` / `Storage__Supabase__PrivateDocumentsBucket` | Intentional defaults are the existing `trailguard-public-images` and `trailguard-private-documents`; the application rejects substitutions. |
| `Storage__Supabase__TimeoutSeconds` | Intentional default: 20 seconds. |
| `Storage__Local__RootPath` | Intentional local-only default: `App_Data/uploads`; it is unused when `Storage__Provider=Supabase` and must stay outside `wwwroot` if configured. |
| `TrailGuardV2Api__BaseUrl` | Required: `http://127.0.0.1:8011` for the private sidecar. |
| `MlApi__BaseUrl` | Legacy client fallback remains `http://127.0.0.1:8000`; set it only if that preserved historical client is actually hosted. |
| `SeedAdmin__SupabasePassword` | Required only if this Supabase database has no initial Admin yet. Do not configure `SeedAdmin__Password` for the Supabase target. |
| `DataProtection__Provider` | Set `AzureBlobKeyVault` for the selected hosted provider. `Filesystem` remains an explicit compatibility mode only. |
| `DataProtection__ApplicationName` | Required by either explicit provider. Choose one stable value and retain it across deployments. |
| `DataProtection__AzureBlobUri` | Required only for `AzureBlobKeyVault`: HTTPS Blob URI naming the dedicated key-ring blob, without a query, fragment, SAS, or credentials. |
| `DataProtection__KeyVaultKeyIdentifier` | Required only for `AzureBlobKeyVault`: HTTPS, versionless Azure Key Vault key URI. |
| `DataProtection__ManagedIdentityClientId` | Required only for `AzureBlobKeyVault`: user-assigned managed identity client ID. |
| `DataProtection__KeyRingPath` | Required only for explicit `Filesystem`; it must remain unset for `AzureBlobKeyVault`. |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | Set `false` for the selected external Azure provider; no hosted key ring requires `/home`. |
| `ForwardedHeaders__TrustedProxies__0` or `ForwardedHeaders__TrustedNetworks__0` | Required only after deployment evidence identifies the exact App Service proxy sender or CIDR. More indexed entries may be added. |
| `ProxyHeaderObservation__Enabled` / `ExpiresAtUtc` / `MaxEventCount` | Set only during bounded proxy discovery. Enabled observation requires an absolute UTC expiry within one hour of startup and an event cap from 1 through 50; remove all three after discovery. |

For the planned sidecar-enabled App Service deployment, configure the MVC container as
the `Microsoft.Web/sites/sitecontainers` entry with `isMain: true` and `targetPort:
8080`. That is the main-container routing configuration. Configure the Python entry
with `isMain: false` and `targetPort: 8011`; a sidecar's port is metadata and does not
route public traffic. App Service routes external requests only to the main container;
the containers share a network namespace, so MVC reaches the private adapter at
`http://127.0.0.1:8011`. These distinctions follow Azure's current
[sidecar configuration documentation](https://learn.microsoft.com/en-us/azure/app-service/configure-sidecar)
and [sidecar overview](https://learn.microsoft.com/en-us/azure/app-service/overview-sidecar).

`SeedAdmin__SupabasePassword` is read only while creating the first Admin. Existing
records are never reset. The MVC image retains the established ten-second ML HTTP
timeout; hosting configuration does not change it.

### Data Protection and the selected external provider

When all Data Protection settings are absent, TrailGuard keeps the existing local
development provider behavior. Explicit `Filesystem` mode retains the existing
absolute, writable key-ring path rules outside `wwwroot` and local upload storage.

The selected `AzureBlobKeyVault` mode requires all of `ApplicationName`,
`AzureBlobUri`, `KeyVaultKeyIdentifier`, and `ManagedIdentityClientId`; it rejects a
filesystem key-ring path, SAS/query Blob URI, versioned Key Vault URI, and unknown or
incomplete provider settings. It uses `ManagedIdentityCredential` for the configured
user-assigned identity, persists XML to Blob Storage, and wraps the XML using the
versionless Key Vault key identifier. No Data Protection key, certificate, password,
SAS, or XML is included in the image or App Service configuration.

The selected path has no `/home` key-ring requirement, so the MVC image no longer
creates that directory and `WEBSITES_ENABLE_APP_SERVICE_STORAGE=false` is appropriate.
Do not migrate or delete an existing filesystem key ring without separate approval.
Retain every Key Vault key version referenced by stored Data Protection XML during
rotation and rollback.

App Service application settings remain shared with sidecars. Key Vault references
protect configuration storage but do not keep their resolved values from Python, and
the managed identity is within the same App Service trust boundary. Stronger secret
isolation requires a separately established service boundary, not these settings.

### Forwarded headers and liveness

Forwarded headers are processed before HSTS, HTTPS redirection, and authentication only when
an explicit trusted proxy or non-`/0` CIDR is configured. The middleware trusts one hop,
requires matching `X-Forwarded-For` and `X-Forwarded-Proto` headers, and clears default
trusted proxy/network entries. Invalid addresses, invalid CIDRs, and whole-address-
family CIDRs fail startup. ASP.NET Core also provides the
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` convenience switch; it would bypass this
explicit-trust design, so it must not be enabled. TrailGuard rejects startup when the
setting is `true`, including when explicit trust settings are also present. See
[Microsoft's proxy and load-balancer guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

Do not guess App Service proxy ranges. A live deployment must capture the direct proxy
source and forwarded-header shape, then set the exact trust boundary and verify HTTPS
cookies/redirects. Until that evidence exists, leave this configuration absent rather
than trusting spoofable headers.

`ProxyHeaderObservation` is disabled when absent. During discovery it runs before
forwarded-header processing and records only the direct peer IP, request-scheme class,
forwarded-header presence/counts, and a sanitized forwarded-scheme class. It has no
route and cannot alter trust or requests. Its absolute UTC expiration remains in force
after a restart, and its per-process event cap prevents unbounded logging. It never
records header values, cookies, authorization, query strings, or secrets.

`GET /healthz` is anonymous. Through HTTPS it returns an empty HTTP 200 response;
plain HTTP deliberately receives the normal HTTPS redirect. It performs no database
or ML call and is suitable only as MVC process liveness. It must not be used as proof
that Supabase, storage, or the v2 adapter is ready.

### Sidecar startup and deployment prerequisites

Use the MVC container as the App Service main container on port 8080 and the Python
container as a private sidecar on `127.0.0.1:8011`. Enable Always On. The local Linux
measurement from container creation to Python `/health` was 16.102 seconds, so a
deployment verification should wait at least 30 seconds for the sidecar before making a
representative assessment request. MVC may be live while Python is still loading; the
existing no-fallback assessment behavior remains in force and the existing ten-second
request timeout remains unchanged.

Before a live deployment, verify: the actual sidecar loopback/network behavior; the
trusted proxy source and header pair; Azure Blob/Key Vault RBAC, encryption, and
restart continuity for the external Data Protection provider; Supabase database/storage
access; production host names; Always On; and `/healthz` behavior through App Service. Record a registry
**manifest digest only after pushing** each image. The local image IDs recorded in the
verification report are not deployable registry digests.

## Linux deployment lock

`requirements.linux.lock.txt` is the 28-package `pip freeze --all` result from the
pinned Linux Python image. It retains every 15 direct/frozen pin and records Linux
transitives, including `nvidia-nccl-cu13==2.32.3`, which XGBoost 3.4.0 selects on
Linux. The adapter Dockerfile installs this lock and runs `pip check`; a second,
no-cache build successfully installed it. The frozen `requirements.lock.txt` remains
unchanged.

## Build and verify after Linux Docker is available

Use a clean temporary export of a candidate Git tree containing the pending
`.gitattributes`, original frozen bytes and these packaging files. Prepare it with a
temporary `GIT_INDEX_FILE`; never stage the user's real index as part of verification.
Do not use a checkout of the old HEAD, whose normalized JSON blobs fail checksums.

Run from that export (PowerShell):

```powershell
docker version
docker info --format '{{.OSType}}'
# Require linux; do not switch modes automatically.
docker build --platform linux/amd64 --progress plain -t trailguard-mvc:verify .
docker build --platform linux/amd64 --progress plain -t trailguard-v2:verify ./ml-services/trailguard-v2
docker image inspect trailguard-mvc:verify trailguard-v2:verify --format '{{.Id}} {{.Size}}'

# This invokes sha256sum, not MVC startup.
docker run --rm --network none --entrypoint sha256sum trailguard-mvc:verify /app/wwwroot/css/output.css
docker run --rm --network none --workdir /app/frozen-bundle --entrypoint sha256sum trailguard-v2:verify --check --strict SHA256SUMS.txt
docker run --rm --network none --entrypoint python trailguard-v2:verify -m pip check

$adapterTests = (Resolve-Path ./ml-services/trailguard-v2/tests).Path
docker run --rm --network none --mount "type=bind,source=$adapterTests,target=/app/tests,readonly" --entrypoint python trailguard-v2:verify -B -m unittest discover -s tests -v
```

The Python Dockerfile requires 20 checksum entries and exactly 21 bundle files,
including the manifest. Existing tests check the recorded prediction and TreeSHAP
fixture, feature order, model hash, malformed requests, fail-closed behavior and label
boundaries. Run those tests unchanged: their fixture comparisons are exact, while the
frozen TreeSHAP internal reconstruction/additivity tolerance is 1e-4. Report any Linux
discrepancy rather than weakening assertions or changing the frozen bundle.

For HTTP verification and measurement, start only Python without external networking:

```powershell
docker run -d --network none --name trailguard-v2-verify trailguard-v2:verify
docker exec trailguard-v2-verify python -c "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:8011/health').read().decode())"
docker exec trailguard-v2-verify python -c "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:8011/model-info').read().decode())"
docker stats --no-stream trailguard-v2-verify
# Remove only this verification container when finished.
docker rm -f trailguard-v2-verify
```

Expect `trailguard-v2.0.0`, 985 trees, the frozen model SHA-256, all 11 features in
schema order, binary threshold 0.5 and the inherited 0.30/0.80 UI policy. Execute the
recorded input against `/predict` and compare its complete explanation with the
refreshed fixture, as the existing tests do.

Measure container-create-to-first-successful-health time separately from first and
warm prediction latency; report allocated CPU/memory and whether image pulls were
excluded. Poll container memory during repeated and concurrent predictions and read
cgroup peak memory where available. A single idle `docker stats` sample is not a peak
measurement. No Linux sizing conclusion can be drawn from this packaging-only run.

The completed inspection found no secret/configuration files, local uploads/private
documents, Windows virtual environments, node_modules, test/build output or training
datasets. The MVC published output contained 519 files (73,913,057 bytes), and the
Python `/app` directory contained only the adapter, requirement files, and the 21
frozen-bundle files. The in-image manifest check and the Linux lock-pin comparison
passed. The scoped checks do not replace a future Azure runtime or database test.
