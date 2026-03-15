import { existsSync } from "node:fs";
import { mkdir, readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const appRoot = path.resolve(__dirname, "..");
const filesDirectory = path.join(appRoot, "public", "files");
const manifestPath = path.join(appRoot, "src", "lib", "release-dates.json");

function sortNames(a, b) {
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: "base" });
}

function isValidIsoDate(value) {
  if (typeof value !== "string" || value.length === 0) {
    return false;
  }

  return !Number.isNaN(new Date(value).getTime());
}

async function readExistingManifest() {
  if (!existsSync(manifestPath)) {
    return {};
  }

  try {
    const raw = await readFile(manifestPath, "utf8");
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {};
    }

    return parsed;
  } catch {
    return {};
  }
}

async function getReleaseFiles() {
  const entries = await readdir(filesDirectory, { withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".zip"))
    .map((entry) => entry.name)
    .sort(sortNames);
}

async function syncReleaseDates() {
  const [releaseFiles, existingManifest] = await Promise.all([getReleaseFiles(), readExistingManifest()]);
  const nextManifest = {};
  let addedCount = 0;

  for (const fileName of releaseFiles) {
    const existingDate = existingManifest[fileName];
    if (isValidIsoDate(existingDate)) {
      nextManifest[fileName] = existingDate;
      continue;
    }

    nextManifest[fileName] = new Date().toISOString();
    addedCount += 1;
  }

  const nextJson = `${JSON.stringify(nextManifest, null, 2)}\n`;
  const previousJson = existsSync(manifestPath) ? await readFile(manifestPath, "utf8") : null;

  if (previousJson !== nextJson) {
    await mkdir(path.dirname(manifestPath), { recursive: true });
    await writeFile(manifestPath, nextJson, "utf8");
  }

  console.log(
    `[sync-release-dates] release files: ${releaseFiles.length}, new dates added: ${addedCount}, manifest: ${manifestPath}`,
  );
}

syncReleaseDates().catch((error) => {
  console.error("[sync-release-dates] failed:", error);
  process.exitCode = 1;
});
