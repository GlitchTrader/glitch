# 07 — Deployment

## Do Not Dockerize NinjaTrader

NinjaTrader should run natively on Windows.

Docker is appropriate for Hermes, Postgres, workers, and dashboards.

## Minimal Local Deployment

```text
Windows workstation:
  NinjaTrader 8
  Glitch AddOn
  Glitch bridge bound to 127.0.0.1

Docker Desktop / WSL2:
  Hermes
  Postgres
```

Pros:

```text
simple
fast iteration
no network complexity
```

Cons:

```text
desktop instability
Windows sleep/update risk
GPU/LLM resource contention
```

## Preferred VPS Deployment

```text
Windows VPS:
  NinjaTrader 8
  data feed/broker
  Glitch AddOn
  local bridge

Linux VPS:
  Hermes
  Postgres
  dashboards
  backups

Private network:
  Tailscale or WireGuard
```

## Windows VPS Requirements

Practical minimum:

```text
Windows Server 2022
4 vCPU
16 GB RAM
100 GB SSD
stable network
no forced auto-restart during session
```

## Security

Required:

```text
bind to localhost by default
API key or HMAC
account allowlist
instrument allowlist
intent idempotency
rate limiting
kill switch
audit logs
private network only if remote
```

## Runtime Failure Rules

If Hermes disconnects:

```text
Glitch keeps managing open risk.
No new AI entries.
Existing stops remain active.
Optional flatten after stale heartbeat threshold.
```

If Glitch bridge fails:

```text
Hermes stops emitting intents.
Mark data stale.
No retry storm.
```

If account state is ambiguous:

```text
Reject new entries.
Allow EXIT / FLATTEN only.
```
