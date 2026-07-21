import { createHash } from "node:crypto";
import { createReadStream, existsSync } from "node:fs";
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const appRoot = path.resolve(__dirname, "..");
const filesDirectory = path.join(appRoot, "public", "files");
const catalogPath = path.join(appRoot, "src", "lib", "release-catalog.json");
const checksumsPath = path.join(filesDirectory, "checksums.json");
const allowedEditions = new Set(["standard", "ai"]);
const allowedStatuses = new Set(["stable", "experimental"]);
const versionPattern = /^\d+(?:\.\d+){1,}(?:-[0-9A-Za-z.-]+)?$/;
const commitPattern = /^[0-9a-f]{40}$/i;
const checksumPattern = /^[0-9a-f]{64}$/i;

function fail(message) {
  throw new Error(`[validate-releases] ${message}`);
}

async function readJson(filePath) {
  try {
    return JSON.parse(await readFile(filePath, "utf8"));
  } catch (error) {
    fail(`could not read ${filePath}: ${error instanceof Error ? error.message : String(error)}`);
  }
}

async function hashFileSha256(filePath) {
  const hash = createHash("sha256");
  const stream = createReadStream(filePath);
  for await (const chunk of stream) {
    hash.update(chunk);
  }
  return hash.digest("hex").toUpperCase();
}

function expectedFileName(entry) {
  return entry.edition === "ai"
    ? `Glitch_AI_v${entry.version}.zip`
    : `Glitch_v${entry.version}.zip`;
}

async function validateReleases() {
  const [catalog, checksums] = await Promise.all([readJson(catalogPath), readJson(checksumsPath)]);
  if (!Array.isArray(catalog)) {
    fail("release-catalog.json must contain an array.");
  }
  if (!checksums || typeof checksums !== "object" || Array.isArray(checksums)) {
    fail("checksums.json must contain an object.");
  }

  const files = existsSync(filesDirectory)
    ? (await readdir(filesDirectory, { withFileTypes: true }))
      .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".zip"))
      .map((entry) => entry.name)
      .sort()
    : [];
  const catalogFiles = new Set();
  const editionVersions = new Set();

  for (const entry of catalog) {
    if (!entry || typeof entry !== "object" || Array.isArray(entry)) {
      fail("every catalog entry must be an object.");
    }
    if (!allowedEditions.has(entry.edition)) {
      fail(`${entry.fileName ?? "unknown"} has an invalid edition.`);
    }
    if (!allowedStatuses.has(entry.status)) {
      fail(`${entry.fileName ?? "unknown"} has an invalid status.`);
    }
    if (entry.edition === "ai" && entry.status !== "experimental") {
      fail(`${entry.fileName ?? "unknown"} must mark the AI edition experimental.`);
    }
    if (entry.edition === "standard" && entry.hermesProfileVersion) {
      fail(`${entry.fileName ?? "unknown"} cannot attach Hermes to the Standard edition.`);
    }
    if (typeof entry.version !== "string" || !versionPattern.test(entry.version)) {
      fail(`${entry.fileName ?? "unknown"} has an invalid version.`);
    }
    if (entry.fileName !== expectedFileName(entry)) {
      fail(`${entry.fileName ?? "unknown"} does not match its edition and version.`);
    }
    if (typeof entry.releaseDate !== "string" || Number.isNaN(new Date(entry.releaseDate).getTime())) {
      fail(`${entry.fileName} has an invalid release date.`);
    }
    if (typeof entry.sourceCommit !== "string" || !commitPattern.test(entry.sourceCommit)) {
      fail(`${entry.fileName} has an invalid source commit.`);
    }
    if (catalogFiles.has(entry.fileName)) {
      fail(`${entry.fileName} is registered more than once.`);
    }
    const editionVersion = `${entry.edition}:${entry.version.toLowerCase()}`;
    if (editionVersions.has(editionVersion)) {
      fail(`${entry.edition} ${entry.version} is registered more than once.`);
    }
    catalogFiles.add(entry.fileName);
    editionVersions.add(editionVersion);

    const absolutePath = path.join(filesDirectory, entry.fileName);
    if (!existsSync(absolutePath) || !(await stat(absolutePath)).isFile()) {
      fail(`${entry.fileName} is registered but missing.`);
    }
    const expectedChecksum = checksums[entry.fileName];
    if (typeof expectedChecksum !== "string" || !checksumPattern.test(expectedChecksum)) {
      fail(`${entry.fileName} has no valid SHA-256 manifest entry.`);
    }
    const actualChecksum = await hashFileSha256(absolutePath);
    if (actualChecksum !== expectedChecksum.toUpperCase()) {
      fail(`${entry.fileName} does not match checksums.json.`);
    }
  }

  const unregistered = files.filter((fileName) => !catalogFiles.has(fileName));
  if (unregistered.length > 0) {
    fail(`unregistered ZIP files are not publishable: ${unregistered.join(", ")}`);
  }
  const staleChecksums = Object.keys(checksums).filter((fileName) => !catalogFiles.has(fileName));
  if (staleChecksums.length > 0) {
    fail(`checksum entries without catalog records: ${staleChecksums.join(", ")}`);
  }

  console.log(`[validate-releases] validated ${catalog.length} explicit release records.`);
}

validateReleases().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
