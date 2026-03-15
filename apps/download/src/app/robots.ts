import type { MetadataRoute } from "next";
import { getDownloadsUrl } from "@/lib/releases";

function resolveDownloadsOrigin(): string {
  const value = getDownloadsUrl().trim();
  const normalized = value.startsWith("http") ? value : `https://${value}`;
  return new URL(normalized).toString().replace(/\/$/, "");
}

export default function robots(): MetadataRoute.Robots {
  const origin = resolveDownloadsOrigin();

  return {
    rules: {
      userAgent: "*",
      disallow: "/",
    },
    host: origin,
  };
}
