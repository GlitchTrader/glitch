import type { Metadata } from "next";
import "./globals.css";
import { docsSocialImagePath } from "@/lib/metadata";
import { getDocLanguages } from "@/lib/docs-locales";
import { docsSiteUrl, resolveSiteUrl } from "@/lib/site";

const faviconPath = "/images/Glitch%20Favicon.png";
const appIconPath = "/images/branding/Glitch%20Icon.png";

export const metadata: Metadata = {
  metadataBase: resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl),
  applicationName: "Glitch Docs",
  manifest: "/manifest.webmanifest",
  title: {
    default: "Glitch Docs - NinjaTrader AddOn Documentation",
    template: "%s - Glitch Docs",
  },
  description: "Technical documentation for the live Glitch NinjaTrader AddOn and GlitchAnalyticsBridge indicator.",
  alternates: {
    canonical: "/",
    languages: getDocLanguages(),
  },
  openGraph: {
    siteName: "Glitch Docs",
    title: "Glitch Docs - NinjaTrader AddOn Documentation",
    description: "Technical documentation for the live Glitch NinjaTrader AddOn and GlitchAnalyticsBridge indicator.",
    url: "/",
    locale: "en_US",
    type: "website",
    images: [
      {
        url: docsSocialImagePath,
        alt: "Glitch documentation banner",
        width: 1536,
        height: 1024,
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch Docs - NinjaTrader AddOn Documentation",
    description: "Technical documentation for the live Glitch NinjaTrader AddOn and GlitchAnalyticsBridge indicator.",
    images: [docsSocialImagePath],
  },
  icons: {
    icon: [
      { url: "/favicon.ico", sizes: "32x32", type: "image/x-icon" },
      { url: faviconPath, sizes: "32x32", type: "image/png" },
    ],
    shortcut: faviconPath,
    apple: [{ url: appIconPath, sizes: "512x512", type: "image/png" }],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className="min-h-screen antialiased">{children}</body>
    </html>
  );
}
