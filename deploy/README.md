# Linux single-host deployment

This deployment runs Caddy, Toklong Web, Toklong API, Toklong CRM, one
background worker, and PostgreSQL on one Linux host. It is intended as the
first staging or closed-beta topology. It is not high availability: loss of
the host stops the applications and their database.

## Host prerequisites

- A current x86_64 or arm64 Linux distribution with Docker Engine and the
  Docker Compose plugin.
- At least 4 GB RAM for Web, API, CRM, Worker, PostgreSQL, Caddy, and image
  processing.
- DNS A/AAAA records for separate Web, API, and CRM hostnames pointing to the
  host.
- Inbound firewall access only for SSH, TCP 80, TCP 443, and UDP 443. Do not
  expose PostgreSQL port 5432.
- An HTTPS OTP provider and notification provider. Production startup fails
  closed without them.
- A contracted and funded SHIPPOP account with live API key/account email and
  approved carrier service codes. Production startup fails closed without
  these managed-shipping settings.
- Apple/Android association identifiers for the current mobile-link contract.
- A dedicated single-tenant Microsoft Entra app registration for CRM with
  redirect URI `https://<crm-host>/signin-oidc`. Entra ID Free is acceptable
  while CRM financial actions remain disabled.

The Dockerfiles pin the .NET 10 SDK and runtime patch versions. Review and bump
those pins after each supported .NET security update, rebuild all images, and
rerun the smoke checks below.

Payment, payout, OTP, push, carrier, legal, and operational approval remain
separate launch prerequisites. Keep Stripe disabled until those approvals and
the live webhook configuration exist.

## First deployment

From the repository root:

```bash
cp deploy/.env.production.example .env.production
chmod 600 .env.production
mkdir -p deploy/secrets
chmod 700 deploy/secrets
openssl rand -hex 32 \
  > deploy/secrets/data-protection-password
openssl req -x509 -newkey rsa:3072 -sha256 -nodes \
  -subj "/CN=TOKLONG Data Protection" \
  -keyout deploy/secrets/data-protection.key \
  -out deploy/secrets/data-protection.crt \
  -days 730
openssl pkcs12 -export \
  -out deploy/secrets/data-protection.pfx \
  -inkey deploy/secrets/data-protection.key \
  -in deploy/secrets/data-protection.crt \
  -passout file:deploy/secrets/data-protection-password
openssl rand -base64 32 \
  > deploy/secrets/dispute-evidence-key
rm deploy/secrets/data-protection.key \
  deploy/secrets/data-protection.crt
chmod 600 deploy/secrets/*
```

Fill every blank required value. Use a unique database password and a random
reconciliation signing secret and a separate SHIPPOP quote-signing secret, each
at least 32 characters. Do not commit the resulting file or
`deploy/secrets`. Back up the PFX, its password, and the dispute-evidence key
separately in encrypted storage. Losing the evidence key makes retained
evidence unreadable; changing it without re-encrypting existing files breaks
evidence access.

Validate and start:

```bash
docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  config --quiet

docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  up -d --build
```

The startup order is PostgreSQL health check, one-shot core migration,
one-shot CRM-schema migration, Web/API/Worker/CRM, then Caddy. Long-running
services never run migrations during normal startup.

Check the deployment:

```bash
docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  ps

curl --fail "https://YOUR_WEB_DOMAIN/health/ready"
curl --fail "https://YOUR_API_DOMAIN/health/ready"
curl --fail "https://YOUR_CRM_DOMAIN/health/ready"
```

Replace the example hostnames in the checks. Caddy provisions and renews TLS
certificates automatically after DNS and ports 80/443 are correct.

## Bootstrap CRM access

The first command works only while `crm.users` is empty. Use the tenant ID
configured for the CRM Entra app and the first workforce user's object ID:

```bash
docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  run --rm \
  -e CrmBootstrap__EntraTenantId=YOUR_TENANT_ID \
  -e CrmBootstrap__EntraObjectId=FIRST_USER_OBJECT_ID \
  -e CrmBootstrap__Email=FIRST_USER_EMAIL \
  -e CrmBootstrap__DisplayName=FIRST_USER_NAME \
  crm --bootstrap-super-admin
```

Sign in, create the second user as Admin, then run the one-time second
SuperAdmin ceremony:

```bash
docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  run --rm \
  -e CrmBootstrap__EntraTenantId=YOUR_TENANT_ID \
  -e CrmBootstrap__EntraObjectId=SECOND_USER_OBJECT_ID \
  -e CrmBootstrap__Email=SECOND_USER_EMAIL \
  -e CrmBootstrap__DisplayName=SECOND_USER_NAME \
  crm --bootstrap-second-super-admin
```

Keep at least two active SuperAdmins. All later elevation uses the normal
two-person approval workflow. Do not store bootstrap values in source control.

## Persistent data

The Compose project creates these named volumes:

- `postgres-data`: PostgreSQL data.
- `app-data`: Web/API/CRM Data Protection keys, managed product images, and
  AES-GCM encrypted dispute evidence.
- `caddy-data` and `caddy-config`: TLS certificates and Caddy state.

The application containers run as the non-root .NET image user, use read-only
root filesystems, and share only `app-data`. Data Protection keys are encrypted
with the certificate mounted through Docker secrets. PostgreSQL is reachable
only on the internal Docker network.

Managed product images and encrypted dispute evidence remain local to this
host. Back up `app-data` and the evidence key under separate access controls;
move private assets to managed object storage before horizontal scaling.

## Backup

Create a database dump without publishing the database port:

```bash
mkdir -p backups
docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  exec -T postgres \
  pg_dump -U toklong -d toklong -Fc \
  > "backups/toklong-$(date -u +%Y%m%dT%H%M%SZ).dump"
```

Copy database dumps and the `app-data` volume backup to encrypted off-host
storage. A backup is not accepted until a restore drill succeeds on another
host. Restrict backup files because they contain personal and transaction
data.

## Deploy an update

Build first. Before deploying parcel-protection booking-fingerprint or
rebooking changes, put buyer checkout and the shipping Worker into maintenance
mode, then drain the existing shipping-operation queue. Reconcile every
`OutcomeUnknown` or `NeedsReview` operation against the provider, and do not
continue while any legacy-fingerprint operation remains `Pending`,
`Processing`, or `RetryScheduled`. Record the reconciliation result, keep the
API/Web checkout path quiesced, and take a database backup plus `app-data`
backup. Prove that backup with a restore drill before continuing.

The `AllowSupersededOutboundBookingIntent` and
`ParcelProtectionRebooking` migrations are intentionally irreversible because
rolling their uniqueness changes back could discard immutable booking history.
Do not use EF `Down` as rollback. The rollback boundary is a tested pre-migration
database and `app-data` restore; after new transactions are accepted, prefer a
reviewed forward fix unless restoring the whole deployment is explicitly
authorized.

Run the migration as a separate failure boundary, replace the long-running
services, and only then resume checkout and the Worker:

```bash
docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  build --pull

docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  run --rm migrate

docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  run --rm crm-migrate

docker compose \
  --env-file .env.production \
  -f compose.linux.yml \
  up -d --no-deps api web worker crm caddy
```

Review `docker compose ... logs --since=15m` and all readiness endpoints after
the update. Confirm that newly queued booking fingerprints match the deployed
code and that no stale legacy operation reappears before ending maintenance
mode. Database migrations need an explicit restore/forward-fix plan before each
production deployment.

## Current operational boundary

- Run exactly one `worker` replica. The Web and API processes do not run
  deadline, SHIPPOP confirmation/tracking/cancellation,
  financial-reconciliation, retention, or notification-outbox loops.
- The Worker runs due retention at startup and every 24 hours. Before deploying
  the migration, run the signed retention preview and verify legal holds for
  every active case.
- The included Caddy configuration does not enable access logging. Do not
  enable upstream CDN/load-balancer IP or device logging without a separately
  approved purpose and retention schedule.
- Caddy passes trusted forwarding headers; Web/API/CRM accept them only because
  their ports are private to the Compose networks.
- Web and CRM use interactive Blazor Server. This topology has one replica of
  each and therefore does not yet need load-balancer affinity.
- The single PostgreSQL instance and local product images are single points of
  failure. Do not describe this topology as high availability.
- Before accepting real money, add monitored encrypted off-host backups,
  centralized logs and alerts, restore/runbook drills, provider reconciliation,
  and the required legal/provider approvals.
