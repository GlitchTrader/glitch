import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Glitch Docs",
    short_name: "Glitch Docs",
    description: "Technical documentation for the Glitch NinjaTrader AddOn and indicator.",
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
