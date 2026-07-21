import { readOptionalEnv } from "@/lib/env";

const DEFAULT_LATEST_RELEASE_METADATA_URL = "https://download.glitchtrader.com/api/releases/latest";
const DEFAULT_ADDON_DOWNLOAD_URL = "https://download.glitchtrader.com/latest";
const DEFAULT_AI_ADDON_DOWNLOAD_URL = "https://download.glitchtrader.com/latest/ai";
const VERSION_TOKEN_PATTERN = /\d+/g;
const LATEST_RELEASE_FETCH_TIMEOUT_MS = 2_000;
const LATEST_RELEASE_SUCCESS_CACHE_TTL_MS = 120_000;
const LATEST_RELEASE_FAILURE_CACHE_TTL_MS = 30_000;

export interface AddonUpdateInfo {
  checked: boolean;
  latestVersion: string | null;
  downloadUrl: string;
  isOutdated: boolean;
}

interface LatestReleaseMetadata {
  version: string | null;
  downloadUrl: string;
}

type AddonEdition = "standard" | "ai";

const latestReleaseMetadataCache = new Map<
  AddonEdition,
  { value: LatestReleaseMetadata | null; expiresAtMs: number }
>();
const latestReleaseMetadataInFlight = new Map<AddonEdition, Promise<LatestReleaseMetadata | null>>();

function resolveAddonEdition(clientVersion: string | null | undefined): AddonEdition {
  return clientVersion?.trim().toLowerCase().startsWith("addon-ai-") ? "ai" : "standard";
}

function parseVersionTokens(value: string | null | undefined): number[] | null {
  if (!value) {
    return null;
  }

  const tokens = value.match(VERSION_TOKEN_PATTERN);
  if (!tokens || tokens.length === 0) {
    return null;
  }

  const parsed = tokens
    .map((token) => Number.parseInt(token, 10))
    .filter((token) => Number.isFinite(token) && token >= 0);
  if (parsed.length === 0) {
    return null;
  }

  return parsed.map((token) => Math.min(token, 1_000_000));
}

function compareVersionTokens(a: number[], b: number[]): number {
  const length = Math.max(a.length, b.length);
  for (let index = 0; index < length; index += 1) {
    const left = a[index] ?? 0;
    const right = b[index] ?? 0;
    if (left < right) {
      return -1;
    }
    if (left > right) {
      return 1;
    }
  }

  return 0;
}

function normalizeLatestVersion(rawValue: string | null): string | null {
  if (!rawValue) {
    return null;
  }

  const trimmed = rawValue.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function normalizeDownloadUrl(rawValue: string | null, defaultUrl: string): string {
  const candidate = rawValue?.trim();
  if (!candidate) {
    return defaultUrl;
  }

  try {
    const parsed = new URL(candidate);
    if (parsed.protocol === "https:" || parsed.protocol === "http:") {
      return parsed.toString();
    }
  } catch {
    // Fall through to default.
  }

  return defaultUrl;
}

function normalizeMetadataUrl(rawValue: string | null): string {
  const candidate = rawValue?.trim();
  if (!candidate) {
    return DEFAULT_LATEST_RELEASE_METADATA_URL;
  }

  try {
    const parsed = new URL(candidate);
    if (parsed.protocol === "https:" || parsed.protocol === "http:") {
      return parsed.toString();
    }
  } catch {
    // Fall through to default.
  }

  return DEFAULT_LATEST_RELEASE_METADATA_URL;
}

async function fetchLatestReleaseMetadataFromDownloadApi(
  metadataUrl: string,
  fallbackDownloadUrl: string,
): Promise<LatestReleaseMetadata | null> {
  const controller = new AbortController();
  const timeoutHandle = setTimeout(() => controller.abort(), LATEST_RELEASE_FETCH_TIMEOUT_MS);

  try {
    const response = await fetch(metadataUrl, {
      method: "GET",
      cache: "no-store",
      redirect: "follow",
      signal: controller.signal,
      headers: {
        accept: "application/json",
      },
    });

    if (!response.ok) {
      return null;
    }

    const payload = (await response.json()) as Record<string, unknown> | null;
    const latest = payload && typeof payload.latest === "object"
      ? (payload.latest as Record<string, unknown>)
      : payload;

    const rawVersion = typeof latest?.version === "string" ? latest.version : null;
    const latestVersion = normalizeLatestVersion(rawVersion);
    if (!latestVersion) {
      return null;
    }

    const rawDownloadUrl = typeof latest?.downloadUrl === "string"
      ? latest.downloadUrl
      : typeof latest?.downloadPath === "string"
        ? latest.downloadPath
        : null;
    let downloadUrl = fallbackDownloadUrl;
    if (rawDownloadUrl) {
      try {
        downloadUrl = normalizeDownloadUrl(new URL(rawDownloadUrl, metadataUrl).toString(), fallbackDownloadUrl);
      } catch {
        downloadUrl = normalizeDownloadUrl(rawDownloadUrl, fallbackDownloadUrl);
      }
    }

    return {
      version: latestVersion,
      downloadUrl,
    };
  } catch {
    return null;
  } finally {
    clearTimeout(timeoutHandle);
  }
}

async function getLatestReleaseMetadata(
  edition: AddonEdition,
  fallbackDownloadUrl: string,
): Promise<LatestReleaseMetadata | null> {
  const now = Date.now();
  const cached = latestReleaseMetadataCache.get(edition);
  if (cached && cached.expiresAtMs > now) {
    return cached.value;
  }

  const inFlight = latestReleaseMetadataInFlight.get(edition);
  if (inFlight) {
    return inFlight;
  }

  const baseMetadataUrl = normalizeMetadataUrl(readOptionalEnv("ADDON_RELEASES_LATEST_URL"));
  const parsedMetadataUrl = new URL(baseMetadataUrl);
  if (edition === "ai") {
    parsedMetadataUrl.searchParams.set("edition", "ai");
  }
  const metadataUrl = parsedMetadataUrl.toString();
  const request = fetchLatestReleaseMetadataFromDownloadApi(metadataUrl, fallbackDownloadUrl)
    .then((metadata) => {
      latestReleaseMetadataCache.set(edition, {
        value: metadata,
        expiresAtMs: Date.now() + (metadata ? LATEST_RELEASE_SUCCESS_CACHE_TTL_MS : LATEST_RELEASE_FAILURE_CACHE_TTL_MS),
      });
      return metadata;
    })
    .finally(() => {
      latestReleaseMetadataInFlight.delete(edition);
    });
  latestReleaseMetadataInFlight.set(edition, request);

  return request;
}

function buildUpdateInfo(
  latestVersion: string | null,
  downloadUrl: string,
  clientVersion: string | null | undefined,
): AddonUpdateInfo {
  if (!latestVersion) {
    return {
      checked: false,
      latestVersion: null,
      downloadUrl,
      isOutdated: false,
    };
  }

  const latestTokens = parseVersionTokens(latestVersion);
  if (!latestTokens) {
    return {
      checked: false,
      latestVersion,
      downloadUrl,
      isOutdated: false,
    };
  }

  const clientTokens = parseVersionTokens(clientVersion);
  const isOutdated = clientTokens ? compareVersionTokens(clientTokens, latestTokens) < 0 : false;

  return {
    checked: true,
    latestVersion,
    downloadUrl,
    isOutdated,
  };
}

export async function resolveAddonUpdateInfo(clientVersion: string | null | undefined): Promise<AddonUpdateInfo> {
  const edition = resolveAddonEdition(clientVersion);
  const defaultDownloadUrl = edition === "ai" ? DEFAULT_AI_ADDON_DOWNLOAD_URL : DEFAULT_ADDON_DOWNLOAD_URL;
  const downloadUrlEnvName = edition === "ai" ? "ADDON_AI_LATEST_DOWNLOAD_URL" : "ADDON_LATEST_DOWNLOAD_URL";
  const versionEnvName = edition === "ai" ? "ADDON_AI_LATEST_VERSION" : "ADDON_LATEST_VERSION";
  const fallbackDownloadUrl = normalizeDownloadUrl(readOptionalEnv(downloadUrlEnvName), defaultDownloadUrl);

  // Emergency override path for operational incidents.
  const manualLatestVersion = normalizeLatestVersion(readOptionalEnv(versionEnvName));
  if (manualLatestVersion) {
    return buildUpdateInfo(manualLatestVersion, fallbackDownloadUrl, clientVersion);
  }

  const latestMetadata = await getLatestReleaseMetadata(edition, fallbackDownloadUrl);
  if (!latestMetadata) {
    return {
      checked: false,
      latestVersion: null,
      downloadUrl: fallbackDownloadUrl,
      isOutdated: false,
    };
  }

  return buildUpdateInfo(latestMetadata.version, latestMetadata.downloadUrl, clientVersion);
}
