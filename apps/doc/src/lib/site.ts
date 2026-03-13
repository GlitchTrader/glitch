function readPublicUrl(name: string, fallback: string): string {
  const value = process.env[name]?.trim();
  return value || fallback;
}

export function resolveSiteUrl(envName: string, fallback: string): URL {
  const value = readPublicUrl(envName, fallback);
  return new URL(value.startsWith("http") ? value : `https://${value}`);
}

export const docsSiteUrl = readPublicUrl("NEXT_PUBLIC_DOCS_URL", "https://docs.glitchtrader.com");
export const websiteUrl = readPublicUrl("NEXT_PUBLIC_WEBSITE_URL", "https://glitchtrader.com");
