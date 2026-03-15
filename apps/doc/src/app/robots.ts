import type { MetadataRoute } from "next";
import { docsSiteUrl, resolveSiteUrl } from "@/lib/site";

export default function robots(): MetadataRoute.Robots {
  const origin = resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl).toString().replace(/\/$/, "");

  return {
    rules: {
      userAgent: "*",
      allow: "/",
    },
    sitemap: `${origin}/sitemap.xml`,
    host: origin,
  };
}
