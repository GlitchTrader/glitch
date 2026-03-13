import { readOptionalEnv } from "@/lib/env";

const legacySslModeAliases = new Set(["prefer", "require", "verify-ca"]);
const truthyValues = new Set(["1", "true", "yes", "on"]);

function isTruthy(value: string | null): boolean {
  if (!value) {
    return false;
  }

  return truthyValues.has(value.trim().toLowerCase());
}

export function normalizeDatabaseUrl(value: string): string {
  const trimmed = value.trim();

  let parsed: URL;
  try {
    parsed = new URL(trimmed);
  } catch {
    return trimmed;
  }

  const protocol = parsed.protocol.toLowerCase();
  if (protocol !== "postgres:" && protocol !== "postgresql:") {
    return trimmed;
  }

  if (isTruthy(parsed.searchParams.get("uselibpqcompat"))) {
    return trimmed;
  }

  const sslmode = parsed.searchParams.get("sslmode");
  if (!sslmode) {
    return trimmed;
  }

  if (!legacySslModeAliases.has(sslmode.trim().toLowerCase())) {
    return trimmed;
  }

  parsed.searchParams.set("sslmode", "verify-full");
  return parsed.toString();
}

export function readDatabaseUrl(): string | null {
  const raw = readOptionalEnv("DATABASE_URL");
  if (!raw) {
    return null;
  }

  return normalizeDatabaseUrl(raw);
}
