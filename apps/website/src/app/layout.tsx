import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import Link from "next/link";
import { marketingLinks } from "@/lib/marketing-links";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Glitch - Risk-First NinjaTrader AddOn for Prop Traders",
  description:
    "Glitch helps prop traders protect eval and funded accounts with compliance enforcement, replication guardrails, GlitchScore analytics, and macro context.",
  openGraph: {
    title: "Glitch - Risk-First NinjaTrader AddOn for Prop Traders",
    description:
      "Protect accounts with compliance enforcement, replication guardrails, and multi-timeframe GlitchScore context.",
    images: ["/images/Glitch Banner 4-1 .jpg"],
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch - Risk-First NinjaTrader AddOn for Prop Traders",
    description:
      "Protect accounts with compliance enforcement, replication guardrails, and multi-timeframe GlitchScore context.",
    images: ["/images/Glitch Banner 4-1 .jpg"],
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
          <Link
            href={marketingLinks.goProCheckoutUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Go Pro
          </Link>
          <Link
            href={marketingLinks.memberHubUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Member Hub
          </Link>
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
          <Link
            href={marketingLinks.goProCheckoutUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Go Pro
          </Link>
          <Link
            href={marketingLinks.memberHubUrl}
            className="text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
          >
            Member Hub
          </Link>
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
