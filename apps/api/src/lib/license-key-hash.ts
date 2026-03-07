import { createHmac } from "crypto";

function normalizeLicenseKey(licenseKey: string): string {
  return licenseKey.trim();
}

export function hashLicenseKey(licenseKey: string, secret: string): string {
  const normalizedKey = normalizeLicenseKey(licenseKey);
  const normalizedSecret = secret.trim();
  if (normalizedKey.length === 0) {
    throw new Error("License key cannot be empty.");
  }
  if (normalizedSecret.length === 0) {
    throw new Error("License hash secret cannot be empty.");
  }

  return createHmac("sha256", normalizedSecret).update(normalizedKey, "utf8").digest("hex");
}
