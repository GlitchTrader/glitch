import type { Metadata } from "next";
import "./globals.css";

const socialImagePath = "/images/Glitch%20Banner.png";
const faviconPath = "/images/Glitch%20Favicon.png";
const appIconPath = "/images/branding/Glitch%20Icon.png";

function resolveMetadataBase(fallback: string) {
  const value =
    process.env.NEXT_PUBLIC_API_URL?.trim() ||
    process.env.VERCEL_PROJECT_PRODUCTION_URL?.trim();

  if (!value) {
    return new URL(fallback);
  }

  return new URL(value.startsWith("http") ? value : `https://${value}`);
}

export const metadata: Metadata = {
  metadataBase: resolveMetadataBase("https://api.glitchtrader.com"),
  applicationName: "Glitch API",
  manifest: "/manifest.webmanifest",
  title: "Glitch API - Licensing, Webhooks, and Market Services",
  description:
    "Operational API for Glitch licensing, webhook ingestion, entitlements, market data, and admin services.",
  robots: {
    index: false,
    follow: false,
  },
  alternates: {
    canonical: "/",
  },
  openGraph: {
    siteName: "Glitch API",
    title: "Glitch API - Licensing, Webhooks, and Market Services",
    description:
      "Operational endpoints for Glitch licensing, webhooks, entitlements, market data, and admin services.",
    url: "/",
    locale: "en_US",
    type: "website",
    images: [
      {
        url: socialImagePath,
        alt: "Glitch trading assistant banner",
        width: 1536,
        height: 1024,
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch API - Licensing, Webhooks, and Market Services",
    description:
      "Operational endpoints for Glitch licensing, webhooks, entitlements, market data, and admin services.",
    images: [socialImagePath],
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
      <body className="antialiased">
        {children}
      </body>
    </html>
  );
}
