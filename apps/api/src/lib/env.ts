const truthyValues = new Set(["1", "true", "yes", "on"]);

export function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value || value.trim().length === 0) {
    throw new Error(`Missing required environment variable: ${name}`);
  }

  return value.trim();
}

export function readBooleanEnv(name: string, defaultValue = false): boolean {
  const value = process.env[name];
  if (!value) {
    return defaultValue;
  }

  return truthyValues.has(value.trim().toLowerCase());
}

