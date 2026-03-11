import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const socialImagePath = "/images/Glitch%20Banner.png";

function resolveMetadataBase(fallback: string) {
  const value =
    process.env.NEXT_PUBLIC_APP_URL?.trim() ||
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
  metadataBase: resolveMetadataBase("https://app.glitchtrader.com"),
  applicationName: "Glitch App",
  title: "Glitch App - Member Workspace for Prop Traders",
  description:
    "Glitch App is the member workspace for downloads, onboarding, updates, and trading assistant tools.",
  openGraph: {
    siteName: "Glitch App",
    title: "Glitch App - Member Workspace for Prop Traders",
    description:
      "Access downloads, onboarding, updates, and Glitch member tools in one workspace.",
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
    title: "Glitch App - Member Workspace for Prop Traders",
    description:
      "Access downloads, onboarding, updates, and Glitch member tools in one workspace.",
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
