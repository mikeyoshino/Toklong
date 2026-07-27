# 12 — CRM Architecture and Access

## Purpose

`Toklong.Crm` is an internal, server-rendered Blazor Web App for authorized
operations staff. Its first scope is dispute review. It is not a customer
application, a replacement for the domain transition service, or a direct
database state editor.

## Deployment boundary

- Deploy CRM separately from the consumer Web and mobile API, normally on a
  dedicated internal hostname.
- Use Blazor Interactive Server. Do not place provider credentials, database
  credentials, evidence URLs, or authorization decisions in browser code.
- Use the existing PostgreSQL database for the MVP, but place every CRM-owned
  table in the `crm` schema.
- Use a separate `CrmDbContext`, migration history table, database principal,
  authentication cookie, and Data Protection application name.
- The transaction aggregate remains the source of truth for payment,
  fulfillment, dispute blocking, refund, and payout states.
- CRM workflow state is operational metadata only. It must not duplicate or
  override `SaleTransaction.State`.

## Workforce identity

The initial workforce identity provider is Microsoft Entra ID Free using a
single-tenant OpenID Connect application.

- Entra authenticates the workforce identity.
- Toklong CRM stores local activation status and application roles.
- Consumer Buyer/Seller identities, phone OTP sessions, and mobile bearer
  tokens never authorize CRM.
- Unknown Entra identities are denied. There is no open or just-in-time
  registration.
- Microsoft Entra Security Defaults must remain enabled while the Free plan is
  used.
- Microsoft Entra ID P1 is a production gate before CRM may execute real
  refund or payout decisions. P1 is required so Conditional Access can be
  applied specifically to the CRM application.
- Microsoft Entra ID P2 or Entra ID Governance, including Privileged Identity
  Management, is a later-stage control and is not claimed for the Free plan.

Official identity references:

- https://learn.microsoft.com/en-us/entra/fundamentals/security-defaults
- https://learn.microsoft.com/en-us/entra/identity/authentication/how-to-mandatory-multifactor-authentication
- https://learn.microsoft.com/en-us/entra/identity/conditional-access/plan-conditional-access

## Roles

### Admin

May:

- View and claim dispute cases.
- Review authorized transaction, payment, fulfillment, and audit information.
- Request evidence from a buyer or seller.
- Add internal case notes.
- Prepare a reasoned `FullRefund` or `FullPayout` recommendation.
- Submit that recommendation for approval.

May not:

- Approve a recommendation they created.
- Execute a refund or payout decision.
- Change provider-confirmed history.
- Change a transaction state directly.
- Create, elevate, deactivate, or reactivate CRM accounts.

### SuperAdmin

May perform Admin work and may additionally:

- Invite, activate, deactivate, or reactivate Admin accounts.
- Return a recommendation for more work.
- Approve a recommendation prepared by a different person.
- Place or release an authorized risk/legal hold through the applicable domain
  command.
- Review security and sensitive-access audit records.

A SuperAdmin who prepared or materially reviewed a recommendation cannot be its
final approver. A second SuperAdmin must approve that case.

## Separation of duties

Every resolution affecting an amount greater than zero uses two-person
approval for the MVP:

```text
Admin recommendation
  -> SuperAdmin approval
  -> authorized domain command
  -> provider instruction
  -> provider-confirmed completion
```

The recommender and approver must have different CRM user IDs. The restriction
is enforced in the application service and by persisted decision data; hiding
a button is not sufficient.

Additional SuperAdmin creation requires approval by two existing
SuperAdmins. The last active SuperAdmin cannot be disabled. Until two
SuperAdmins exist, a SuperAdmin cannot approve a recommendation they created.

## Account lifecycle

1. A SuperAdmin creates an invitation for a specific Entra tenant/object ID and
   expected work email.
2. The invited identity authenticates through the configured single tenant.
3. CRM verifies the Entra issuer, tenant, object ID, local active status, and
   assigned role before issuing its own cookie.
4. Deactivation immediately invalidates server-side CRM sessions.
5. Role changes and account lifecycle actions create append-only auth events.
6. No CRM password, refresh token, OTP, or reusable Entra credential is stored
   in the Toklong database.

One monitored break-glass identity may be maintained outside normal daily use.
Its use is an incident and must create an alert and a reviewed audit record.

## Session baseline

- Cookie name: `toklong.crm.session`.
- Cookie is `HttpOnly`, `Secure` outside local Development, and uses an
  appropriate SameSite setting for the OIDC callback.
- Reject open redirects and accept only local return URLs.
- Sign-out clears the CRM cookie and ends the local server-side session.
- Sensitive resolution approval requires a recently validated CRM session.
- CRM responses containing case or evidence data use `Cache-Control: no-store`.
- Production secrets come from the deployment secret store, never committed
  configuration.

## Database authorization

The production database principal used by CRM must be distinct from the
consumer/API principals.

- CRM owns DML in the `crm` schema.
- Core transaction reads are limited to the fields/views required for case
  work.
- CRM pages do not issue direct SQL/EF updates to transaction financial state.
- Refund/payout decisions execute only through authorized application/domain
  commands that enforce the transition allow-list and append the immutable
  transaction audit event.

## Production gates

CRM may be deployed read-only with Entra ID Free. These gates must pass before
real financial resolution is enabled:

- Entra ID P1 and Conditional Access are configured for the CRM application.
- At least one Admin and two independent SuperAdmins are active.
- Two-person approval, self-approval rejection, concurrency, and idempotency
  tests pass.
- Provider refund and payout completion paths pass signature/reconciliation
  tests.
- Evidence access logging and retention purge cover the CRM-owned case data.
- Operations, legal, privacy, and provider owners approve the dispute policy.
