# 16 — CRM Operations Runbook

## Scope

This runbook deploys the implemented TOKLONG CRM dispute MVP, including
Buyer/Seller image evidence. It does not enable the planned managed-return
outcome, which remains provider- and policy-gated.

## 1. Database

CRM uses the same PostgreSQL database as the transaction applications, with a
separate `crm` schema and migration history.

Generate a reviewable idempotent script:

```bash
dotnet ef migrations script --idempotent \
  --project src/Toklong.Crm/Toklong.Crm.csproj \
  --startup-project src/Toklong.Crm/Toklong.Crm.csproj \
  --context CrmDbContext
```

Generate and review the core migration script as well because party-evidence
metadata belongs to the transaction schema:

```bash
dotnet ef migrations script --idempotent \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --context ToklongDbContext
```

Run the approved script as the deployment/migration role. The CRM runtime
database role must not own the schema or have DDL rights. Do not run migrations
automatically in production.

## 2. Microsoft Entra ID

Use one single-tenant app registration dedicated to CRM:

- Redirect URI: `https://<crm-host>/signin-oidc`
- Use the OpenID Connect code flow.
- No consumer Buyer/Seller account is admitted.
- Add the exact production host; do not use wildcard redirect URIs.
- Store the client secret outside source control.

For internal development, Entra ID Free plus Security Defaults is acceptable.
Keep `FinancialActionsEnabled=false`.

Before real refund or payout decisions:

- Upgrade the workforce tenant to Entra ID P1.
- Apply and test CRM-specific Conditional Access.
- Set `ConditionalAccessApproved=true`.
- Set `FinancialActionsEnabled=true` only after the approval record exists.

## 3. First SuperAdmin

The bootstrap command works only while `crm.users` is empty:

```bash
dotnet run --project src/Toklong.Crm -- \
  --bootstrap-super-admin
```

Supply `CrmBootstrap` tenant ID, object ID, email, and display name through the
deployment secret/configuration system. After bootstrap:

1. Sign in as the first SuperAdmin.
2. Create a second account as Admin.
3. During the one-time initialization ceremony, point `CrmBootstrap` to that
   existing Admin and run:

   ```bash
   dotnet run --project src/Toklong.Crm -- \
     --bootstrap-second-super-admin
   ```

4. The command succeeds only when exactly one active SuperAdmin exists and
   the target is already an active CRM Admin.
5. After two SuperAdmins exist, every later elevation uses the normal request
   and different-SuperAdmin approval flow.
6. Confirm at least two active SuperAdmins before enabling financial actions.

## 4. Dispute workflow

1. Buyer opens a dispute; payout is blocked.
2. CRM synchronizes one case from the authoritative transaction.
3. Admin claims it, reviews the immutable snapshot, records notes, and records
   any evidence request with an exact 48-hour deadline. The notification
   outbox sends each selected party the request and transaction deep link.
4. Admin recommends `FullRefund` or `FullPayout`, a supported reason code, and
   written rationale.
5. A different SuperAdmin approves.
6. The application writes the trusted actors and CRM references into the core
   transaction audit and moves only to `RefundPending` or `PayoutEligible`.
7. Provider-confirmed processing remains responsible for `Refunded` or
   `PaidOut`.

The retired direct dispute-resolution endpoint and script cannot bypass CRM.

### Local Stripe refund validation

Before enabling financial actions in any environment, the development team can
validate the implemented boundary against Stripe Test Mode:

```bash
./scripts/test-stripe-refund.sh
```

The command is local-only. It uses development accounts and simulation,
requires an explicit transaction ID inside its gated CRM command modes, and
still exercises the normal role checks, transition service, audit trail,
provider refund, webhook/reconciliation, and notification paths. It rejects
live Stripe keys and never validates production Entra Conditional Access.

## 5. Evidence storage

- Set `DisputeEvidence:StoragePath` to an absolute persistent path shared by
  API, CRM, and retention Worker.
- Set `DisputeEvidence:EncryptionKeyBase64` from secret storage to one
  base64-encoded 32-byte key. Do not commit or log it.
- Grant the three runtime processes only the file permissions they need.
- Back up encrypted files and the encryption key under separate access
  controls. Losing the key makes retained evidence unreadable.
- Rotate the key only with an approved re-encryption procedure; changing the
  configured key alone breaks existing files.
- Confirm the storage path and key are identical on API, CRM, and Worker
  before rollout.

## 6. Operational checks

- `/health/live` confirms the process is alive.
- `/health/ready` confirms CRM database connectivity.
- Unknown, disabled, or role-less Entra users must be denied.
- Disabling a user must revoke all active CRM sessions.
- Review `crm.sensitive_access_events`, `crm.case_events`, and core transaction
  audit events during incident investigation.
- A Buyer/Seller can list only its own evidence; every CRM image view appends
  `party_dispute_evidence` to `crm.sensitive_access_events`.
- Evidence responses contain `Cache-Control: no-store` and a restrictive
  content security policy.
- Repeated approval must not create a second financial transition.

## 7. Rollout gate

Run:

```bash
dotnet test tests/Toklong.Domain.Tests
dotnet test tests/Toklong.Application.Tests
dotnet test tests/Toklong.Api.Tests
dotnet test tests/Toklong.Crm.Tests
dotnet test tests/Toklong.Mobile.Core.Tests
dotnet ef migrations has-pending-model-changes \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --context ToklongDbContext
dotnet ef migrations has-pending-model-changes \
  --project src/Toklong.Crm/Toklong.Crm.csproj \
  --startup-project src/Toklong.Crm/Toklong.Crm.csproj \
  --context CrmDbContext
```

Do not deploy real financial actions while any test fails, the workforce
Conditional Access gate is incomplete, or the refund/payout provider
reconciliation capability is unavailable.
