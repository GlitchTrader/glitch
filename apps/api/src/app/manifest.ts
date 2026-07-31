import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Glitch API",
    short_name: "Glitch API",
    description: "Operational API for Glitch licensing, entitlements, and market services.",
    start_url: "/",
    scope: "/",
    display: "standalone",
    background_color: "#09090b",
    theme_color: "#ff4f00",
    icons: [
      {
        src: "/images/branding/Glitch%20Icon.png",
        sizes: "512x512",
        type: "image/png",
        purpose: "any",
      },
    ],
  };
}
