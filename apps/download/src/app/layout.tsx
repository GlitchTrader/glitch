import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { getDownloadsUrl } from "@/lib/releases";
import "./globals.css";

const socialImagePath = "/images/Glitch Banner.png";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

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
  metadataBase: resolveMetadataBase("https://downloads.glitchtrader.com"),
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
      <body className={`${geistSans.variable} ${geistMono.variable} min-h-screen antialiased`}>
        {children}
      </body>
    </html>
  );
}
