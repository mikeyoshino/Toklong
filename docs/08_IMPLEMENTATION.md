# 08 — MVP Implementation

## Architecture

The implementation targets .NET 10. It includes a Blazor Web App with interactive
server rendering and a separate native .NET MAUI XAML application for Android,
iOS, and a Mac Catalyst development/desktop target.

- `Toklong.Domain`: transaction aggregate, allow-listed transitions, authorization checks, product policy, immutable audit and external events.
- `Toklong.Application`: feature folders containing MediatR commands, queries, and handlers.
- `Toklong.Infrastructure`: EF Core, PostgreSQL, repositories, HMAC verification, and manual payout adapter.
- `Toklong.Web`: Thai mobile-first pages, reusable Razor form components,
  signed internal reconciliation endpoints, and the deadline worker.
- `Toklong.Api`: separate mobile HTTP boundary, phone authentication, rotating
  sessions, transaction actions, Stripe PaymentIntent creation, and the
  signature-verified Stripe webhook.
- `Toklong.Worker`: the single owner of deadline evaluation, SHIPPOP booking
  confirmation/tracking/cancellation, payment/refund reconciliation, payout
  instruction creation, and notification-outbox dispatch. Web and API do not
  run these loops.
- `Toklong.Mobile`: native XAML pages, buyer/seller transaction workspace,
  mobile state presentation, SecureStorage session handling, and native
  PaymentSheet coordination.
- `tests`: domain transition, application/infrastructure, API security/webhook,
  and mobile-core tests.

Dependencies point inward: Web, API, and Infrastructure depend on Application;
Application depends on Domain. Mobile consumes authenticated HTTP contracts and
does not reference Infrastructure or provider secrets.

## Start locally

PostgreSQL is the only Docker service:

```bash
docker compose up -d postgres
dotnet run --project src/Toklong.Web/Toklong.Web.csproj
dotnet run --project src/Toklong.Api/Toklong.Api.csproj
```

The API applies committed EF Core migrations on startup. Launch profiles print
the local URLs.

For a Linux staging or controlled closed-beta host, use
`compose.linux.yml` and `deploy/README.md`. That topology runs migrations as a
one-shot container before starting Web/API/Worker; normal Production startup
fails if `Database:ApplyMigrations` is enabled.

For the native app, install the .NET MAUI workload and build the desired target:

```bash
sudo dotnet workload install maui
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-android
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-maccatalyst
```

If the system SDK has no MAUI workload, install it with the required local
administrator access or use a user-owned SDK. See `docs/10_MOBILE_APP_SPEC.md`
for navigation, the Stripe boundary, and the remaining mobile invitation
boundary.

Before accepting a real transfer, configure these values through environment variables or a local uncommitted settings file:

```text
ManualBank__BankName
ManualBank__AccountName
ManualBank__AccountNumber
Reconciliation__SigningSecret
Stripe__Enabled
Stripe__LiveMode
Stripe__EnableDigitalGoods
Stripe__PublishableKey
Stripe__SecretKey
Stripe__WebhookSecret
BuyerProtectionFee__Enabled
BuyerProtectionFee__MinimumFeeSatang
BuyerProtectionFee__MinimumItemPriceSatang
BuyerProtectionFee__MaximumItemPriceSatang
BuyerProtectionFee__PolicyVersion
BuyerProtectionFee__Tiers__0__UpToItemPriceSatang
BuyerProtectionFee__Tiers__0__RateBasisPoints
BuyerProtectionFee__Tiers__1__UpToItemPriceSatang
BuyerProtectionFee__Tiers__1__RateBasisPoints
BuyerProtectionFee__Tiers__2__UpToItemPriceSatang
BuyerProtectionFee__Tiers__2__RateBasisPoints
ShippingQuotes__Provider
Shippop__BaseUrl
Shippop__ApiKey
Shippop__AccountEmail
Shippop__QuoteSigningSecret
Shippop__QuoteLifetimeMinutes
Shippop__ServiceCodes__0
Otp__Provider
Otp__BaseUrl
Otp__ApiKey
Otp__ApiSecret
BankPayout__Provider
BankPayout__BaseUrl
BankPayout__ApiKey
BankPayout__AllowManualInProduction
Notifications__Enabled
Notifications__BaseUrl
Notifications__ApiKey
DataProtection__KeysPath
DataProtection__CertificatePath
DataProtection__CertificatePasswordFile
ProductImages__StoragePath
PublicUrls__WebBaseUrl
MobileLinks__AppleTeamId
MobileLinks__AndroidSha256Fingerprints__0
```

The committed payment values are intentionally disabled or blank. Persist the
Data Protection key ring outside an ephemeral container, encrypt it with the
configured PFX certificate, and restrict access to the application identity.
The certificate and password-file paths are required for Production Web/API
startup. Product images must also use persistent storage; the single-host
Compose topology shares that path between Web and API. Do not put a signing
secret, Stripe secret, SHIPPOP key, certificate, password file, or key ring in
source control. Production requires `ShippingQuotes__Provider=Shippop`, the
HTTPS SHIPPOP base URL, account email, API key, and a random quote-signing
secret of at least 32 characters.

## Stripe Test Mode end-to-end check

The repository includes a real Stripe sandbox smoke test. It does not mock the
Stripe API or treat a client result as payment proof.

Prerequisites:

- PostgreSQL/Docker, .NET SDK, `curl`, `jq`, and Stripe CLI.
- Stripe CLI authenticated to the intended Stripe sandbox/Test Mode account.
- No process listening on port `5182`, or set
  `TOKLONG_STRIPE_TEST_PORT` to another free port.

Run:

```bash
./scripts/test-stripe-payment.sh
```

The script builds `Toklong.Api`, starts an isolated API process, reads Test Mode
credentials from the environment or the authenticated Stripe CLI config,
starts `stripe listen`, and then exercises this exact path:

1. Create authenticated buyer and seller test accounts.
2. Create and accept one ฿1,000 physical-item offer and verify the 59 THB
   `buyer-protection-v2` fee plus the selected shipping charge.
3. Ask the backend to create the Stripe PaymentIntent.
4. Confirm it with Stripe's `pm_card_visa` test payment method.
5. Forward `payment_intent.succeeded` with a Stripe CLI signing secret.
6. Poll the authenticated transaction API until the state is
   `PaidAwaitingShipment`.

The test fails closed unless the key pair is `sk_test_*` and `pk_test_*`.
Client secrets and API keys are never printed or committed. A completed
PaymentSheet or CLI command is not accepted as proof; only the
signature-verified webhook changes the transaction to paid. The test process
and webhook listener are stopped automatically when the command ends.

To exercise the complete real Test Mode refund path, run:

```bash
./scripts/test-stripe-refund.sh
```

This uses the same live sandbox PaymentIntent flow and continues through:

1. Provider-confirmed development shipment and delivery.
2. A Buyer `NotAsDescribed` dispute, which blocks payout immediately.
3. Three distinct local CRM workforce identities: an Admin claims/reviews the
   case, one SuperAdmin recommends a full refund, and another SuperAdmin
   approves and applies it.
4. A `RefundPending` instruction for the immutable total paid by the Buyer.
5. `Toklong.Worker` creates the matching full refund through Stripe.
6. A signature-verified `refund.updated` webhook or authorized provider
   reconciliation changes the transaction to `Refunded`.
7. A final verifier checks the closed CRM case, applied resolution, core audit
   events, Stripe external event, and Buyer/Seller notification intents.

The CRM command modes used by this script are available only when
`DevelopmentAccess__Enabled=true` and a specific
`DevelopmentRefundTest__TransactionId` is supplied. They call the normal CRM
operations and authorization boundaries; they do not expose an HTTP test
endpoint or write transaction state directly. The script rejects live keys,
does not initiate seller payout, and stops its API, Worker, and Stripe listener
processes on exit.

For interactive testing in the iOS or Android app, stop the normal development
API and run:

```bash
./scripts/run-stripe-test-api.sh
```

This keeps `Toklong.Api` on the mobile app's expected port `5181` and runs the
Stripe webhook listener alongside it. The mobile app receives only the
publishable key and PaymentIntent client secret; the secret key and webhook
secret stay in the API process environment. This command also enables the
Development-only demo simulation: the deterministic managed-shipping provider
confirms the reserved booking after payment, then advances tracking to
in-transit and delivered one backend step at a time; after the buyer taps
`ตรวจแล้ว ทุกอย่างเรียบร้อย`, the already-created manual-bank payout becomes paid out on
the next step. No carrier or payout command is needed during the demo. Stop the
command with `Ctrl+C`.

As of 2026-07-27, new offers use snapshot schema version 8. The product photo is
optional and is represented by its managed reference or explicit `null`; when
supplied it remains immutable. For physical goods, offer creation resolves and
locks the private full delivery address. Before accepting, the seller supplies
a saved-or-new full origin, transaction-specific package weight/dimensions, and
one provider-validated shipping quote. SHIPPOP acceptance creates an
unconfirmed booking and version 7 retains its purchase/tracking references,
reservation time, and structured private address parts. The shared core
includes only origin and destination province/postal values plus parcel
measurements, carrier/service, quote/booking metadata, item price, shipping
charge, and buyer total. Authenticated seller acceptance
creates canonical agreement-core and terms JSON plus separate SHA-256 hashes
and an append-only seller acceptance. Authenticated buyer acceptance validates
that unchanged core, appends a buyer acceptance pointing to the same hash, and
creates the checkout/product snapshot referencing both locked address records.
Checkout does not accept address changes. Provider-confirmed payment for the
buyer total seals the snapshot and unlocks the full destination for seller
fulfillment. Version 8 also freezes the exact buyer-funded Buyer Protection fee.
OTP values and reusable credentials are never stored. Schema
versions 1–7 remain readable without inventing historical party acceptances,
addresses, or regions.

`ShippingQuotes:Provider=Development` enables only the deterministic in-memory
local managed-shipping adapter. It is never selected by default for production
and does not represent SHIPPOP pricing. Its 4×6 HTML label contains the locked
sender/recipient data, prepaid status, weight, service, tracking number, and a
deterministic Code 39 barcode so the complete native viewer can be exercised
without live credentials. `ShippingQuotes:Provider=Shippop`
selects the HTTPS adapter for price lookup, signed request-bound quote
validation, unconfirmed booking, post-payment confirmation, label creation,
tracking polling, and cancellation. The documented provider callback is not
accepted because it has no verifiable signature; the Worker reconciles
server-to-provider every 15 seconds instead. Live activation still requires a
contracted/funded SHIPPOP account and credentials.

Authenticated buyers and sellers can download deterministic JSON evidence with
the shared hashes and acceptance times, or an HTML rendering for printing or
saving as PDF. The evidence excludes OTP values, reusable credentials, the full
delivery address, IP addresses, and device identifiers.

Terminal states now create a five-year evidence-retention schedule. The Worker
runs retention once at startup and every 24 hours in batches of 100, capped at
1,000 records per pass. It excludes active legal holds and atomically replaces
each expired transaction aggregate with a minimized financial tombstone. That
tombstone excludes party, address, item, photo, snapshot, acceptance, and hash
data and expires seven years after the terminal time. Migration
`TransactionRetentionLifecycle` backfills existing terminal transactions from
their provider/audit timestamps.

Managed product photos use a deletion outbox committed with the purge. File
deletion accepts only the configured managed-media path (including its absolute
Web URL form), rejects path traversal, treats an already-missing file as
success, and retries by leaving the outbox row when storage deletion fails.

The OTP rate limiter does not retain a raw IP address. It derives a keyed HMAC
partition from the network address using a random key that exists only for the
current API process; neither the raw address nor the derived partition is
persisted or logged. Caddy access logging is disabled in the supplied
deployment configuration and must remain disabled unless Privacy approves a
separate logging policy.

Retention operations use the reconciliation HMAC secret:

```bash
./scripts/manage-retention.sh preview 100
./scripts/manage-retention.sh hold TRANSACTION_ID CASE-001 "court preservation request"
./scripts/manage-retention.sh release TRANSACTION_ID CASE-001
```

Preview and legal-hold operations require a fresh signed request. Hold/release
write immutable audit events and are replay-safe by reference. Deletion has no
HTTP endpoint; only the internal Worker executes it.

New physical offers store a fixed 72-hour inspection duration inside the
accepted agreement core. Trusted carrier delivery copies that
duration into the exact start/end timestamps. Existing rows migrated from the
former rule retain 168 hours so a paid agreement is not shortened
retroactively. Financial release is blocked if a versioned snapshot or its
required acceptance evidence no longer matches the stored hashes.

`DevelopmentDemoSimulation:Enabled` is false by default and is accepted only
when `ASPNETCORE_ENVIRONMENT=Development`. The worker uses the normal
idempotent carrier and payout reconciliation commands and never bypasses buyer
receipt confirmation. Enabling it in Testing, Staging, or Production fails
startup.

Seller payout is intentionally outside this Stripe slice. The seller's bank
account remains part of acceptance and the immutable payout instruction, but an
operator transfers funds through the bank manually. The transaction must stay
`PayoutPending` until authorized bank reconciliation confirms that transfer;
the Stripe payment smoke test never starts or confirms a payout.

## Manual reconciliation without an admin page

The app never accepts a slip, browser redirect, or ordinary client action as proof of payment, delivery, or payout. In development, an authorized operator can send signed events:

```bash
./scripts/reconcile-event.sh payment TRANSACTION_ID bank-event-001
./scripts/reconcile-event.sh carrier TRANSACTION_ID carrier-event-001 in_transit
./scripts/reconcile-event.sh carrier TRANSACTION_ID carrier-event-002 delivered
./scripts/reconcile-event.sh payout TRANSACTION_ID payout-event-001
```

Set `TOKLONG_SIGNING_SECRET` when it differs from the development-only value. Every endpoint verifies HMAC-SHA256, persists a unique provider/event ID, and safely returns `AlreadyProcessed` for replayed events.

This mechanism is for the first manual operation only. Before production, replace it with an approved bank/payment-provider reconciliation source and a trusted carrier integration.

## Reusable UI components

All main forms use the shared components under `Components/Shared`:

- `TextField`, `TextAreaField`, `SelectField`, `MoneyField`, `CheckboxField`
- `PrimaryButton`, `StatusBadge`, `AlertMessage`
- `TransactionSummary`, `TransactionDetails`, seller and buyer state cards

Money inputs remain strings in the form and are parsed to integer satang at the command boundary. No floating-point money enters the domain.

## Current boundaries

- One fixed physical item, buyer, seller, payment, shipment, and payout in THB.
- Buyer-first is the only new-transaction path: buyer signs in by phone, supplies first/last name, and creates the complete private offer; seller authenticates by phone, chooses an owned payout account, and accepts or declines without editing.
- Buyer checkout is domain-gated until seller acceptance. The buyer wait page refreshes while open and shows the unchanged buyer-specified record before checkout.
- Seller and buyer acceptance records bind their authenticated account IDs and
  server timestamps to one shared agreement-core hash. Mobile keeps these
  legal/technical records off the normal detail screen; the authenticated
  evidence export service remains available without rendering raw hashes,
  terms-version codes, or acceptance audit rows to everyday users.
- Physical seller acceptance requires a valid server-side shipping quote. The
  seller may keep one saved origin, while package measurements remain
  transaction-specific. The snapshot separates item price, shipping charge,
  buyer total, and seller net; Stripe payment/refund validation uses buyer
  total. SHIPPOP acceptance reserves the exact price and service without
  confirming it; the Worker confirms it only after verified payment and records
  provider-issued tracking through the transition service.
- Seller detail uses one single-line `รายละเอียดสินค้า` accordion. The mobile
  transaction response includes condition and known defects so the expanded
  content shows the actual item facts, hides the deterministic description
  fallback, and omits defect copy when no defect was declared.
- Opaque buyer and seller transaction links remain temporary per-transaction authorization. Seller acceptance additionally requires the authenticated seller session.
- Uploaded product photos are normalized to a managed local asset path in this development slice. Virus scanning, private object storage, and signed asset URLs remain a later slice.
- Linux images include the native SkiaSharp runtime used for photo
  normalization. The single-host topology keeps managed images in a shared
  persistent volume; object storage and signed URLs remain required before
  horizontal scaling.
- The mobile API converts managed root-relative photo paths to absolute HTTP(S)
  URLs on the current API host. Native transaction-detail gradients retain
  non-null loading colors before either buyer or seller transaction data is
  available, avoiding the iOS MAUI gradient-rendering crash.
- Historical seller-first state names remain readable for existing rows, but no command, route, CTA, or acceptance test creates a new seller-first transaction.
- Mobile authentication uses a 15-minute protected access token and a random
  30-day refresh token stored in platform SecureStorage. The database stores
  only the SHA-256 refresh-token hash; refresh rotates once and replay fails.
  Logout revokes access immediately.
- The mobile API returns no buyer/seller transaction access tokens. Its
  authenticated transaction actions authorize the party ID before calling the
  domain transition service.
- PaymentSheet receives a backend-created client secret and publishable key.
  Amount, currency, fee, and idempotency key are server-controlled. Payment is
  confirmed only by a verified, replay-safe Stripe webhook.
- Mobile can create and list buyer offers, pay, download a managed-shipping
  label, record a digital handoff, confirm receipt, open a dispute, claim an
  allow-listed HTTPS/custom
  seller-invitation link through phone authentication, create or update the
  payout account, and accept or decline the immutable buyer offer.
- Mobile exposes one collapsed `ให้ AI ช่วยกรอก` action on offer creation.
  `POST /api/mobile/offers/extract-draft` requires the mobile bearer session,
  applies a six-request-per-ten-minute limiter, accepts text and up to three
  signature-validated JPG/PNG/WebP images, and sends a structured-output request
  through the server-side OpenAI client. It uses a session-derived
  privacy-preserving end-user identifier and disables stored model output.
  Uploaded helper sources remain in memory and are never promoted to an
  optional product-evidence photo. The client previews the draft and fills only
  blank form fields.
- Offer creation uses progressive-disclosure Quick Deal presentation. Seller
  phone, product name, item price, optional product photo, and physical delivery
  address are the initial path; item type is one compact switch row and
  additional details stay collapsed. `ตรวจข้อมูลก่อนส่ง` opens a bottom review sheet where the
  buyer selects condition and sees the defect input only for a defect-bearing
  item. Final creation remains the explicit `ส่งข้อเสนอให้ผู้ขาย` action.
  Optional presentation does not weaken the record: blank description falls
  back to product name, and non-defect conditions store
  `ไม่มีตำหนิที่ผู้ซื้อระบุ`.
- The mobile transaction root uses a persisted top-level `ซื้อ | ขาย` mode
  switch. Each mode filters the spotlight and collection before rendering;
  seller filters separate review, fulfillment, payout wait, and completion.
  Phone-targeted offers appear directly in seller mode. Buyer detail retains
  optional copy/share controls for the same invitation URL, but the list root
  has no generic clipboard-open action and link possession never replaces the
  verified-seller-phone authorization check.
- Universal-link files are configuration-driven. Production deliberately
  returns no association file until the Apple Team ID and Android release
  certificate fingerprint are supplied; no signing identity is fabricated.
- Production OTP can use `Otp:Provider=ThaiBulkSms` with
  `https://otp.thaibulksms.com/`, a server-only API key/secret, a signed opaque
  challenge, bounded timeout, and no stored OTP value. Bank payout and
  notification adapters use HTTPS, server-only
  credentials, bounded timeouts, and idempotency keys. Development adapters do
  not silently become production providers. Startup validation rejects unsafe
  production configuration.
- A missed 72-hour fulfillment deadline enters the full-refund path. Creating a
  Stripe refund remains `RefundPending`; only a signature-verified matching
  `refund.updated` event or authorized Worker reconciliation with status
  `succeeded` becomes `Refunded`. Both paths require matching transaction
  metadata, PaymentIntent, refund reference, full buyer total, and currency.
- PromptPay refund progress retains `requires_action`, `pending`, and the
  provider action/instruction timestamps without adding another transaction
  state. The original PaymentIntent receipt email is sent to Stripe as
  `instructions_email` only when Stripe confirms that the charge used
  PromptPay; card refunds omit that method-specific parameter. TOKLONG stores
  neither a second email copy nor the refund bank account. A first
  `requires_action` event creates one Buyer
  notification and an exact deadline when supplied. Replay is silent; leaving
  and later re-entering `requires_action` creates one new notification for the
  new action cycle. Mobile tells the Buyer to respond directly to Stripe and
  shows `pending` as provider processing. CRM renders both events in plain Thai.
  Signed webhook integration tests cover the complete status cycle. A real
  Test Mode PromptPay ceremony still requires an interactive payment plus
  Stripe's hosted email/bank-details step and remains a go-live verification
  item; synthetic events are not presented as proof of email delivery.
- The API also reconciles pending Stripe payments from PaymentIntent and its
  paid Charge. It uses the Charge timestamp rather than observation time, so a
  delayed webhook cannot incorrectly turn an on-time payment into a late
  payment. A later webhook is recorded without repeating the state transition.
- Payout instruction acceptance remains `PayoutPending`; only signed bank
  reconciliation becomes `PaidOut`. Full-refund/full-payout dispute outcomes
  require the signed internal operations endpoint and create immutable audit
  events. Partial outcomes remain disabled.
- Production carrier reconciliation polls the stored SHIPPOP tracking
  reference, requires the returned carrier to match the locked quote, and
  creates deterministic replay-safe carrier event IDs. Supported SHIPPOP
  service mappings are `EMST → THAIPOST`, `FLE → FLASH`, and
  `KRYX/KRYS → KERRY` (shown as KEX). `shipping` becomes verified in-transit;
  `complete` uses the provider POD time for delivery. Problem/invalid/return
  statuses enter the unverified path. The documented unsigned SHIPPOP callback
  is deliberately not exposed.
- Managed tracking is read-only in mobile and Web. After payment the seller can
  open the authenticated, no-store 4×6 HTML label in a dedicated native
  full-screen viewer, pinch to zoom, and use the OS file/share sheet to save,
  share, or print the unchanged file. The WebView uses a script-disabled,
  navigation-blocked preview copy. Label allocation alone does not satisfy
  `ship_by_at`; the first carrier scan does. Before Stripe
  creates a refund for an unscanned managed shipment, the shipping Worker
  cancels it or records that a scan made cancellation inappropriate.
- `scripts/simulate-carrier-event.sh` remains an HMAC-signed,
  timestamp-bounded, replay-safe internal carrier boundary for local/manual
  integration tests; it is not the SHIPPOP production ingress.
- Notification intents are stored transactionally in a persistent outbox,
  including the exact 24-hour pre-payout reminder. Delivery retries with
  backoff; the system does not claim a message was sent until the configured
  provider returns a reference.
- Buyer offers store a normalized intended-seller phone and a required product
  name. Seller offer read/accept/decline endpoints require the authenticated
  phone to match; the invitation token routes to the record but grants no
  authority by itself. Creation writes `buyer_offer_received` to the same
  durable outbox transaction.
- The authenticated mobile activity feed renders reusable notification
  templates from the outbox. A provider-neutral device endpoint and gateway
  payload carry installation, platform, opaque push token, title, body, and
  deep link. iOS requests permission and uploads its APNs device token on a
  real device after authentication. Production APNs credentials and Android
  Firebase client/sender configuration remain external prerequisites.
- Real identity/KYC, beneficiary-name verification, provider contracts,
  approved bank/carrier mappings, private object storage, and live operational
  credentials remain blocked external capabilities. Code readiness does not
  imply those capabilities are active.

## Operations and health

- `GET /health/live` checks process liveness.
- `GET /health/ready` checks database connectivity without exposing secrets.
- Production Web/API trust one forwarded-header hop only when
  `ReverseProxy:TrustForwardedHeaders=true`; the Compose topology keeps their
  HTTP ports private behind Caddy.
- Data Protection keys survive container replacement. Web and API use separate
  application discriminators and key directories.
- PostgreSQL migrations run in the `migrate` one-shot service. Long-running
  Production processes must set `Database:ApplyMigrations=false`.
- Production startup requires an external OTP provider, notifications,
  restricted hosts, production database credentials, a strong reconciliation
  secret, and mobile-link signing identifiers on the Web host.
- `POST /api/internal/disputes/{id}/resolution` accepts only a five-minute,
  HMAC-signed `FullRefund` or `FullPayout` decision with a human review
  reference. AI output is never accepted as the binding decision.
