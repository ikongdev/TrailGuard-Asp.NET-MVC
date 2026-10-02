# Azure deployment specification and runbook

This is a deployment handoff and runbook. It does not authorize a further push,
deployment, database connection, or storage connection. Values in angle brackets in
the reusable templates remain unresolved deployment inputs, never secret values.

## Recorded deployment handoff (user-reported, 2026-10-02)

The following are operational facts supplied by the project owner. They are not live
Azure checks run by this repository review. Keep the reusable files under
`deployment/azure/` parameterized; this record supplies their later deployment values.

| Item | Recorded value |
| --- | --- |
| Subscription / resource group / region | Azure for Students / `rg-trailguard` / `australiaeast` |
| App Service plan | `plan-trailguard`, Linux B2, one worker |
| Web app / public host | `trailguard-c9a8-20261002` / `trailguard-c9a8-20261002.azurewebsites.net` |
| Container registry | `trailguardacr20261002` / `trailguardacr20261002-fkf3btbzgeafgkgf.azurecr.io` |
| MVC image | `trailguard-mvc:hosting-20261002-02` at `sha256:738c06e08b6e147474df190301eebe728426745b02acbc8acb79ee978460c5db` |
| Python image | `trailguard-v2:hosting-20261002-01` at `sha256:b261bf0048537578c2ad9ba61eb6f1c7951ca148d85639ae43af79af6a2f0d86` |
| User-assigned identity | `id-trailguard`; client ID `89b7a41f-f2ec-488f-b9c7-dfdaa03f266c`; principal ID `21d3f588-5d23-4b78-ba86-853fb3d7e314` |
| Data Protection key ring | `https://sttrailguardc9a820261002.blob.core.windows.net/data-protection/keyring.xml` |
| Data Protection wrapping key | `https://kvtrailguardc9a820261002.vault.azure.net/keys/trailguard-dp` (versionless) |
| Key Vault reference secret names | `supabase-connection-string`, `supabase-storage-secret` |
| Supabase endpoint / storage namespace | `https://ylckjuqymvyozammqrex.supabase.co` / `supabase-main` |
| Hosted Npgsql TLS | `SSL Mode=VerifyFull` with `Root Certificate=/app/certificates/supabase-prod-ca-2021.crt` |
| Public CA file SHA-256 | `700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7` (staged file bytes) |
| Cost control | Monthly portal budget `budget-trailguard-monthly` exists; budgets notify and do not stop charges. |

The observed direct proxy sender was `::ffff:169.254.130.1`; temporary proxy
observation is now disabled. This is evidence for this deployment only, not an
Azure-wide stable IP guarantee. Re-run the bounded observation before changing the
trust boundary after an App Service networking or proxy change.

The project owner also reports these live verifications: HTTPS returned 200 with HSTS;
login worked; a real ML assessment and SHAP output completed; public uploads worked;
the owner could access a document through the MVC document link while an anonymous
request was denied; and the login cookie, images, and ML behavior continued after a
restart. A Supabase Dashboard signed URL was separately accessible by design. These
are user-reported production observations, not tests executed by this repository work.

## Target and cost estimate

The recorded deployment uses one Azure for Students subscription and one instance in
**Australia East (`australiaeast`)**. The historical estimate below is planning-only:
it used Southeast Asia retail meters and must not be treated as Australia East billing.
Use the portal's actual Australia East student-subscription costs and the existing
budget for ongoing tracking. Do not count a student registry allowance without a
confirmed entitlement.

| Resource | Purpose and proposed SKU | Region | Monthly estimate | Two months |
| --- | --- | --- | ---: | ---: |
| Resource group | Dedicated, disposable deployment boundary | Australia East | $0.00 | $0.00 |
| App Service plan | Linux **B2**, one instance; MVC and Python share its 2 vCPU / 3.5 GB | Australia East | Recheck portal | Recheck portal |
| App Service site | One sidecar-enabled `sitecontainers` web app; main MVC plus private Python | Australia East | Included in plan | Included |
| Azure Container Registry | Basic; private storage for two immutable image manifests, 10 GB included | Australia East | Recheck portal | Recheck portal |
| User-assigned managed identity | Pull images and access Data Protection resources | Australia East | $0.00 | $0.00 |
| Storage account + blob container | Standard GPv2, Hot LRS; one Data Protection key-ring blob | Australia East | Recheck portal | Recheck portal |
| Key Vault | Standard vault with one software-protected RSA key for Data Protection wrapping | Australia East | Recheck portal | Recheck portal |
| Cost budget / email alert | Subscription or dedicated-resource-group cost notification | Global control plane | $0.00 | $0.00 |
| **Estimated Azure total** | Historical Southeast Asia estimate only; use Australia East portal pricing for actual cost |  | **about $31.36** | **about $62.74** |

The current public Southeast Asia retail meters used here are B2 Linux at
$0.036/hour and ACR Basic at $0.1666/day; ACR includes 10 GB and charges extra
storage by GB. Key Vault Standard software-key operations are $0.03 per 10,000
operations; Hot LRS Blob storage is approximately $0.02/GB-month plus transactions.
Azure's pricing page says actual prices vary by offer and currency, so recheck the
portal calculator under the specific student subscription before approval.

Excluded charges: Supabase, DNS/custom-domain registration, optional custom-domain
certificate, outbound Internet or cross-region traffic, Azure Monitor/Log Analytics
ingestion, Application Insights, ACR Tasks, premium support, and any scale-out.
No Log Analytics workspace is proposed initially; use short-retention App Service
container logs for deployment diagnosis. Same-region ACR reduces avoidable registry
network traffic but does not make all networking free.

The $100 credit should cover this estimate for two months with roughly $37 headroom,
but it is not a guarantee. Azure Cost Management budgets notify only; they do not
stop resources or consumption.

## Data Protection decision

Use **Azure Blob key-ring storage protected by an Azure Key Vault software key, both
accessed through a user-assigned managed identity**. This removes the need to persist
the key ring in `/home` and avoids relying on mounted-directory ownership, `chmod`,
or the Python sidecar's inability to read a shared mount.

| Option | Assessment |
| --- | --- |
| Persist `/home` and protect with a certificate | The current code can persist keys to a writable filesystem path, but it does not call `ProtectKeysWithCertificate`. A mounted App Service `/home` can replace the image directory; its ownership and permissions must be verified live. Certificate deployment, private-key access, rotation, and protection code would all still be required. App settings and `/home` are shared with sidecars, so this is not a credible isolation boundary. Do not select it. |
| Blob + Key Vault via managed identity | Microsoft documents this combination for container-hosted ASP.NET Core: persist the XML key ring in Blob Storage and wrap it with Key Vault. It uses no SAS, account key, certificate file, or secret in the image. Blob RBAC limits key-ring writes to the hosting identity; Key Vault performs wrap/unwrap and supports a versionless key identifier for rotation. **Selected and implemented; live Azure validation remains.** |

`HostedDataProtectionOptions` now implements the explicit
`DataProtection__Provider=AzureBlobKeyVault` mode using
`Azure.Extensions.AspNetCore.DataProtection.Blobs` 1.5.4,
`Azure.Extensions.AspNetCore.DataProtection.Keys` 1.6.4, and `Azure.Identity` 1.21.0.
It requires a stable application name, an HTTPS Azure Blob URI without a query or SAS,
a versionless Key Vault key URI, and a user-assigned identity client ID. It creates a
`ManagedIdentityCredential` for that identity, calls
`PersistKeysToAzureBlobStorage(...)`, then
`ProtectKeysWithAzureKeyVault(...)`. Missing, conflicting, unknown, or malformed
provider settings fail startup; Azure mode cannot fall back to filesystem or ephemeral
keys. The unconfigured local behavior and explicit `Filesystem` mode remain available.

Offline verification exercises this registration with a fake Blob HTTP transport and a
deterministic fake Key Vault wrapping resolver. It verifies provider recreation,
encrypted key XML, application-name isolation, and failed wrap/unwrap behavior without
making managed-identity or network calls. It does not prove real Azure RBAC, Key Vault
encryption, Blob durability, key rotation, or App Service restart behavior.

The runtime identity needs `AcrPull` on the registry, `Storage Blob Data Contributor`
on the dedicated key-ring container, and `Key Vault Crypto User` on the vault/key.
Provisioning personnel, not the runtime identity, create the key and role assignments.
Use the narrowest possible role scopes: `AcrPull` on the registry, `Storage Blob Data
Contributor` on the dedicated key-ring container, and `Key Vault Crypto User` on the
specific wrapping key or vault if key scope is unavailable. Retain every wrapping-key
version referenced by existing Data Protection XML through rotation and rollback; use
the versionless identifier only for new wraps. Set
`WEBSITES_ENABLE_APP_SERVICE_STORAGE=false` for this selected external provider,
because no runtime component then depends on `/home` persistence.
This reduces shared filesystem exposure; it does not isolate App Service app settings
from Python. `Key Vault Secrets User` is separate and is granted only when App Service
Key Vault references are selected.

## Configuration skeleton

[`deployment/azure/sitecontainers.spec.template.json`](deployment/azure/sitecontainers.spec.template.json)
is an input skeleton for the documented `az webapp sitecontainers` specification and
the `Microsoft.Web/sites/sitecontainers@2024-04-01` properties. It deliberately pins
both images by **registry manifest digest**, sets MVC as `isMain: true` / `targetPort:
8080`, and marks Python `isMain: false` / `targetPort: 8011`. Only the main container
receives public traffic; the sidecar's target port is metadata, while MVC reaches its
loopback listener at `http://127.0.0.1:8011`.

[`deployment/azure/appsettings.template.json`](deployment/azure/appsettings.template.json)
is an inventory, not an importable secret file. Its selected mode uses Key Vault
references. [`deployment/azure/webapp.identity-and-keyvault.template.json`](deployment/azure/webapp.identity-and-keyvault.template.json)
shows the corresponding web-app identity property. App settings are shared by
sitecontainers, so Key Vault references protect configuration storage but their
resolved values can still be read by the Python process. This deployment cannot claim
process-level secret isolation. If Python must never access MVC secrets, the
architecture needs a separate service boundary.

Configure these web-app properties at deployment time:

- Linux custom container with sidecar mode (`LinuxFxVersion=sitecontainers`), B2 plan,
  one instance, `alwaysOn: true`, `httpsOnly: true`, minimum TLS 1.2, FTPS disabled,
  and public network access enabled only for the intended demo audience.
- Use the user-assigned identity for ACR authentication (`authType: UserAssigned` and
  its client ID); do not enable ACR admin credentials or place registry passwords in
  settings.
- Set `AllowedHosts` to the exact `azurewebsites.net` name and any bound custom
  hostname. A custom hostname must be separately verified and bound; do not invent it.
- Set `ASPNETCORE_HTTPS_PORT=443`. It configures ASP.NET Core's singular HTTPS
  **redirect destination**; do not replace it with `ASPNETCORE_HTTPS_PORTS`, which
  configures server listeners. Keep `ASPNETCORE_HTTP_PORTS=8080` for the MVC listener.
- The MVC image packages the verified public Supabase Root 2021 CA at
  `/app/certificates/supabase-prod-ca-2021.crt`. The Key Vault-referenced
  `ConnectionStrings__SupabaseConnection` value must retain
  `SSL Mode=VerifyFull;Root Certificate=/app/certificates/supabase-prod-ca-2021.crt`.
  Do not substitute `Require`, omit hostname verification, or package credentials or
  private-key material with the certificate.
- Do **not** set `WEBSITES_PORT`: it is for classic custom-container routing, not the
  sidecar-enabled schema. Do **not** set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.
- Keep the image commands unchanged. Python remains one Uvicorn worker bound to
  `127.0.0.1:8011`. Do not expose its port externally.
- The default Linux startup allowance is 230 seconds. The skeleton records that value;
  it comfortably exceeds the measured 16.102-second local Python cold start. Wait at
  least 30 seconds before the first hosted assessment verification. MVC liveness is
  intentionally independent of Python, so it is not ML readiness and does not change
  the existing ten-second ML timeout.

Do not configure a custom warm-up path/status until live App Service probing and the
HTTPS-redirection behavior have been observed. `/healthz` is MVC liveness only, and
must not be made a Python readiness gate.

## Secret delivery

The selected approach places only Key Vault **references** in App Service settings.
Before the web app receives an image configuration, create the user-assigned identity,
the vault and its required secrets, and the relevant role assignments. Attach that
identity to the web app and set the site property `keyVaultReferenceIdentity` to the
identity's full ARM **resource ID**. This is distinct from its GUID **client ID**,
which is supplied to `DataProtection__ManagedIdentityClientId` and sitecontainer ACR
configuration, and from its Microsoft Entra **principal ID**, which is the RBAC
assignee object ID.

Grant the identity `Key Vault Secrets User` at the dedicated vault resource scope
`/subscriptions/<SUBSCRIPTION>/resourceGroups/<RESOURCE_GROUP>/providers/Microsoft.KeyVault/vaults/<VAULT>`;
that vault must contain only the app-reference secrets. This grants reference
resolution only; retain `Key Vault
Crypto User` separately for the Data Protection wrapping key. The identity-and-key-
vault template records the three values without inserting any real IDs or secrets.

After applying settings, verify references without printing their resolved values:

```bash
az rest --method get --uri "https://management.azure.com$(az webapp show --resource-group <RESOURCE_GROUP> --name <APP_NAME> --query id -o tsv)/config/configreferences/appsettings/list?api-version=2022-03-01" --query "value[].{setting:name,status:properties.status,source:properties.source}" -o table
```

Every selected reference must report `Resolved`; investigate `AccessToKeyVaultDenied`,
`SecretNotFound`, or other status values without querying the app-setting values. If
the approved deployment instead supplies direct secret values through its authorized
deployment interface, do not create Key Vault reference values, do not set
`keyVaultReferenceIdentity`, and do not grant `Key Vault Secrets User`. The identity
still needs its non-secret `AcrPull`, `Storage Blob Data Contributor`, and `Key Vault
Crypto User` permissions for the selected architecture.

## Bounded proxy discovery

No Azure proxy IP range is assumed and no public diagnostic endpoint is proposed.
`ProxyHeaderObservation` is an implemented, configuration-gated middleware placed
before forwarded-header processing. It is absent unless all of these temporary settings
are supplied: `ProxyHeaderObservation__Enabled=true`, an absolute UTC
`ProxyHeaderObservation__ExpiresAtUtc` no more than one hour after startup, and
`ProxyHeaderObservation__MaxEventCount` from 1 through 50. The absolute expiry remains
effective across restarts; a restart cannot begin a new observation window.

It writes only to normal restricted App Service container logs: direct peer IP,
request-scheme classification (`http`, `https`, or `other`), `X-Forwarded-For` and
`X-Forwarded-Proto` presence/counts, and a sanitized forwarded-scheme classification.
It has no route and never emits raw header values, cookies, authorization, query
strings, tokens, or configuration values. Observation neither adds trusted proxies nor
changes any request; forwarded-header trust remains disabled until separately verified.

Deployment-time procedure:

1. Configure the three temporary observation settings with a short absolute expiry,
   leave all `ForwardedHeaders__Trusted*` settings and
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED` absent, then restart. Request the normal HTTPS
   hostname once from an authorized operator and read the restricted container log.
2. Record the observed direct proxy IP or smallest exact CIDR and the expected paired
   header shape. Treat the values as deployment evidence, not a public diagnostic.
3. Remove all three observation settings, set only the exact
   `ForwardedHeaders__TrustedProxies__N` or non-`/0`
   `ForwardedHeaders__TrustedNetworks__N` entries, and restart the site.
4. Verify trusted HTTPS liveness gives empty `200` plus HSTS with no redirect; verify
   ordinary HTTP redirects; verify a request without the trusted sender cannot cause
   forwarded HTTPS to be accepted. Keep `ASPNETCORE_FORWARDEDHEADERS_ENABLED` absent:
   the application rejects `true` at startup.

The configured values survive restarts as app settings, but their validity depends on
the App Service proxy contract remaining unchanged. Re-run this bounded observation
after a platform/network architecture change, a proxy-related incident, or any
observed rejected forwarding. If the sender changes, leave it untrusted until new
evidence is reviewed; do not broaden a CIDR to restore availability.

## Deployment and rollback runbook

1. **Preflight.** Confirm the student subscription, allowed region, B2 and
   sitecontainers support, ACR Basic, Blob, Key Vault, role-assignment permissions,
   resource naming, and credit balance. Confirm the active v2 adapter's frozen hashes
   and container verification remain recorded. Review the external Data Protection
   implementation and its fake-service checks before proceeding.
2. **Provision prerequisites before image access.** Create the dedicated resource
   group and ACR first, then the user-assigned identity, Blob account/container, Key
   Vault/key, and required app-reference secrets. Assign `AcrPull`, `Storage Blob Data
   Contributor`, and `Key Vault Crypto User` to the identity before a web app can pull
   an image or initialize Data Protection. When Key Vault references are selected,
   additionally assign `Key Vault Secrets User` on the dedicated secrets vault. These
   role assignments use the identity principal ID; the web-app attachment and
   `keyVaultReferenceIdentity` use its resource ID.
3. **Build and publish after ACR exists.** This deployment has recorded immutable
   registry digests for MVC (`sha256:738c06e08b6e147474df190301eebe728426745b02acbc8acb79ee978460c5db`)
   and Python (`sha256:b261bf0048537578c2ad9ba61eb6f1c7951ca148d85639ae43af79af6a2f0d86`).
   For a later image revision, push its immutable tag to ACR, then capture its registry
   manifest digest, for example:
   `az acr manifest list-metadata --registry <ACR_NAME> --name trailguard-mvc --query "[?tags[?@=='<TAG>']].digest" -o tsv`.
   Repeat for `trailguard-v2`, record the two output digests in the deployment record,
   and replace the template placeholders. A local Docker image ID is not deployable.
4. **Create the site without unverified proxy trust.** Create the B2 plan and
   HTTPS-only sidecar web app, attach the pre-authorized user-assigned identity, set
   `keyVaultReferenceIdentity` to its resource ID when using references, and apply the
   digest-pinned sitecontainer specification. Apply non-secret settings and either
   Key Vault references or the authorized direct-secret alternative. Verify reference
   statuses without displaying values before the app needs them.
5. **Proxy discovery and security configuration.** Perform the bounded observation
   above, set the resulting exact trust boundary, disable discovery instrumentation,
   and verify the forwarding behavior. Enable Always On and confirm B2 remains at one
   instance.
6. **Hosted verification.** The recorded user-reported checks above cover HTTPS/HSTS,
   login, an actual ML/SHAP assessment, uploads, MVC document authorization, and
   restart continuity. For a later deployment revision, confirm ACR pulls by managed
   identity; MVC `/healthz`
   over HTTPS returns empty `200` with HSTS; HTTP redirects; Python is not externally
   reachable; after 30 seconds, `/health` and `/model-info` are reachable only from
   MVC-side execution; model identity matches the frozen record; a representative
   authorized assessment preserves label, probability, feature order, and SHAP output;
   Supabase uploads use `supabase-main`; private documents remain unauthorized to a
   different user; registration, medical screening, capacity, and assessment locks
   retain their tested behavior. Verify cookie continuity across a restart and Blob/
   Key Vault activity without printing secrets.
7. **Rollback.** Keep the prior two manifest digests and the prior app-setting export.
   The recorded rollback targets are MVC
   `sha256:738c06e08b6e147474df190301eebe728426745b02acbc8acb79ee978460c5db` and Python
   `sha256:b261bf0048537578c2ad9ba61eb6f1c7951ca148d85639ae43af79af6a2f0d86`. Reapply the
   prior digest-pinned sitecontainer specification and restart the app.
   Roll back configuration as one reviewed set. Do not delete the Blob key ring or Key
   Vault key during rollback: doing so invalidates protected cookies and antiforgery
   payloads. B2 has no deployment-slot assumption in this plan, so expect a brief
   restart rather than slot swap.
8. **Cost control and demo shutdown.** Create a monthly $40 budget with alerts at
   50%, 75%, and 90%, plus a forecast alert; review Cost Management daily during the
   demo. Budgets are notifications only and do not stop charges. At demo end, retain
   the approved digest/configuration record, then delete the dedicated resource group
   after confirming no wanted Data Protection key ring or registry image remains.
   Deleting only the web app does not stop the App Service plan charge.

## Unresolved prerequisites

1. Retain the subscription's Australia East availability and credit monitoring. No
   student registry allowance is assumed, and the supplied budget does not stop spend.
2. The reported proxy sender is deployment-specific evidence, not an Azure-wide
   guarantee. Re-observe it and validate the exact header shape after relevant platform
   or network changes before changing forwarding trust.
3. Shared app-setting visibility remains: Key Vault references do not prevent Python
   from seeing MVC credential values after App Service injects them. A separate service
   boundary is required if that becomes unacceptable.
4. Continue to retain every wrapping-key version referenced by the Blob key ring during
   rotation and rollback. Do not migrate or delete any existing filesystem key ring
   without separate authorization.

Authoritative references consulted 2026-10-02: [App Service Linux pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/), [ACR pricing](https://azure.microsoft.com/en-us/pricing/details/container-registry/), [Key Vault pricing](https://azure.microsoft.com/en-us/pricing/details/key-vault/), [Azure for Students offer](https://azure.microsoft.com/en-us/pricing/offers/ms-azr-0170p), [App Service sidecars](https://learn.microsoft.com/en-us/azure/app-service/configure-sidecar), [sitecontainers schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.web/2024-04-01/sites/sitecontainers), [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [HTTPS redirection](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0), [App Service Key Vault references](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references), [App Service security settings](https://learn.microsoft.com/en-us/azure/app-service/overview-security), and [Azure budget behavior](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets).
