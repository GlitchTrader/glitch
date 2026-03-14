import { createHash } from "node:crypto";
import { createReadStream, existsSync } from "node:fs";
import { promises as fs } from "node:fs";
import path from "node:path";
import { cache } from "react";

const defaultWebsiteUrl = "https://glitchtrader.com";
const defaultDocsUrl = "https://docs.glitchtrader.com";
const defaultDownloadsUrl = "https://download.glitchtrader.com";

export type ReleaseRecord = {
  version: string;
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

type ParsedVersion = {
  numericParts: number[];
  prerelease: string | null;
  valid: boolean;
};

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

function deriveVersion(fileName: string): string {
  const baseName = getBaseName(fileName);
  const match = baseName.match(/(\d+(?:\.\d+)+(?:-[0-9A-Za-z.-]+)?)/);
  return match?.[1] ?? baseName;
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

function resolvePublicOrigin(): string {
  const vercelUrl = process.env.VERCEL_URL?.trim();
  if (vercelUrl) {
    const normalized = vercelUrl.startsWith("http") ? vercelUrl : `https://${vercelUrl}`;
    return normalized.replace(/\/+$/, "");
  }

  const configured = process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() || defaultDownloadsUrl;
  const normalized = configured.startsWith("http") ? configured : `https://${configured}`;
  return normalized.replace(/\/+$/, "");
}

async function resolveReleaseDate(downloadPath: string, fallbackDate: Date): Promise<Date> {
  const origin = resolvePublicOrigin();

  try {
    const response = await fetch(`${origin}${downloadPath}`, {
      method: "HEAD",
      cache: "no-store",
    });

    if (!response.ok) {
      return fallbackDate;
    }

    const header = response.headers.get("last-modified");
    if (!header) {
      return fallbackDate;
    }

    const parsed = new Date(header);
    if (Number.isNaN(parsed.getTime())) {
      return fallbackDate;
    }

    return parsed;
  } catch {
    return fallbackDate;
  }
}

async function hashFileSha256(filePath: string): Promise<string> {
  const hash = createHash("sha256");
  const stream = createReadStream(filePath);
  for await (const chunk of stream) {
    hash.update(chunk);
  }
  return hash.digest("hex").toUpperCase();
}

async function readLocalReleases(): Promise<ReleaseRecord[]> {
  const directoryPath = getReleaseDirectoryPath();
  const entries = await fs.readdir(directoryPath, { withFileTypes: true });

  const zipFiles = entries
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".zip"))
    .map((entry) => entry.name);

  const records = await Promise.all(
    zipFiles.map(async (fileName) => {
      const absolutePath = path.join(directoryPath, fileName);
      const [stats, sha256] = await Promise.all([fs.stat(absolutePath), hashFileSha256(absolutePath)]);
      const version = deriveVersion(fileName);
      const pathname = `files/${fileName}`;
      const downloadPath = `/${pathname.split("/").map(encodeURIComponent).join("/")}`;
      const uploadedAt = await resolveReleaseDate(downloadPath, new Date(stats.mtime));

      return {
        version,
        slug: toSlug(fileName),
        fileName,
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
    const releases = await readLocalReleases();
    return {
      error: null,
      releases,
    };
  } catch (error) {
    if ((error as NodeJS.ErrnoException)?.code === "ENOENT") {
      return {
        error: null,
        releases: [],
      };
    }

    return {
      error: "Release catalog could not be loaded.",
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
  const normalizedSlug = toSlugValue(slug);
  return (
    catalog.releases.find(
      (release) => release.slug === normalizedSlug || toSlugValue(release.version) === normalizedSlug,
    ) ?? null
  );
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
