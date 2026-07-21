import { existsSync } from "node:fs";
import { promises as fs } from "node:fs";
import path from "node:path";
import { cache } from "react";
import checksumManifest from "../../public/files/checksums.json";
import releaseCatalog from "./release-catalog.json";

const defaultWebsiteUrl = "https://glitchtrader.com";
const defaultDocsUrl = "https://docs.glitchtrader.com";
const defaultDownloadsUrl = "https://download.glitchtrader.com";

export type ReleaseEdition = "standard" | "ai";
export type ReleaseStatus = "stable" | "experimental";

export type ReleaseRecord = {
  version: string;
  edition: ReleaseEdition;
  status: ReleaseStatus;
  sourceCommit: string;
  hermesProfileVersion: string | null;
  slug: string;
  fileName: string;
  pathname: string;
  size: number;
  uploadedAt: Date;
  downloadPath: string;
  sha256: string;
};

export type ReleaseCatalog = {
  error: string | null;
  releases: ReleaseRecord[];
};

type CatalogEntry = {
  fileName: string;
  edition: ReleaseEdition;
  version: string;
  releaseDate: string;
  status: ReleaseStatus;
  sourceCommit: string;
  hermesProfileVersion?: string;
};

type ParsedVersion = {
  numericParts: number[];
  prerelease: string | null;
  valid: boolean;
};

type ChecksumMap = Record<string, string>;

function getReleaseDirectoryPath(): string {
  const candidates = [
    path.join(process.cwd(), "public", "files"),
    path.join(process.cwd(), "apps", "download", "public", "files"),
  ];

  for (const candidate of candidates) {
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return candidates[0];
}

function getBaseName(fileName: string): string {
  return fileName.replace(/\.zip$/i, "");
}

function toSlugValue(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");
}

function toSlug(fileName: string): string {
  return toSlugValue(getBaseName(fileName));
}

function parseVersion(version: string): ParsedVersion {
  const match = version.match(/^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]+))?$/);
  if (!match) {
    return {
      numericParts: [],
      prerelease: null,
      valid: false,
    };
  }

  const numericParts = match[1]
    .split(".")
    .map((part) => Number(part))
    .filter((part) => Number.isFinite(part));

  return {
    numericParts,
    prerelease: match[2] ?? null,
    valid: numericParts.length > 0,
  };
}

function compareVersionsDescending(a: string, b: string): number {
  const parsedA = parseVersion(a);
  const parsedB = parseVersion(b);

  if (parsedA.valid && parsedB.valid) {
    const maxLength = Math.max(parsedA.numericParts.length, parsedB.numericParts.length);
    for (let index = 0; index < maxLength; index += 1) {
      const aPart = parsedA.numericParts[index] ?? 0;
      const bPart = parsedB.numericParts[index] ?? 0;
      if (aPart !== bPart) {
        return aPart > bPart ? -1 : 1;
      }
    }

    if (parsedA.prerelease && !parsedB.prerelease) {
      return 1;
    }
    if (!parsedA.prerelease && parsedB.prerelease) {
      return -1;
    }
    if (parsedA.prerelease && parsedB.prerelease) {
      const prereleaseCompare = parsedA.prerelease.localeCompare(parsedB.prerelease, undefined, {
        numeric: true,
        sensitivity: "base",
      });
      if (prereleaseCompare !== 0) {
        return prereleaseCompare > 0 ? -1 : 1;
      }
    }
    return 0;
  }

  if (parsedA.valid && !parsedB.valid) {
    return -1;
  }
  if (!parsedA.valid && parsedB.valid) {
    return 1;
  }

  return b.localeCompare(a, undefined, { numeric: true, sensitivity: "base" });
}

function compareReleaseRecords(a: ReleaseRecord, b: ReleaseRecord): number {
  const versionCompare = compareVersionsDescending(a.version, b.version);
  if (versionCompare !== 0) {
    return versionCompare;
  }

  const uploadedAtCompare = b.uploadedAt.getTime() - a.uploadedAt.getTime();
  if (uploadedAtCompare !== 0) {
    return uploadedAtCompare;
  }

  return b.fileName.localeCompare(a.fileName, undefined, {
    numeric: true,
    sensitivity: "base",
  });
}

function validateCatalogEntry(entry: CatalogEntry): Date {
  if (!entry.fileName || !entry.fileName.toLowerCase().endsWith(".zip")) {
    throw new Error("Release catalog contains an invalid filename.");
  }
  if (entry.edition !== "standard" && entry.edition !== "ai") {
    throw new Error(`Release catalog contains an invalid edition for ${entry.fileName}.`);
  }
  if (entry.status !== "stable" && entry.status !== "experimental") {
    throw new Error(`Release catalog contains an invalid status for ${entry.fileName}.`);
  }
  if (!parseVersion(entry.version).valid) {
    throw new Error(`Release catalog contains an invalid version for ${entry.fileName}.`);
  }
  if (!/^[0-9a-f]{40}$/i.test(entry.sourceCommit)) {
    throw new Error(`Release catalog contains an invalid source commit for ${entry.fileName}.`);
  }
  if (entry.edition === "standard" && entry.hermesProfileVersion) {
    throw new Error(`Standard release ${entry.fileName} cannot declare a Hermes profile.`);
  }
  if (entry.edition === "ai" && entry.status !== "experimental") {
    throw new Error(`AI release ${entry.fileName} must be marked experimental.`);
  }

  const releaseDate = new Date(entry.releaseDate);
  if (Number.isNaN(releaseDate.getTime())) {
    throw new Error(`Release catalog contains an invalid release date for ${entry.fileName}.`);
  }
  return releaseDate;
}

async function readCatalogedReleases(): Promise<ReleaseRecord[]> {
  const directoryPath = getReleaseDirectoryPath();
  const entries = releaseCatalog as CatalogEntry[];
  const checksums = checksumManifest as ChecksumMap;
  const seenFiles = new Set<string>();
  const seenSlugs = new Set<string>();

  const records = await Promise.all(
    entries.map(async (entry) => {
      const uploadedAt = validateCatalogEntry(entry);
      const slug = toSlug(entry.fileName);
      if (seenFiles.has(entry.fileName) || seenSlugs.has(slug)) {
        throw new Error(`Release catalog contains a duplicate entry for ${entry.fileName}.`);
      }
      seenFiles.add(entry.fileName);
      seenSlugs.add(slug);

      const sha256 = checksums[entry.fileName]?.trim().toUpperCase();
      if (!sha256 || !/^[0-9A-F]{64}$/.test(sha256)) {
        throw new Error(`Release checksum is missing or invalid for ${entry.fileName}.`);
      }

      const absolutePath = path.join(directoryPath, entry.fileName);
      const stats = await fs.stat(absolutePath);
      if (!stats.isFile()) {
        throw new Error(`Cataloged release is not a file: ${entry.fileName}.`);
      }

      const pathname = `files/${entry.fileName}`;
      const downloadPath = `/${pathname.split("/").map(encodeURIComponent).join("/")}`;
      return {
        version: entry.version,
        edition: entry.edition,
        status: entry.status,
        sourceCommit: entry.sourceCommit,
        hermesProfileVersion: entry.hermesProfileVersion?.trim() || null,
        slug,
        fileName: entry.fileName,
        pathname,
        size: stats.size,
        uploadedAt,
        downloadPath,
        sha256,
      };
    }),
  );

  return records.sort(compareReleaseRecords);
}

export const getReleaseCatalog = cache(async (): Promise<ReleaseCatalog> => {
  try {
    const releases = await readCatalogedReleases();
    return {
      error: null,
      releases,
    };
  } catch {
    return {
      error: "Release catalog could not be loaded.",
      releases: [],
    };
  }
});

export async function getLatestRelease(edition: ReleaseEdition = "standard"): Promise<ReleaseRecord | null> {
  const catalog = await getReleaseCatalog();
  return catalog.releases.find((release) => release.edition === edition) ?? null;
}

export async function getReleaseBySlug(
  slug: string,
  defaultEdition: ReleaseEdition = "standard",
): Promise<ReleaseRecord | null> {
  const catalog = await getReleaseCatalog();
  const normalizedSlug = toSlugValue(slug);
  const exactSlug = catalog.releases.find((release) => release.slug === normalizedSlug);
  if (exactSlug) {
    return exactSlug;
  }

  return (
    catalog.releases.find(
      (release) => release.edition === defaultEdition && toSlugValue(release.version) === normalizedSlug,
    ) ?? null
  );
}

export function getWebsiteUrl(): string {
  return process.env.NEXT_PUBLIC_WEBSITE_URL?.trim() || defaultWebsiteUrl;
}

export function getDocsUrl(): string {
  return process.env.NEXT_PUBLIC_DOCS_URL?.trim() || defaultDocsUrl;
}

export function getDownloadsUrl(): string {
  return process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() || defaultDownloadsUrl;
}

export function buildAbsoluteDownloadUrl(downloadPath: string, requestUrl?: string): string {
  const configuredBase = getDownloadsUrl().replace(/\/$/, "");
  try {
    return new URL(downloadPath, `${configuredBase}/`).toString();
  } catch {
    if (!requestUrl) {
      throw new Error("Unable to build absolute download URL.");
    }

    return new URL(downloadPath, requestUrl).toString();
  }
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
    month: "long",
    day: "numeric",
    year: "numeric",
    timeZone: "UTC",
  }).format(date);
}
