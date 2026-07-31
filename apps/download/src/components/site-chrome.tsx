"use client";

import Image from "next/image";
import { usePathname } from "next/navigation";
import { SiteHeaderClient } from "@/components/site-header";
import { downloadCopy, downloadLocales, getDownloadHomeHref, isDownloadLocale, type DownloadLocale } from "@/lib/download-locales";

const defaultMemberHubUrl = "https://whop.com/joined/glitchtrader/";
const defaultStartFreeUrl = "https://whop.com/checkout/plan_IROhfJAbF79K6";
const defaultGoProUrl = "https://whop.com/checkout/plan_G81vTccV19dNA";
const defaultWebsiteUrl = "https://glitchtrader.com";
const defaultDocsUrl = "https://docs.glitchtrader.com";
const defaultDownloadsUrl = "https://download.glitchtrader.com";
function trimTrailingSlash(value: string): string {
  return value.replace(/\/+$/, "");
}

function websitePath(baseUrl: string, pathname: string, locale: DownloadLocale): string {
  return `${trimTrailingSlash(baseUrl)}/${locale}${pathname === "/" ? "" : pathname}`;
}

function useDownloadLocale(): DownloadLocale {
  const firstSegment = usePathname().split("/").filter(Boolean)[0];
  return firstSegment && isDownloadLocale(firstSegment) ? firstSegment : "en";
}

function docsPath(baseUrl: string, locale: DownloadLocale, slug?: string): string {
  const prefix = locale === "en" ? "" : `/${locale}`;
  return `${trimTrailingSlash(baseUrl)}${prefix}${slug ? `/${slug}` : ""}`;
}

export function SiteHeader() {
  const locale = useDownloadLocale();
  const copy = downloadCopy[locale];
  const websiteUrl = process.env.NEXT_PUBLIC_WEBSITE_URL?.trim() || defaultWebsiteUrl;
  const docsUrl = trimTrailingSlash(process.env.NEXT_PUBLIC_DOCS_URL?.trim() || defaultDocsUrl);
  const memberHubUrl = process.env.NEXT_PUBLIC_WHOP_MEMBER_HUB_URL?.trim() || defaultMemberHubUrl;
  const startFreeUrl = process.env.NEXT_PUBLIC_WHOP_FREE_ACCESS_URL?.trim() || defaultStartFreeUrl;
  const goProUrl = process.env.NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL?.trim() || defaultGoProUrl;

  return (
    <SiteHeaderClient
      websiteUrl={websitePath(websiteUrl, "/", locale)}
      homeUrl={websitePath(websiteUrl, "/", locale)}
      pricingUrl={websitePath(websiteUrl, "/pricing", locale)}
      affiliateUrl={websitePath(websiteUrl, "/affiliate", locale)}
      docsUrl={docsPath(docsUrl, locale)}
      guideUrl={docsPath(docsUrl, locale, "installation-guide-troubleshooting")}
      startFreeUrl={startFreeUrl}
      memberHubUrl={memberHubUrl}
      goProUrl={goProUrl}
      labels={copy.nav}
      languageLabel={copy.language}
      locale={locale}
      languageLinks={downloadLocales.map((item) => ({ locale: item, label: downloadCopy[item].label, languageTag: downloadCopy[item].languageTag, href: getDownloadHomeHref(item) }))}
    />
  );
}

export function SiteFooter() {
  const locale = useDownloadLocale();
  const copy = downloadCopy[locale];
  const websiteUrl = process.env.NEXT_PUBLIC_WEBSITE_URL?.trim() || defaultWebsiteUrl;
  const docsUrl = trimTrailingSlash(process.env.NEXT_PUBLIC_DOCS_URL?.trim() || defaultDocsUrl);
  const downloadsUrl = trimTrailingSlash(process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() || defaultDownloadsUrl);
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
          <h2 className="mt-5 text-lg font-semibold tracking-tight">{copy.footerTitle}</h2>
          <p className="mt-2 max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
            {copy.footerDescription}
          </p>
          <div className="mt-6 flex flex-wrap gap-3">
            <a
              href={downloadsUrl}
              className="inline-flex h-11 items-center justify-center rounded-full bg-glitch-orange px-5 text-sm font-medium text-white transition-opacity hover:opacity-90"
            >
              {copy.download}
            </a>
            <a
              href={memberHubUrl}
              className="inline-flex h-11 items-center justify-center rounded-full border border-zinc-300 px-5 text-sm font-medium text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-900"
            >
              {copy.nav.memberHub}
            </a>
          </div>
        </div>

        <div className="mt-8 flex flex-wrap items-center justify-between gap-4 border-t border-zinc-200 pt-6 text-sm text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
          <span>© {year} Glitch. All rights reserved.</span>
          <div className="flex flex-wrap gap-6">
            <a href={downloadsUrl} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {copy.download}
            </a>
            <a href={docsPath(docsUrl, locale, "installation-guide-troubleshooting")} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {copy.nav.guide}
            </a>
            <a href={docsPath(docsUrl, locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {copy.nav.docs}
            </a>
            <a href={websitePath(websiteUrl, "/risk-disclosure", locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {copy.risk}
            </a>
            <a href={websitePath(websiteUrl, "/terms", locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {copy.terms}
            </a>
            <a href={websitePath(websiteUrl, "/privacy", locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {copy.privacy}
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
