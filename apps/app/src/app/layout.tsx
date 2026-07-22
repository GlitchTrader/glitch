import type { Metadata } from "next";
import "./globals.css";

const socialImagePath = "/images/Glitch%20Banner.png";
const faviconPath = "/images/Glitch%20Favicon.png";
const appIconPath = "/images/branding/Glitch%20Icon.png";

function resolveMetadataBase(fallback: string) {
  const value =
    process.env.NEXT_PUBLIC_APP_URL?.trim() ||
    process.env.VERCEL_PROJECT_PRODUCTION_URL?.trim();

  if (!value) {
    return new URL(fallback);
  }

  return new URL(value.startsWith("http") ? value : `https://${value}`);
}

export const metadata: Metadata = {
  metadataBase: resolveMetadataBase("https://app.glitchtrader.com"),
  applicationName: "Glitch App",
  manifest: "/manifest.webmanifest",
  title: "Glitch App - Member Workspace for Prop Traders",
  description:
    "Glitch App is the member workspace for downloads, onboarding, updates, and trading assistant tools.",
  robots: {
    index: false,
    follow: false,
  },
  alternates: {
    canonical: "/",
  },
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
        width: 1536,
        height: 1024,
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
