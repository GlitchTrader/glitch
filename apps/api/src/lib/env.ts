const truthyValues = new Set(["1", "true", "yes", "on"]);

export function readOptionalEnv(name: string): string | null {
  const value = process.env[name];
  if (!value || value.trim().length === 0) {
    return null;
  }

  return value.trim();
}

export function requireEnv(name: string): string {
  const value = readOptionalEnv(name);
  if (!value) {
    throw new Error(`Missing required environment variable: ${name}`);
  }

  return value;
}

export function readBooleanEnv(name: string, defaultValue = false): boolean {
  const value = process.env[name];
  if (!value) {
    return defaultValue;
  }

  return truthyValues.has(value.trim().toLowerCase());
}
