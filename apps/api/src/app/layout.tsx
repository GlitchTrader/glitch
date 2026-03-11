import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const socialImagePath = "/images/Glitch%20Banner.png";

function resolveMetadataBase(fallback: string) {
  const value =
    process.env.NEXT_PUBLIC_API_URL?.trim() ||
    process.env.VERCEL_PROJECT_PRODUCTION_URL?.trim();

  if (!value) {
    return new URL(fallback);
  }

  return new URL(value.startsWith("http") ? value : `https://${value}`);
}

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  metadataBase: resolveMetadataBase("https://api.glitchtrader.com"),
  applicationName: "Glitch API",
  title: "Glitch API - Licensing, Webhooks, and Market Services",
  description:
    "Operational API for Glitch licensing, webhook ingestion, entitlements, market data, and admin services.",
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
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body
        className={`${geistSans.variable} ${geistMono.variable} antialiased`}
      >
        {children}
      </body>
    </html>
  );
}
