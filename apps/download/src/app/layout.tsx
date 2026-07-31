import type { Metadata } from "next";
import { getDownloadsUrl } from "@/lib/releases";
import { downloadCopy, downloadLocales, getDownloadHomeHref } from "@/lib/download-locales";
import { downloadSocialImagePath } from "@/lib/metadata";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import "./globals.css";

const faviconPath = "/images/Glitch%20Favicon.png";
const appIconPath = "/images/branding/Glitch%20Icon.png";

function resolveMetadataBase(fallback: string) {
  const value =
    process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() ||
    process.env.VERCEL_PROJECT_PRODUCTION_URL?.trim();

  if (!value) {
    return new URL(fallback);
  }

  return new URL(value.startsWith("http") ? value : `https://${value}`);
}

export const metadata: Metadata = {
  metadataBase: resolveMetadataBase("https://download.glitchtrader.com"),
  applicationName: "Glitch Downloads",
  manifest: "/manifest.webmanifest",
  title: "Glitch Downloads - NinjaTrader Releases",
  description:
    "Official Glitch NinjaTrader release downloads, version history, and direct release links for operators and members.",
  robots: {
    index: false,
    follow: false,
  },
  alternates: {
    canonical: getDownloadsUrl(),
    languages: Object.fromEntries(downloadLocales.map((locale) => [downloadCopy[locale].languageTag, getDownloadHomeHref(locale)])),
  },
  openGraph: {
    siteName: "Glitch Downloads",
    title: "Glitch Downloads - NinjaTrader Releases",
    description:
      "Official Glitch NinjaTrader release downloads, version history, and direct release links.",
    url: "/",
    locale: "en_US",
    type: "website",
    images: [
      {
        url: downloadSocialImagePath,
        alt: "Glitch download banner",
        width: 1536,
        height: 1024,
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch Downloads - NinjaTrader Releases",
    description:
      "Official Glitch NinjaTrader release downloads, version history, and direct release links.",
    images: [downloadSocialImagePath],
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
      <body className="min-h-screen antialiased">
        <SiteHeader />
        {children}
        <SiteFooter />
      </body>
    </html>
  );
}
