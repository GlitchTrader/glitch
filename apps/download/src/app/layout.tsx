import type { Metadata } from "next";
import { getDownloadsUrl } from "@/lib/releases";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import "./globals.css";

const socialImagePath = "/images/Glitch Banner.png";

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
  title: "Glitch Downloads - NinjaTrader Releases",
  description:
    "Official Glitch NinjaTrader release downloads, version history, and direct release links for operators and members.",
  robots: {
    index: false,
    follow: false,
  },
  alternates: {
    canonical: getDownloadsUrl(),
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
        url: socialImagePath,
        alt: "Glitch download banner",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch Downloads - NinjaTrader Releases",
    description:
      "Official Glitch NinjaTrader release downloads, version history, and direct release links.",
    images: [socialImagePath],
  },
  icons: {
    icon: "/images/Glitch Favicon.png",
    shortcut: "/images/Glitch Favicon.png",
    apple: "/images/Glitch Favicon.png",
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
