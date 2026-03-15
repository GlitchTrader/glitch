import Image from "next/image";
import { getDocsUrl, getDownloadsUrl, getWebsiteUrl } from "@/lib/releases";
import { SiteHeaderClient } from "@/components/site-header";

const defaultMemberHubUrl = "https://whop.com/joined/glitchtrader/";
const defaultGoProUrl = "https://whop.com/joined/glitchtrader/products/glitch-ninjatrader-addon/";
const installationGuideUrl = "https://docs.glitchtrader.com/installation-guide-troubleshooting";

function trimTrailingSlash(value: string): string {
  return value.replace(/\/+$/, "");
}

function websitePath(baseUrl: string, pathname: string): string {
  return `${trimTrailingSlash(baseUrl)}${pathname}`;
}

export function SiteHeader() {
  const websiteUrl = getWebsiteUrl();
  const docsUrl = trimTrailingSlash(getDocsUrl());
  const memberHubUrl = process.env.NEXT_PUBLIC_WHOP_MEMBER_HUB_URL?.trim() || defaultMemberHubUrl;
  const goProUrl = process.env.NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL?.trim() || defaultGoProUrl;

  return (
    <SiteHeaderClient
      websiteUrl={websiteUrl}
      homeUrl={websitePath(websiteUrl, "/")}
      pricingUrl={websitePath(websiteUrl, "/pricing")}
      affiliateUrl={websitePath(websiteUrl, "/affiliate")}
      docsUrl={docsUrl}
      guideUrl={installationGuideUrl}
      memberHubUrl={memberHubUrl}
      goProUrl={goProUrl}
    />
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
            <a href={downloadsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Download
            </a>
            <a href={installationGuideUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Guide
            </a>
            <a href={docsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Docs
            </a>
            <a href={websitePath(websiteUrl, "/risk-disclosure")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Risk
            </a>
            <a href={websitePath(websiteUrl, "/terms")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Terms
            </a>
            <a href={websitePath(websiteUrl, "/privacy")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Privacy
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
