# Account Name Change Round 1 Hardening Design

## Scope

This addendum refines the approved account-name-change design after Task 5
review. It does not add API or UI behavior. It closes four correctness and
privacy gaps: provider verification recovery, same-phone phantom writers,
recoverable protected audit evidence, and unbounded active-session loading.

## Durable provider verification

An account-name verification first creates and commits an
`AccountNameVerificationOperation` before calling the OTP provider. The
operation is unique by challenge and client verification idempotency key and
stores the submitted-code HMAC, a generated 32-character provider
verification key, expected normalized phone, purpose, provider challenge
identifier, lifecycle status, and provider evidence timestamps.

The OTP provider contract adds:

- idempotent verification using the durable provider verification key;
- authoritative verification lookup using that key;
- evidence binding the exact key, purpose, normalized phone, challenge,
  outcome, requested time, and completion time;
- a `SupportsVerificationLookup` capability.

For a pending operation, the handler performs authoritative lookup first. If
no outcome exists, it may call verification again only with the same durable
provider key. A lost provider response is reconciled by lookup. Exact
concurrent callers converge on the one operation and evidence. A different
code digest for the same client key is rejected before provider access.

Production may enable account-name change only when send lookup and
verification lookup are both certified and configured. Provider evidence that
does not exactly match the operation is rejected as an unknown outcome.

## Same-phone transaction serialization

All current-name writers use `IAccountPhoneTransactionManager`. The Npgsql
implementation:

1. starts an explicit database transaction;
2. takes `pg_advisory_xact_lock(hashtextextended(normalized_phone, 0))`;
3. keeps the transaction and lock alive through authoritative reads,
   `SaveChangesAsync`, and explicit commit;
4. rolls back on disposal unless committed.

PostgreSQL derives the signed 64-bit key deterministically from the normalized
phone. Hash collisions can serialize unrelated accounts but cannot weaken
correctness.

The following paths participate:

- account-name completion;
- seller creation/ensure flows;
- buyer registration when a same-phone seller may already exist;
- mobile-session creation;
- attaching a seller to a mobile session.

After acquiring the lock, each path re-reads the authoritative buyer and/or
seller. New roles inherit the existing same-phone account name. New or
modified sessions derive their display name from the current account, not
from a previously materialized profile.

### Mobile registration composition refinement

Mobile registration and its first session are one atomic same-phone operation.
The API performs a no-tracking ticket-to-phone preflight only to select the
lock key; that lookup cannot authorize, consume, or mutate registration state.
After acquiring the outer phone lease, the completion handler re-reads the
pending registration and revalidates the ticket, installation, idempotency key,
and normalized phone under the lock. It also re-reads a same-phone seller and
uses that seller's structured account name for a new buyer when present.

The scoped transaction manager is reentrant only for the same normalized phone
and DbContext. The first lease owns the EF transaction and PostgreSQL advisory
lock. Same-phone nested leases participate without starting another database
transaction; their commits mark successful participation but cannot physically
commit. Leases close in LIFO order, and only the outer lease may commit the
database transaction after all nested leases have committed and disposed. A
different-phone nested begin, out-of-order use, double commit, or commit after
an uncommitted nested disposal is rejected. Any uncommitted lease poisons the
outer scope, whose disposal rolls the transaction back.

`CompleteMobileRegistrationHandler` and `MobileSessionTokenService.CreateAsync`
both acquire nested same-phone leases. Their `SaveChangesAsync` calls therefore
remain inside the outer transaction, so buyer creation, terms acceptance,
pending-registration consumption, and first-session creation commit or roll
back together. Npgsql retains the advisory-lock SQL; SQLite exercises the same
ownership/reentrancy state machine with a real relational transaction but no
PostgreSQL advisory function.

## Protected audit evidence

New audit events store one authenticated encrypted payload:

```json
{
  "oldBuyerName": "normalized value or null",
  "oldSellerName": "normalized value or null",
  "newName": "normalized value"
}
```

`AccountNameAuditEvidenceProtector` uses ASP.NET Core Data Protection with the
fixed purpose `Toklong.AccountNameAuditEvidence.v1`. The row stores ciphertext
as `bytea` in `ProtectedNameEvidence` and the explicit metadata value
`aspnet-dp:v1` in `ProtectionVersion`. Plaintext names never enter logs,
analytics, attempt rows, or unprotected audit columns.

Application code receives only a writer interface. No reader is registered in
consumer API dependency injection. Tests use the infrastructure protector's
authorized decode seam to prove round-trip and ciphertext opacity.

The migration renames existing irreversible `OldName` and `NewName` columns to
`LegacyOldNameDigest` and `LegacyNewNameDigest`. Existing rows remain honest
legacy evidence; their original names cannot be reconstructed. New rows use
protected evidence and leave legacy digest fields null.

## Active-session query

`GetActiveByPartyAsync` translates party membership, `RevokedAt == null`, and
`ExpiresAt > now` into SQL. The handler retains the defensive normalized-phone
check before updating returned sessions. Expired and revoked rows are neither
materialized nor modified.

## Error and recovery behavior

- A known rejected provider outcome records the existing incorrect/locked
  result.
- A known verified outcome enters the serialized completion transaction.
- A missing or invalid authoritative outcome leaves the durable operation
  pending and returns the existing safe unknown-outcome error.
- A process retry resumes the pending operation through lookup and the same
  provider verification key.
- Transaction conflicts reload the authoritative operation/challenge and do
  not generate a new provider key.

## Test strategy

- Single-use provider interleaving where one accepted response is lost and an
  exact retry recovers by lookup.
- Same key/different digest and mismatched provider evidence rejection.
- Relational blocking races for same-phone seller and active-session insertion.
- Data Protection round-trip and ciphertext-not-plaintext assertions.
- Migration/model assertions for operation constraints and honest audit
  columns.
- Relational repository test proving inactive sessions remain untouched.
- Existing immutable transaction tests plus later seller-acceptance snapshot
  evidence.
- Real-manager SQLite composition proving seller-name inheritance, identical
  first-session name, one outer commit, and full rollback when outer commit is
  omitted.
- Same-phone reentry, different-phone rejection, LIFO, poisoned-outer, and
  cancellation/disposal safety checks on the scoped manager.
