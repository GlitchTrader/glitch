import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  verifyLicenseBinding,
} from "@/lib/entitlements-store";
import { buildPolicy, resolvePlanFromCode } from "@/lib/license-policy";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";

export const runtime = "nodejs";

const FINNHUB_BASE_URL = "https://api.finnhub.io/api/v1";
const FRED_BASE_URL = "https://api.stlouisfed.org/fred";
const REQUEST_TIMEOUT_MS = 12000;

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

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
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

    const policy = buildPolicy(resolvePlanFromCode(entitlement.planCode));
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
    const providerPayload = await fetchProviderJson(providerUrl);

    return jsonResponse({
      ok: true,
      requestId,
      provider: parsed.provider,
      operation: parsed.operation,
      data: providerPayload,
    });
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
