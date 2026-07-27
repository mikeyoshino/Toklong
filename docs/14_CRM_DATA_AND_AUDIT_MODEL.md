# 14 — CRM Data and Audit Model

## Ownership

Core transaction tables retain their existing names and ownership.
`Toklong.Crm` owns only tables in PostgreSQL schema `crm`.

The CRM migration history table is:

```text
crm.__ef_migrations_history
```

CRM workflow data is not a financial ledger and is never used as proof that a
provider completed payment, refund, or payout.

## Initial identity tables

### `crm.users`

```text
id uuid primary key
entra_tenant_id text not null
entra_object_id text not null
email text not null
display_name text not null
status text not null
created_at timestamptz not null
created_by_user_id uuid null
disabled_at timestamptz null
disabled_by_user_id uuid null
version bigint not null

unique (entra_tenant_id, entra_object_id)
unique (email)
```

Store stable Entra identifiers, not passwords, tokens, OTPs, or authentication
secrets.

### `crm.roles`

```text
id uuid primary key
name text not null unique
```

Initial immutable names are `Admin` and `SuperAdmin`.

### `crm.user_roles`

```text
user_id uuid not null
role_id uuid not null
assigned_at timestamptz not null
assigned_by_user_id uuid null

primary key (user_id, role_id)
```

### `crm.sessions`

```text
id uuid primary key
user_id uuid not null
ticket_hash text not null unique
created_at timestamptz not null
last_validated_at timestamptz not null
expires_at timestamptz not null
revoked_at timestamptz null
version bigint not null
```

Only a one-way session-ticket hash is retained.

### `crm.auth_events`

Append-only events for invitation, first sign-in, denied sign-in, role
assignment/removal, activation/deactivation, session creation/revocation, and
break-glass use.

## Implemented dispute-workflow tables

### `crm.dispute_cases`

One row per transaction dispute:

```text
id uuid primary key
transaction_id uuid not null unique
case_number text not null unique
workflow_status text not null
assigned_user_id uuid null
opened_at timestamptz not null
assignment_due_at timestamptz not null
first_review_due_at timestamptz not null
ready_for_approval_at timestamptz null
approval_due_at timestamptz null
closed_at timestamptz null
version bigint not null
```

`workflow_status` never replaces the transaction state.

### `crm.case_assignments`

Append-only assignment history with case, assignee, assigning actor, time,
and reason.

### `crm.case_events`

Append-only case workflow events with actor, event name, previous/next workflow
status, correlation ID, idempotency key, metadata, and server time.

### `crm.case_notes`

Internal append-only notes. Corrections create another note. Notes are never
shown to transaction parties unless a separate approved disclosure action
copies suitable content.

### `crm.evidence_requests`

Records the target party, requested evidence types, exact 48-hour deadline,
and requester. The request queues an idempotent core notification for each
target party; notification outbox rows carry the safe request detail and exact
action deadline. Automatic matching of a submission to a particular request,
completion, and extension records remain explicit follow-up workflow events.

### Core `dispute_evidence`

Party uploads remain owned by the transaction domain in the core schema:

```text
id uuid primary key
transaction_id uuid not null
party text not null
submitted_by_id uuid not null
evidence_type text not null
description text not null
storage_reference text not null
content_type text not null
length_bytes bigint not null
sha256 text not null
idempotency_key text not null
submitted_at timestamptz not null

unique (transaction_id, party, idempotency_key)
```

The file itself is a normalized JPEG encrypted outside PostgreSQL with
AES-256-GCM. The encryption key comes from deployment secret storage, never
configuration committed to source control. Buyer/Seller endpoints disclose
only evidence belonging to the authenticated party.

### `crm.resolution_actions`

```text
id uuid primary key
case_id uuid not null
recommendation text not null
reason_code text not null
rationale text not null
recommended_by_user_id uuid not null
recommended_at timestamptz not null
approved_by_user_id uuid null
approved_at timestamptz null
review_reference text not null
idempotency_key text not null unique
applied_at timestamptz null
status text not null
version bigint not null
```

Database/application validation rejects equal recommender and approver IDs.
The domain audit event remains the authoritative financial transition record.
The CRM case remains open after a decision is applied and closes only after
the core transaction records provider-confirmed `Refunded` or `PaidOut`.

### `crm.sensitive_access_events`

Append-only evidence and sensitive-record access with user, case, resource
type/reference, purpose, server time, and correlation ID.

### `crm.role_change_requests` and `crm.account_events`

SuperAdmin elevation is requested by one active SuperAdmin and applied only
after approval by a different active SuperAdmin. Account events are
append-only. Disabling an account revokes every active server-side session in
the same database commit.

## Constraints

- All CRM money values, if ever cached for search/display, are integer satang
  plus ISO currency.
- Concurrency tokens protect account, session, case, and resolution updates.
- Stable idempotency keys have unique constraints.
- Recommendations and approvals are append-only once submitted.
- No CRM table stores raw payment credentials, provider secrets, refund bank
  accounts, OTP values, digital credentials, or private keys.
- Evidence endpoints are authenticated, non-cacheable, scoped to the
  transaction/case, and never expose the storage reference.
- Logs contain internal references, not raw evidence or personal data.

## Retention

Case workflow, notes, resolution actions, evidence requests, party evidence
metadata/files, and access events belong to the transaction evidence
aggregate. They follow the five-year transaction/dispute retention rule and
legal hold.

`crm.dispute_cases.transaction_id` has a database foreign key to the core
transaction with cascade deletion. CRM case-owned rows cascade only when the
authorized core retention purge removes the transaction; CRM exposes no
ordinary case-delete action.

Before the transaction row is removed, the retention job queues each encrypted
party-evidence storage reference. The file-deletion stage removes the managed
file idempotently; a legal hold blocks both metadata and file purge.

The retention worker must delete the CRM case aggregate in the same controlled
purge operation as its transaction. Identity account/auth-event retention is a
separate workforce privacy and security schedule and must not retain
transaction evidence after the transaction purge.

## Migration and database roles

- `CrmDbContext` uses `MigrationsHistoryTable("__ef_migrations_history",
  "crm")`.
- Production migration runs as a dedicated deployment operation.
- Runtime CRM credentials have no schema-owner or DDL rights.
- CRM runtime DML is limited to `crm`.
- Required core reads should use narrowly scoped queries/views.
- Financial state writes remain in authorized core application commands.
