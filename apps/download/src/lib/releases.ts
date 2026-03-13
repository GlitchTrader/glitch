import { list, type ListBlobResultBlob } from "@vercel/blob";
import { cache } from "react";

const defaultWebsiteUrl = "https://glitchtrader.com";
const defaultDocsUrl = "https://docs.glitchtrader.com";
const defaultDownloadsUrl = "https://downloads.glitchtrader.com";
const defaultReleasePrefix = "glitch/nt8/releases/";

export type ReleaseRecord = {
  version: string;
  slug: string;
  fileName: string;
  pathname: string;
  size: number;
  uploadedAt: Date;
  downloadUrl: string;
};

export type ReleaseCatalog = {
  configured: boolean;
  error: string | null;
  prefix: string;
  releases: ReleaseRecord[];
};

function normalizePrefix(value: string): string {
  const trimmed = value.trim().replace(/^\/+|\/+$/g, "");
  return trimmed ? `${trimmed}/` : "";
}

function getReleasePrefix(): string {
  return normalizePrefix(process.env.DOWNLOADS_BLOB_PREFIX ?? defaultReleasePrefix);
}

function getFileName(pathname: string): string {
  const parts = pathname.split("/");
  return parts[parts.length - 1] ?? pathname;
}

function deriveVersion(fileName: string): string {
  const match = fileName.match(/(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)/);
  return match?.[1] ?? fileName.replace(/\.zip$/i, "");
}

function toSlug(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");
}

function toReleaseRecord(blob: ListBlobResultBlob): ReleaseRecord {
  const fileName = getFileName(blob.pathname);
  const version = deriveVersion(fileName);

  return {
    version,
    slug: toSlug(version),
    fileName,
    pathname: blob.pathname,
    size: blob.size,
    uploadedAt: new Date(blob.uploadedAt),
    downloadUrl: blob.downloadUrl,
  };
}

export const getReleaseCatalog = cache(async (): Promise<ReleaseCatalog> => {
  const prefix = getReleasePrefix();
  const configured = Boolean(process.env.BLOB_READ_WRITE_TOKEN);

  if (!configured) {
    return {
      configured: false,
      error: null,
      prefix,
      releases: [],
    };
  }

  try {
    const result = await list({ prefix, limit: 200 });
    const releases = result.blobs
      .filter((blob) => blob.pathname.toLowerCase().endsWith(".zip"))
      .map(toReleaseRecord)
      .sort((a, b) => b.uploadedAt.getTime() - a.uploadedAt.getTime());

    return {
      configured: true,
      error: null,
      prefix,
      releases,
    };
  } catch {
    return {
      configured: true,
      error: "Release catalog could not be loaded from Blob.",
      prefix,
      releases: [],
    };
  }
});

export const getLatestRelease = cache(async (): Promise<ReleaseRecord | null> => {
  const catalog = await getReleaseCatalog();
  return catalog.releases[0] ?? null;
});

export const getReleaseBySlug = cache(async (slug: string): Promise<ReleaseRecord | null> => {
  const catalog = await getReleaseCatalog();
  return catalog.releases.find((release) => release.slug === slug) ?? null;
});

export function getWebsiteUrl(): string {
  return process.env.NEXT_PUBLIC_WEBSITE_URL?.trim() || defaultWebsiteUrl;
}

export function getDocsUrl(): string {
  return process.env.NEXT_PUBLIC_DOCS_URL?.trim() || defaultDocsUrl;
}

export function getDownloadsUrl(): string {
  return process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() || defaultDownloadsUrl;
}

export function formatReleaseSize(size: number): string {
  if (!Number.isFinite(size) || size <= 0) {
    return "Unknown size";
  }

  const units = ["B", "KB", "MB", "GB"];
  let value = size;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value >= 10 || unitIndex === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unitIndex]}`;
}

export function formatReleaseDate(date: Date): string {
  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(date);
}
