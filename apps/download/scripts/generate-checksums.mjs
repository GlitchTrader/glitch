import { createHash } from "node:crypto";
import { createReadStream, existsSync } from "node:fs";
import { mkdir, readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const appRoot = path.resolve(__dirname, "..");
const filesDirectory = path.join(appRoot, "public", "files");
const checksumsPath = path.join(filesDirectory, "checksums.json");

function sortNames(a, b) {
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: "base" });
}

async function hashFileSha256(filePath) {
  const hash = createHash("sha256");
  const stream = createReadStream(filePath);
  for await (const chunk of stream) {
    hash.update(chunk);
  }
  return hash.digest("hex").toUpperCase();
}

async function getReleaseFiles() {
  if (!existsSync(filesDirectory)) {
    return [];
  }

  const entries = await readdir(filesDirectory, { withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".zip"))
    .map((entry) => entry.name)
    .sort(sortNames);
}

async function generateChecksums() {
  const releaseFiles = await getReleaseFiles();
  const checksums = {};

  for (const fileName of releaseFiles) {
    const absolutePath = path.join(filesDirectory, fileName);
    checksums[fileName] = await hashFileSha256(absolutePath);
  }

  const nextJson = `${JSON.stringify(checksums, null, 2)}\n`;
  const previousJson = existsSync(checksumsPath) ? await readFile(checksumsPath, "utf8") : null;

  if (previousJson !== nextJson) {
    await mkdir(filesDirectory, { recursive: true });
    await writeFile(checksumsPath, nextJson, "utf8");
  }

  console.log(
    `[generate-checksums] release files: ${releaseFiles.length}, manifest: ${checksumsPath}`,
  );
}

generateChecksums().catch((error) => {
  console.error("[generate-checksums] failed:", error);
  process.exitCode = 1;
});
