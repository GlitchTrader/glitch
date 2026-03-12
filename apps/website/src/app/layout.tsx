import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import Link from "next/link";
import { ExternalLink } from "@/components/external-link";
import { marketingLinks } from "@/lib/marketing-links";
import "./globals.css";

const socialImagePath = "/images/Glitch%20Banner.png";

function resolveMetadataBase(fallback: string) {
  const value =
    process.env.NEXT_PUBLIC_SITE_URL?.trim() ||
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
  metadataBase: resolveMetadataBase("https://www.glitchtrader.com"),
  applicationName: "Glitch",
  title: "Glitch - Risk-First NinjaTrader AddOn for Prop Traders",
  description:
    "Glitch helps prop traders protect eval and funded accounts with compliance enforcement, replication guardrails, Glitch Score analytics, and macro context.",
  keywords: [
    "Glitch",
    "NinjaTrader AddOn",
    "prop trading assistant",
    "trade replication",
    "prop firm compliance",
    "Glitch Score",
  ],
  alternates: {
    canonical: "/",
  },
  openGraph: {
    siteName: "Glitch",
    title: "Glitch - Risk-First NinjaTrader AddOn for Prop Traders",
    description:
      "Protect accounts with compliance enforcement, replication guardrails, and multi-timeframe Glitch Score context.",
    url: "/",
    locale: "en_US",
    images: [
      {
        url: socialImagePath,
        alt: "Glitch trading assistant banner",
      },
    ],
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch - Risk-First NinjaTrader AddOn for Prop Traders",
    description:
      "Protect accounts with compliance enforcement, replication guardrails, and multi-timeframe Glitch Score context.",
    images: [socialImagePath],
  },
  icons: {
    icon: "/images/Glitch%20Favicon.png",
    shortcut: "/images/Glitch%20Favicon.png",
    apple: "/images/Glitch%20Favicon.png",
  },
};

function SiteHeader() {
  return (
    <header className="sticky top-0 z-50 border-b border-zinc-200 bg-white/95 backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/95">
      <div className="mx-auto flex h-14 max-w-6xl items-center justify-between px-4 sm:px-6">
        <Link
          href="/"
          className="font-semibold tracking-tight text-zinc-900 dark:text-zinc-100"
        >
          Glitch
        </Link>
        <nav className="flex items-center gap-4 text-sm md:hidden">
          <ExternalLink
            href={marketingLinks.goProCheckoutUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Go Pro
          </ExternalLink>
          <ExternalLink
            href={marketingLinks.memberHubUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Member Hub
          </ExternalLink>
        </nav>
        <nav className="hidden items-center gap-6 text-sm md:flex">
          <Link
            href="/"
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Home
          </Link>
          <Link
            href="/offer"
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Offer
          </Link>
          <Link
            href="/pricing"
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Pricing
          </Link>
          <Link
            href="/affiliate"
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Affiliate
          </Link>
          <ExternalLink
            href={marketingLinks.goProCheckoutUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Go Pro
          </ExternalLink>
          <ExternalLink
            href={marketingLinks.memberHubUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Member Hub
          </ExternalLink>
        </nav>
      </div>
    </header>
  );
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body
        className={`${geistSans.variable} ${geistMono.variable} min-h-screen antialiased`}
      >
        <SiteHeader />
        {children}
      </body>
    </html>
  );
}
