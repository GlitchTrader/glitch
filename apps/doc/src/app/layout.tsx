import type { Metadata } from "next";
import "./globals.css";
import { docsSiteUrl, resolveSiteUrl } from "@/lib/site";

export const metadata: Metadata = {
  metadataBase: resolveSiteUrl("NEXT_PUBLIC_DOCS_URL", docsSiteUrl),
  applicationName: "Glitch Docs",
  title: {
    default: "Glitch Docs - NinjaTrader AddOn Documentation",
    template: "%s - Glitch Docs",
  },
  description: "Technical documentation for the live Glitch NinjaTrader AddOn and GlitchAnalyticsBridge indicator.",
  alternates: {
    canonical: "/",
  },
  openGraph: {
    siteName: "Glitch Docs",
    title: "Glitch Docs - NinjaTrader AddOn Documentation",
    description: "Technical documentation for the live Glitch NinjaTrader AddOn and GlitchAnalyticsBridge indicator.",
    url: "/",
    locale: "en_US",
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "Glitch Docs - NinjaTrader AddOn Documentation",
    description: "Technical documentation for the live Glitch NinjaTrader AddOn and GlitchAnalyticsBridge indicator.",
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
      <body className="min-h-screen antialiased">{children}</body>
    </html>
  );
}
