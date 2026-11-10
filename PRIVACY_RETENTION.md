# TrailGuard manual-retention plan

Recorded November 10, 2026. This is an approved notice and planning record, not proof
of deployment or operational readiness. Cleanup is not yet authorized or executed.
No automatic deletion feature or executable cleanup procedure is introduced.

## Responsibility and contact

The TrailGuard project administrator (the user directing this work) monitors
[admin.trailguard@gmail.com](mailto:admin.trailguard@gmail.com), handles privacy
concerns and requests, and coordinates manual cleanup. Requests require review and
may require identity verification. Initial messages should not include passwords,
receipts, medical documents, or other sensitive material. No DPO, legal entity,
personal name, school affiliation, or formal response deadline is designated here.

## Conditional trigger and target

If TrailGuard is discontinued after evaluation, the planned target is manual review
and cleanup of personal data no longer needed within **30 calendar days after final
project acceptance, including acceptance of any required revisions**. Record the
acceptance evidence/date and calculate that date plus 30 calendar days. Confirm and
record discontinuation separately; the target remains anchored to final acceptance.
The December 1–2, 2026 final defense schedule is not the cleanup trigger.

This target is not a guarantee of complete erasure of every copy within 30 days.
Backups, logs, exports, and other copies require separate handling; their actual
settings and expiry remain unverified. School retention requirements remain
unconfirmed. A justified exception needs a confirmed reason, a defined data scope,
a custodian, and a defined review or retention period. Confirm any school requirement
before finalizing a school-based exception; research is not a blanket exception.

## Manual closeout checklist — future work requiring authorization

1. Record final acceptance, including accepted revisions, the calculated target date,
   and the discontinuation decision. Assign an authorized operator and reviewer;
   confirm continued inbox monitoring and access after service closure.
2. Confirm closure arrangements for Events, registrations, payments, unresolved
   requests, new submissions, and reminder processing. Deactivation, cancellation,
   and inactive assessments retain information and do not constitute cleanup.
3. Inventory independent Local/Supabase and other authorized environment copies.
   Map Users/Identity records, Events, all registration and assessment attempts,
   prediction/SHAP snapshots, feedback, post-event outcomes, consent fields, and
   reminder records. Check applied foreign keys, cascades, restrictive organizer and
   Trail relationships, and registration links before planning removal order.
4. Resolve and document justified exceptions before removal. Identify approved
   evaluation/report evidence that still needs retention; assess aggregation and
   re-identification risks. Removing source records changes report totals and
   prediction/outcome provenance. Retained identifiers or lookup keys alone do not
   establish anonymization. Do not alter frozen model artifacts as routine cleanup.
5. Inventory file locators **before row removal**: database deletion does not invoke
   object deletion. Reconcile receipts, medical clearances, profile/trail images,
   and orphan uploads against the correct namespace/provider. Review managed local
   storage, Supabase objects, legacy web-root files, and shared/snapshot cover
   references separately. Existing managed-object deletion deliberately excludes
   legacy files and other namespaces; missing references are not proof of removal.
6. Inventory downloaded CSV/PDF reports, document downloads, screenshots, project
   exports, mail copies, and any other copies under project control. Confirm actual
   application/provider log retention, database backups/PITR, file backups, replicas,
   versions, permissions, deletion options, and expiry. Do not assume deleting the
   web app or Azure resource group erases Supabase or external copies. Record any
   provider-controlled residual copies and expected expiry. Any temporary recovery
   copy needs restricted access and its own disposal decision. Plan how cleanup
   would be preserved or repeated if an older backup is restored.
7. Validate a relationship-aware procedure on synthetic data before separately
   authorizing real cleanup. After authorized execution, reconcile records and
   objects, verify approved retained evidence, and record completion date, scope,
   operator/reviewer, aggregate counts, exceptions, failures, and residual-copy
   follow-up. Do not copy participant details into the completion record.

## Unresolved operational prerequisites

- Actual provider/environment inventory, backup/log configuration and expiry,
  replica/version/copy status, and permissions remain unverified. Repository defaults
  and historical deployment reports do not prove current production settings.
- Applicable school requirements, exception evidence, operators' access, and the
  cleanup procedure's correctness and workload still require confirmation.
- This document authorizes planning only. Do not access real records/providers or
  execute cleanup on the strength of this notice change.

## Consent and source references

This revision is an informational clarification of contact, existing handling, and
the conditional plan. It does not change `ParticipantProfileConsent.CurrentNotice`,
its fingerprint, stored acceptances, assessment acknowledgements, or enforcement.
The profile fingerprint hashes the modal notice and link fields, not the content
of the linked Privacy Policy. Existing acceptance is not represented as acceptance
of a newly versioned full policy. Future material changes need an explicit notice
and version decision; not every wording change requires renewed consent.

Sources: [Privacy Policy](Views/Home/Privacy.cshtml),
[profile consent](Services/ParticipantProfileConsent.cs),
[public image route](Controllers/MediaController.cs),
[upload references](Services/UploadReference.cs), [storage behavior](STORAGE.md),
[database relationships](Data/ApplicationDbContext.cs),
[schema snapshot](Migrations/ApplicationDbContextModelSnapshot.cs),
[named CSV export](Controllers/RecordsController.cs),
[business reports](Services/BusinessReportService.cs), and
[recorded deployment limits](AZURE_DEPLOYMENT.md).

Earlier contact/retention placeholders in project context documents are superseded
by the approved decisions recorded here; they do not establish operational readiness.
