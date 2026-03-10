import { NextRequest } from "next/server";
import { createHash } from "crypto";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  verifyLicenseBinding,
} from "@/lib/entitlements-store";
import { buildPolicy, resolvePlanFromCode } from "@/lib/license-policy";
import type { LicensePlan } from "@/lib/license-policy";
import { readOptionalEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";

export const runtime = "nodejs";

const FINNHUB_BASE_URL = "https://api.finnhub.io/api/v1";
const FRED_BASE_URL = "https://api.stlouisfed.org/fred";
const REQUEST_TIMEOUT_MS = 12000;
const PROVIDER_CACHE_STALE_FALLBACK_SECONDS = 900;
const PROVIDER_CACHE_RETENTION_SECONDS_DEFAULT = 86400;
const PROVIDER_CACHE_CLEANUP_INTERVAL_MS = 10 * 60 * 1000;
const PROVIDER_ACCESS_CACHE_SECONDS_DEFAULT = 30;
const globalAccessCacheKey = "__glitchProviderProxyAccessCacheV1";
const globalPoolKey = "__glitchProviderProxyCachePoolV1";
const globalSchemaReadyKey = "__glitchProviderProxyCacheSchemaReadyV1";
const globalCleanupTsKey = "__glitchProviderProxyCacheLastCleanupAtV1";

type ProviderName = "finnhub" | "fred";

interface ProviderProxyRequestPayload {
  provider: ProviderName;
  operation: string;
  params: Record<string, string>;
  licenseKey: string;
  installationId: string;
  deviceFingerprintHash: string;
  clientVersion?: string;
}

interface ProviderCacheRow {
  cache_key: string;
  payload: string;
  updated_at: string | Date;
  expires_at: string | Date;
}

interface ProviderCacheDbPool {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rows: T[]; rowCount: number | null }>;
}

interface ProviderAccessCacheEntry {
  expiresAtMs: number;
  plan: LicensePlan;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function readDatabaseUrl(): string | null {
  return readOptionalEnv("DATABASE_URL");
}

function toMs(value: string | Date | null | undefined): number {
  if (!value) {
    return Number.NaN;
  }

  if (value instanceof Date) {
    return value.valueOf();
  }

  const parsed = new Date(value);
  return parsed.valueOf();
}

function readProviderProxyCacheTtlSeconds(provider: ProviderName, operation: string): number {
  const envValue = readOptionalEnv("PROVIDER_PROXY_CACHE_TTL_SECONDS");
  const parsedEnv = envValue ? Number.parseInt(envValue, 10) : Number.NaN;
  if (Number.isFinite(parsedEnv) && parsedEnv > 0) {
    return Math.max(15, Math.min(parsedEnv, 3600));
  }

  if (provider === "finnhub") {
    switch (operation) {
      case "quote":
        return 20;
      case "general_news":
        return 90;
      case "company_news":
        return 120;
      case "stock_metric":
        return 3600;
      case "calendar_earnings":
        return 1800;
      default:
        return 120;
    }
  }

  if (provider === "fred" && operation === "releases_dates") {
    return 1800;
  }

  return 120;
}

function readProviderProxyCacheRetentionSeconds(): number {
  const envValue = readOptionalEnv("PROVIDER_PROXY_CACHE_RETENTION_SECONDS");
  const parsedEnv = envValue ? Number.parseInt(envValue, 10) : Number.NaN;
  if (Number.isFinite(parsedEnv) && parsedEnv > 0) {
    return Math.max(900, Math.min(parsedEnv, 604800));
  }

  return PROVIDER_CACHE_RETENTION_SECONDS_DEFAULT;
}

function readProviderProxyAccessCacheSeconds(): number {
  const envValue = readOptionalEnv("PROVIDER_PROXY_ACCESS_CACHE_SECONDS");
  const parsedEnv = envValue ? Number.parseInt(envValue, 10) : Number.NaN;
  if (Number.isFinite(parsedEnv) && parsedEnv > 0) {
    return Math.max(5, Math.min(parsedEnv, 120));
  }

  return PROVIDER_ACCESS_CACHE_SECONDS_DEFAULT;
}

function buildParamsHash(params: Record<string, string>): string {
  const canonical = Object.entries(params)
    .filter(([key, value]) => isNonEmpty(key) && isNonEmpty(value))
    .map(([key, value]) => [key.trim(), value.trim()] as const)
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([key, value]) => `${key}=${value}`)
    .join("&");

  return createHash("sha256").update(canonical, "utf8").digest("hex");
}

function buildCacheKey(provider: ProviderName, operation: string, params: Record<string, string>): string {
  return `provider:${provider}:${operation}:${buildParamsHash(params)}`;
}

function buildProviderAccessCacheKey(
  licenseKey: string,
  installationId: string,
  deviceFingerprintHash: string,
): string {
  return createHash("sha256")
    .update(`${licenseKey}|${installationId}|${deviceFingerprintHash}`, "utf8")
    .digest("hex");
}

function getProviderAccessCacheStore(): Map<string, ProviderAccessCacheEntry> {
  const globalScope = globalThis as typeof globalThis & {
    [globalAccessCacheKey]?: Map<string, ProviderAccessCacheEntry>;
  };

  if (!globalScope[globalAccessCacheKey]) {
    globalScope[globalAccessCacheKey] = new Map<string, ProviderAccessCacheEntry>();
  }

  return globalScope[globalAccessCacheKey]!;
}

function readCachedProviderAccess(
  cacheKey: string,
): ProviderAccessCacheEntry | null {
  const store = getProviderAccessCacheStore();
  const entry = store.get(cacheKey);
  if (!entry) {
    return null;
  }

  if (entry.expiresAtMs <= Date.now()) {
    store.delete(cacheKey);
    return null;
  }

  return entry;
}

function writeCachedProviderAccess(cacheKey: string, plan: LicensePlan): void {
  const ttlSeconds = readProviderProxyAccessCacheSeconds();
  const expiresAtMs = Date.now() + (ttlSeconds * 1000);
  getProviderAccessCacheStore().set(cacheKey, {
    expiresAtMs,
    plan,
  });
}

async function getProviderCachePool(): Promise<ProviderCacheDbPool | null> {
  const globalScope = globalThis as typeof globalThis & {
    [globalPoolKey]?: ProviderCacheDbPool;
  };

  if (globalScope[globalPoolKey]) {
    return globalScope[globalPoolKey];
  }

  const connectionString = readDatabaseUrl();
  if (!connectionString) {
    return null;
  }

  const pgModule = (await import("pg")) as typeof import("pg");
  const pool = new pgModule.Pool({
    connectionString,
    max: 4,
    idleTimeoutMillis: 30_000,
  });

  globalScope[globalPoolKey] = pool;
  return pool;
}

async function ensureProviderCacheSchema(pool: ProviderCacheDbPool): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalSchemaReadyKey]?: boolean;
  };

  if (globalScope[globalSchemaReadyKey]) {
    return;
  }

  await pool.query(`
    CREATE TABLE IF NOT EXISTS provider_proxy_cache (
      cache_key TEXT PRIMARY KEY,
      payload TEXT NOT NULL,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      expires_at TIMESTAMPTZ NOT NULL
    );
  `);

  await pool.query(`
    CREATE INDEX IF NOT EXISTS provider_proxy_cache_expires_at_idx
      ON provider_proxy_cache (expires_at);
  `);

  globalScope[globalSchemaReadyKey] = true;
}

async function readProviderCache(
  pool: ProviderCacheDbPool,
  cacheKey: string,
): Promise<{
  payload: string;
  updatedAtMs: number;
  expiresAtMs: number;
} | null> {
  const result = await pool.query<ProviderCacheRow>(
    `
      SELECT cache_key, payload, updated_at, expires_at
      FROM provider_proxy_cache
      WHERE cache_key = $1
      LIMIT 1;
    `,
    [cacheKey],
  );

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  return {
    payload: row.payload,
    updatedAtMs: toMs(row.updated_at),
    expiresAtMs: toMs(row.expires_at),
  };
}

async function writeProviderCache(
  pool: ProviderCacheDbPool,
  cacheKey: string,
  payload: string,
  ttlSeconds: number,
): Promise<void> {
  await pool.query(
    `
      INSERT INTO provider_proxy_cache (cache_key, payload, updated_at, expires_at)
      VALUES ($1, $2, NOW(), NOW() + ($3::int * INTERVAL '1 second'))
      ON CONFLICT (cache_key)
      DO UPDATE SET
        payload = EXCLUDED.payload,
        updated_at = NOW(),
        expires_at = EXCLUDED.expires_at;
    `,
    [cacheKey, payload, ttlSeconds],
  );
}

async function maybeCleanupProviderCache(pool: ProviderCacheDbPool): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalCleanupTsKey]?: number;
  };

  const now = Date.now();
  const lastCleanupTs = globalScope[globalCleanupTsKey] ?? 0;
  if ((now - lastCleanupTs) < PROVIDER_CACHE_CLEANUP_INTERVAL_MS) {
    return;
  }

  globalScope[globalCleanupTsKey] = now;
  const retentionSeconds = readProviderProxyCacheRetentionSeconds();

  try {
    await pool.query(
      `
        DELETE FROM provider_proxy_cache
        WHERE expires_at < NOW() - ($1::int * INTERVAL '1 second');
      `,
      [retentionSeconds],
    );
  } catch {
    // Cleanup is best-effort only; main request path must remain available.
  }
}

function parseParams(value: unknown): Record<string, string> {
  if (!value || typeof value !== "object") {
    return {};
  }

  const record = value as Record<string, unknown>;
  const parsed: Record<string, string> = {};
  for (const [key, raw] of Object.entries(record)) {
    if (!isNonEmpty(key) || !isNonEmpty(raw)) {
      continue;
    }

    parsed[key.trim()] = raw.trim();
  }

  return parsed;
}

function parsePayload(payload: unknown): ProviderProxyRequestPayload | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (
    (record.provider !== "finnhub" && record.provider !== "fred") ||
    !isNonEmpty(record.operation) ||
    !isNonEmpty(record.licenseKey) ||
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.deviceFingerprintHash)
  ) {
    return null;
  }

  return {
    provider: record.provider,
    operation: record.operation.trim(),
    params: parseParams(record.params),
    licenseKey: record.licenseKey.trim(),
    installationId: record.installationId.trim(),
    deviceFingerprintHash: record.deviceFingerprintHash.trim(),
    clientVersion: isNonEmpty(record.clientVersion) ? record.clientVersion.trim() : undefined,
  };
}

function requireParam(params: Record<string, string>, key: string): string {
  const value = params[key];
  if (!isNonEmpty(value)) {
    throw new Error(`missing_param:${key}`);
  }

  return value.trim();
}

function buildUrl(baseUrl: string, query: Record<string, string>): string {
  const url = new URL(baseUrl);
  for (const [key, value] of Object.entries(query)) {
    if (!isNonEmpty(key) || !isNonEmpty(value)) {
      continue;
    }

    url.searchParams.set(key, value);
  }

  return url.toString();
}

function resolveFinnhubUrl(
  operation: string,
  params: Record<string, string>,
  apiKey: string,
): string {
  switch (operation) {
    case "company_news":
      return buildUrl(`${FINNHUB_BASE_URL}/company-news`, {
        symbol: requireParam(params, "symbol"),
        from: requireParam(params, "from"),
        to: requireParam(params, "to"),
        token: apiKey,
      });
    case "general_news":
      return buildUrl(`${FINNHUB_BASE_URL}/news`, {
        category: isNonEmpty(params.category) ? params.category : "general",
        token: apiKey,
      });
    case "quote":
      return buildUrl(`${FINNHUB_BASE_URL}/quote`, {
        symbol: requireParam(params, "symbol"),
        token: apiKey,
      });
    case "stock_metric":
      return buildUrl(`${FINNHUB_BASE_URL}/stock/metric`, {
        symbol: requireParam(params, "symbol"),
        metric: isNonEmpty(params.metric) ? params.metric : "all",
        token: apiKey,
      });
    case "calendar_earnings":
      return buildUrl(`${FINNHUB_BASE_URL}/calendar/earnings`, {
        symbol: requireParam(params, "symbol"),
        from: requireParam(params, "from"),
        to: requireParam(params, "to"),
        token: apiKey,
      });
    default:
      throw new Error("unsupported_operation");
  }
}

function resolveFredUrl(operation: string, params: Record<string, string>, apiKey: string): string {
  switch (operation) {
    case "releases_dates":
      return buildUrl(`${FRED_BASE_URL}/releases/dates`, {
        realtime_start: requireParam(params, "realtime_start"),
        realtime_end: requireParam(params, "realtime_end"),
        include_release_dates_with_no_data: isNonEmpty(params.include_release_dates_with_no_data)
          ? params.include_release_dates_with_no_data
          : "true",
        sort_order: isNonEmpty(params.sort_order) ? params.sort_order : "asc",
        limit: isNonEmpty(params.limit) ? params.limit : "1000",
        file_type: isNonEmpty(params.file_type) ? params.file_type : "json",
        api_key: apiKey,
      });
    default:
      throw new Error("unsupported_operation");
  }
}

function resolveProviderUrl(payload: ProviderProxyRequestPayload): string {
  if (payload.provider === "finnhub") {
    const apiKey = process.env.FINNHUB_API_KEY?.trim();
    if (!apiKey) {
      throw new Error("missing_finnhub_api_key");
    }

    return resolveFinnhubUrl(payload.operation, payload.params, apiKey);
  }

  const apiKey = process.env.FRED_API_KEY?.trim();
  if (!apiKey) {
    throw new Error("missing_fred_api_key");
  }

  return resolveFredUrl(payload.operation, payload.params, apiKey);
}

async function fetchProviderJson(url: string): Promise<string> {
  const response = await fetch(url, {
    method: "GET",
    headers: {
      Accept: "application/json",
      "User-Agent": "glitch-api/provider-proxy",
    },
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    cache: "no-store",
  });

  const payload = await response.text();
  if (!response.ok) {
    throw new Error(`provider_http_${response.status}`);
  }

  return payload;
}

export async function POST(request: NextRequest) {
  const requestId = getRequestId(request);
  let payload: unknown;

  try {
    payload = await request.json();
  } catch (error) {
    return errorResponse(
      requestId,
      400,
      "invalid_json",
      "Request body must be valid JSON.",
      error instanceof Error ? error.message : String(error),
    );
  }

  const parsed = parsePayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields.",
    );
  }

  try {
    const providerAccessCacheKey = buildProviderAccessCacheKey(
      parsed.licenseKey,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    const cachedAccess = readCachedProviderAccess(providerAccessCacheKey);
    let plan: LicensePlan | null = cachedAccess?.plan ?? null;
    if (!plan) {
      const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
      if (!entitlement) {
        return errorResponse(requestId, 401, "license_not_found", "License key was not found.");
      }

      if (!isWhopEntitlementStatusActive(entitlement.status)) {
        return errorResponse(
          requestId,
          401,
          "license_inactive",
          `Membership is not active (${entitlement.status}).`,
        );
      }

      const bindingResult = await verifyLicenseBinding(
        entitlement.id,
        parsed.installationId,
        parsed.deviceFingerprintHash,
      );
      if (!bindingResult.ok) {
        return errorResponse(
          requestId,
          401,
          "license_binding_invalid",
          "License binding validation failed.",
          bindingResult.reason,
        );
      }

      plan = resolvePlanFromCode(entitlement.planCode);
      writeCachedProviderAccess(providerAccessCacheKey, plan);
    }

    const policy = buildPolicy(plan);
    if (!policy.features.fundamental) {
      return errorResponse(
        requestId,
        403,
        "feature_locked",
        "Fundamental analytics are not enabled for this plan.",
        {
          plan: policy.plan,
        },
      );
    }

    const providerUrl = resolveProviderUrl(parsed);
    const cacheKey = buildCacheKey(parsed.provider, parsed.operation, parsed.params);
    const ttlSeconds = readProviderProxyCacheTtlSeconds(parsed.provider, parsed.operation);

    const cachePool = await getProviderCachePool();
    let cached: { payload: string; updatedAtMs: number; expiresAtMs: number } | null = null;
    if (cachePool) {
      await ensureProviderCacheSchema(cachePool);
      await maybeCleanupProviderCache(cachePool);
      cached = await readProviderCache(cachePool, cacheKey);
      if (cached && Number.isFinite(cached.expiresAtMs) && cached.expiresAtMs > Date.now()) {
        return jsonResponse({
          ok: true,
          requestId,
          provider: parsed.provider,
          operation: parsed.operation,
          data: cached.payload,
        });
      }
    }

    try {
      const providerPayload = await fetchProviderJson(providerUrl);
      if (cachePool) {
        await writeProviderCache(cachePool, cacheKey, providerPayload, ttlSeconds);
      }

      return jsonResponse({
        ok: true,
        requestId,
        provider: parsed.provider,
        operation: parsed.operation,
        data: providerPayload,
      });
    } catch (providerError) {
      if (
        cached &&
        Number.isFinite(cached.updatedAtMs) &&
        (Date.now() - cached.updatedAtMs) <= (PROVIDER_CACHE_STALE_FALLBACK_SECONDS * 1000)
      ) {
        return jsonResponse({
          ok: true,
          requestId,
          provider: parsed.provider,
          operation: parsed.operation,
          data: cached.payload,
        });
      }

      throw providerError;
    }
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return errorResponse(
        requestId,
        500,
        "entitlement_store_misconfigured",
        "Entitlement storage is not configured correctly.",
        error.code,
      );
    }

    const message = error instanceof Error ? error.message : String(error);
    if (message.startsWith("missing_param:")) {
      return errorResponse(
        requestId,
        400,
        "invalid_payload",
        `Missing required param: ${message.replace("missing_param:", "")}.`,
      );
    }

    if (message === "unsupported_operation") {
      return errorResponse(
        requestId,
        400,
        "unsupported_operation",
        "The requested provider operation is not supported.",
      );
    }

    if (message === "missing_finnhub_api_key" || message === "missing_fred_api_key") {
      return errorResponse(
        requestId,
        500,
        "provider_key_missing",
        "Required provider API key is not configured.",
        message,
      );
    }

    return errorResponse(
      requestId,
      502,
      "provider_proxy_failed",
      "Provider request failed.",
      message,
    );
  }
}
