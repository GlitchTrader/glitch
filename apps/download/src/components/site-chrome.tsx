import Image from "next/image";
import { getDocsUrl, getDownloadsUrl, getWebsiteUrl } from "@/lib/releases";

const defaultMemberHubUrl = "https://whop.com/joined/glitchtrader/";
const defaultGoProUrl = "https://whop.com/joined/glitchtrader/products/glitch-ninjatrader-addon/";

function trimTrailingSlash(value: string): string {
  return value.replace(/\/+$/, "");
}

function websitePath(baseUrl: string, pathname: string): string {
  return `${trimTrailingSlash(baseUrl)}${pathname}`;
}

const navLinkClass = "text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100";
const memberHubClass = "font-medium text-glitch-teal hover:text-glitch-teal/80";
const goProClass = "font-medium text-glitch-orange hover:text-glitch-orange/80";

export function SiteHeader() {
  const websiteUrl = getWebsiteUrl();
  const docsUrl = trimTrailingSlash(getDocsUrl());
  const downloadsUrl = trimTrailingSlash(getDownloadsUrl());
  const memberHubUrl = process.env.NEXT_PUBLIC_WHOP_MEMBER_HUB_URL?.trim() || defaultMemberHubUrl;
  const goProUrl = process.env.NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL?.trim() || defaultGoProUrl;

  return (
    <header className="sticky top-0 z-50 border-b border-zinc-200 bg-white/95 backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/95">
      <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
        <a href={websiteUrl} aria-label="Glitch home" className="flex items-center">
          <Image
            src="/images/branding/Glitch Logo.svg"
            alt="Glitch"
            width={90}
            height={24}
            className="h-6 w-auto"
            unoptimized
            priority
          />
        </a>

        <nav className="hidden items-center gap-6 text-sm md:flex">
          <a href={websitePath(websiteUrl, "/")} className={navLinkClass}>
            Home
          </a>
          <a href={websitePath(websiteUrl, "/offer")} className={navLinkClass}>
            Offer
          </a>
          <a href={websitePath(websiteUrl, "/pricing")} className={navLinkClass}>
            Pricing
          </a>
          <a href={websitePath(websiteUrl, "/affiliate")} className={navLinkClass}>
            Affiliate
          </a>
          <a href={docsUrl} className={navLinkClass}>
            Docs
          </a>
          <a href={downloadsUrl} className={navLinkClass}>
            Download
          </a>
          <a href={memberHubUrl} className={memberHubClass}>
            Member Hub
          </a>
          <a href={goProUrl} className={goProClass}>
            Go Pro
          </a>
        </nav>
      </div>
    </header>
  );
}

export function SiteFooter() {
  const websiteUrl = getWebsiteUrl();
  const docsUrl = trimTrailingSlash(getDocsUrl());
  const downloadsUrl = trimTrailingSlash(getDownloadsUrl());
  const memberHubUrl = process.env.NEXT_PUBLIC_WHOP_MEMBER_HUB_URL?.trim() || defaultMemberHubUrl;
  const year = new Date().getFullYear();

  return (
    <footer className="border-t border-zinc-200 dark:border-zinc-800">
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <div className="w-full md:max-w-2xl">
          <Image
            src="/images/branding/Glitch Logo.svg"
            alt="Glitch"
            width={110}
            height={30}
            className="h-7 w-auto"
            unoptimized
          />
          <h2 className="mt-5 text-lg font-semibold tracking-tight">Need product access or account help?</h2>
          <p className="mt-2 max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
            Use the same official links across Website, Docs, and Download for onboarding and updates.
          </p>
          <div className="mt-6 flex flex-wrap gap-3">
            <a
              href={downloadsUrl}
              className="inline-flex h-11 items-center justify-center rounded-full bg-glitch-orange px-5 text-sm font-medium text-white transition-opacity hover:opacity-90"
            >
              Download
            </a>
            <a
              href={memberHubUrl}
              className="inline-flex h-11 items-center justify-center rounded-full border border-zinc-300 px-5 text-sm font-medium text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-900"
            >
              Member Hub
            </a>
          </div>
        </div>

        <div className="mt-8 flex flex-wrap items-center justify-between gap-4 border-t border-zinc-200 pt-6 text-sm text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
          <span>© {year} Glitch. All rights reserved.</span>
          <div className="flex flex-wrap gap-6">
            <a href={docsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Docs
            </a>
            <a href={websitePath(websiteUrl, "/risk-disclosure")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Risk Disclosure
            </a>
            <a href={websitePath(websiteUrl, "/terms")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Terms
            </a>
            <a href={websitePath(websiteUrl, "/privacy")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Privacy
            </a>
            <a href={downloadsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Downloads
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
