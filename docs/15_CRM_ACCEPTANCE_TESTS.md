# 15 — CRM Acceptance Tests

## A. Authentication boundary

### A1 — Consumer identities cannot enter CRM

**Given** a valid Buyer, Seller, Web cookie, or mobile bearer session

**When** it is presented to a CRM route

**Then** CRM does not authenticate it
**And** no CRM session or user is created.

### A2 — Unknown Entra identity is denied

**Given** Entra authenticates a user from the configured tenant
**And** no active matching `crm.users` row exists

**When** the OIDC callback completes

**Then** CRM denies access
**And** writes a safe append-only denied-auth event
**And** discloses no dispute data.

### A3 — Wrong tenant is denied

**Given** an identity token has an issuer or tenant other than the configured
workforce tenant

**When** authentication is attempted

**Then** validation fails before local role assignment or session creation.

### A4 — Disabled user loses access

**Given** an active Admin has a CRM session

**When** a SuperAdmin disables that user

**Then** all server-side CRM sessions for the user are revoked
**And** the user cannot open another authorized page
**And** the action is audited.

### A5 — CRM roles are local application roles

**Given** an active CRM user has only the `Admin` role

**When** authorization is evaluated

**Then** the user satisfies `DisputeReviewer`
**And** does not satisfy `DisputeResolver` or `CrmAccountAdministrator`.

**Given** an active CRM user has `SuperAdmin`

**Then** the user satisfies all three policies.

## B. Account administration

### B1 — Only SuperAdmin manages accounts

**Given** an Admin

**When** it attempts to invite, elevate, disable, or reactivate a CRM user

**Then** access is denied and no account state changes.

### B2 — Last SuperAdmin cannot be disabled

**Given** exactly one active SuperAdmin remains

**When** any actor attempts to disable or remove its SuperAdmin role

**Then** the operation is rejected and audited.

### B3 — SuperAdmin elevation needs two approvers

**Given** an Admin is proposed for SuperAdmin

**When** fewer than two distinct active SuperAdmins approve

**Then** the role remains unchanged.

## C. Database isolation

### C1 — CRM-owned tables use the CRM schema

**Given** the initial CRM migration

**When** its relational model is inspected

**Then** every CRM entity maps to schema `crm`
**And** the migration history is `crm.__ef_migrations_history`
**And** no CRM-owned table is created in `public`.

### C2 — CRM workflow does not replace transaction state

**Given** a CRM case changes assignment or workflow status

**When** that change is saved

**Then** no transaction financial state changes
**And** no provider instruction is created.

## D. Dispute review

### D1 — Opening dispute blocks payout atomically

**Given** a buyer may open a supported dispute before the exact deadline

**When** the dispute command commits

**Then** the transaction enters the dispute path
**And** payout is blocked
**And** the audit and notification intent commit atomically.

### D2 — Admin recommends but cannot approve

**Given** an Admin completed review

**When** it submits a valid outcome, reason, and rationale

**Then** one immutable recommendation enters `ReadyForApproval`
**And** no refund or payout instruction is created.

### D3 — Self-approval is rejected

**Given** a CRM user created a recommendation

**When** the same CRM user attempts final approval, regardless of current role

**Then** approval is rejected
**And** no domain transition or provider instruction occurs.

### D4 — SuperAdmin approval is idempotent

**Given** a different SuperAdmin approves a valid recommendation

**When** the request is repeated or races with another approval

**Then** at most one authorized domain transition occurs
**And** at most one refund or payout instruction is queued
**And** one immutable transaction audit event identifies the trusted actors,
case, review reference, and rationale.

### D5 — Provider processing is not completion

**Given** a full-refund decision is approved

**Then** the transaction is `REFUND_PENDING`
**And** only a verified matching provider event may mark it `REFUNDED`.

**Given** a full-payout decision is approved

**Then** the transaction progresses through `PAYOUT_ELIGIBLE` and
`PAYOUT_PENDING`
**And** only authenticated provider completion or authorized reconciliation
may mark it `PAID_OUT`.

### D6 — AI cannot bind the outcome

**Given** AI summarizes evidence or recommends missing evidence

**When** no valid two-person human approval exists

**Then** no financial transition or provider instruction can be created.

### D7 — Large dispute queues remain operable

**Given** the CRM queue contains more cases than the selected page size

**When** a reviewer searches, filters by status, ownership, or overdue SLA,
changes the priority sort, or moves between pages

**Then** the filters compose deterministically
**And** the result count and visible range are accurate
**And** overdue cases appear first in the default priority order
**And** changing a filter returns to the first valid page.

## E. Evidence and privacy

### E1 — Sensitive access is audited

**Given** an authorized reviewer opens dispute evidence or private fulfillment
information

**When** access succeeds

**Then** CRM appends a sensitive-access event containing actor, case, resource,
purpose, correlation ID, and server time.

### E2 — Unauthorized evidence is not disclosed

**Given** an unknown, disabled, or insufficiently authorized user

**When** it requests evidence or the protected evidence endpoint

**Then** access is denied
**And** no evidence content or sensitive metadata is returned.

### E3 — Digital secrets are rejected

**Given** submitted digital evidence contains a password, recovery code,
private key, seed phrase, or reusable credential

**When** validation runs

**Then** persistence is rejected
**And** the party is told to use the agreed external handoff channel.

### E4 — Party upload is private and idempotent

**Given** a Buyer or Seller belongs to a disputed transaction

**When** that party uploads one supported image with an idempotency key

**Then** the normalized encrypted file and metadata are recorded once
**And** replay by the same party returns the original evidence
**And** the counterparty cannot list or download it through the consumer API.

### E5 — Stored evidence detects tampering

**Given** an accepted image has been normalized and encrypted with AES-GCM

**When** stored ciphertext or its authentication tag changes

**Then** decryption fails closed
**And** neither the mobile API nor CRM returns the modified content.

### E6 — Evidence follows retention and legal hold

**Given** party evidence belongs to a terminal transaction

**When** the evidence-retention deadline passes without a legal hold

**Then** metadata is purged through the transaction cascade
**And** the encrypted file is queued and deleted idempotently.

**Given** an active legal hold

**Then** neither operation runs until the hold is released.

### E7 — Evidence request reaches the correct party

**Given** an Admin requests evidence from Buyer, Seller, or both

**When** the CRM request is recorded

**Then** one durable notification per target party contains the safe request
detail, transaction deep link, and exact Asia/Bangkok deadline
**And** retrying the same request does not duplicate its audit or
notification.

## F. Accessibility

### F1 — CRM case work is keyboard accessible

**Given** an Admin or SuperAdmin uses the dispute queue or case detail

**Then** all filters, evidence controls, notes, recommendation fields, and
approval actions are keyboard reachable
**And** focus order and headings are logical
**And** status and risk are not communicated by color alone
**And** destructive or financial actions require an explicit confirmation
summary.

### F2 — Queue table is responsive

**Given** the dispute queue is used on a narrow mobile viewport

**Then** each table row becomes a labelled case card without losing fields
or actions
**And** filtering and pagination remain keyboard and touch accessible
**And** programmatic heading focus does not draw a persistent border.

### F3 — Case history is understandable without technical knowledge

**Given** an Admin opens a dispute case

**When** the transaction history is shown

**Then** each event explains in Thai who acted, what happened, when it happened,
and how the transaction stage changed
**And** internal event codes and request identifiers are collapsed by default
**And** expanding technical details never reveals a raw access token or other
reusable identity credential
**And** the immutable transaction audit record remains unchanged.
