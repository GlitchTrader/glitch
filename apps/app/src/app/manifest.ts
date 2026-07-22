import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Glitch App",
    short_name: "Glitch",
    description: "Glitch member workspace for downloads, onboarding, and product tools.",
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
