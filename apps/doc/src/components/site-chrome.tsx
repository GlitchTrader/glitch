import Image from "next/image";
import { docsSiteUrl, websiteUrl } from "@/lib/site";
import { SiteHeaderClient } from "@/components/site-header";
import { docsLocaleDetails, docsLocales, getDocsHref, type DocsLocale } from "@/lib/docs-locales";

const defaultMemberHubUrl = "https://whop.com/joined/glitchtrader/";
const defaultStartFreeUrl = "https://whop.com/checkout/plan_IROhfJAbF79K6";
const defaultGoProUrl = "https://whop.com/checkout/plan_G81vTccV19dNA";
function trimTrailingSlash(value: string): string {
  return value.replace(/\/+$/, "");
}

function websitePath(pathname: string, locale: DocsLocale): string {
  return `${trimTrailingSlash(websiteUrl)}/${locale}${pathname === "/" ? "" : pathname}`;
}

function downloadPath(locale: DocsLocale): string {
  return `${downloadsUrl}${locale === "en" ? "" : `/${locale}`}`;
}

const docsUrl = trimTrailingSlash(docsSiteUrl);
const downloadsUrl = trimTrailingSlash(
  process.env.NEXT_PUBLIC_DOWNLOADS_URL?.trim() || "https://download.glitchtrader.com",
);
const memberHubUrl = process.env.NEXT_PUBLIC_WHOP_MEMBER_HUB_URL?.trim() || defaultMemberHubUrl;
const startFreeUrl = process.env.NEXT_PUBLIC_WHOP_FREE_ACCESS_URL?.trim() || defaultStartFreeUrl;
const goProUrl = process.env.NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL?.trim() || defaultGoProUrl;

export function SiteHeader({ locale, activeSlug }: { locale: DocsLocale; activeSlug: string | null }) {
  const ui = docsLocaleDetails[locale].ui;
  return (
    <SiteHeaderClient
      websiteUrl={websitePath("/", locale)}
      homeUrl={websitePath("/", locale)}
      pricingUrl={websitePath("/pricing", locale)}
      affiliateUrl={websitePath("/affiliate", locale)}
      downloadsUrl={downloadPath(locale)}
      guideUrl={`${docsUrl}${getDocsHref(locale, "installation-guide-troubleshooting")}`}
      startFreeUrl={startFreeUrl}
      memberHubUrl={memberHubUrl}
      goProUrl={goProUrl}
      labels={ui.headerNav}
      languageLabel={ui.language}
      locale={locale}
      languageLinks={docsLocales.map((item) => ({
        locale: item,
        label: docsLocaleDetails[item].label,
        languageTag: docsLocaleDetails[item].languageTag,
        href: getDocsHref(item, activeSlug),
      }))}
    />
  );
}

export function SiteFooter({ locale }: { locale: DocsLocale }) {
  const ui = docsLocaleDetails[locale].ui;
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
          <h2 className="mt-5 text-lg font-semibold tracking-tight">{ui.footerTitle}</h2>
          <p className="mt-2 max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
            {ui.footerDescription}
          </p>
          <div className="mt-6 flex flex-wrap gap-3">
            <a
              href={downloadPath(locale)}
              className="inline-flex h-11 items-center justify-center rounded-full bg-glitch-orange px-5 text-sm font-medium text-white transition-opacity hover:opacity-90"
            >
              {ui.headerNav.download}
            </a>
            <a
              href={memberHubUrl}
              className="inline-flex h-11 items-center justify-center rounded-full border border-zinc-300 px-5 text-sm font-medium text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-900"
            >
              {ui.headerNav.memberHub}
            </a>
          </div>
        </div>

        <div className="mt-8 flex flex-wrap items-center justify-between gap-4 border-t border-zinc-200 pt-6 text-sm text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
          <span>© {year} Glitch. All rights reserved.</span>
          <div className="flex flex-wrap gap-6">
            <a href={downloadPath(locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {ui.headerNav.download}
            </a>
            <a href={`${docsUrl}${getDocsHref(locale, "installation-guide-troubleshooting")}`} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {ui.headerNav.guide}
            </a>
            <a href={`${docsUrl}${getDocsHref(locale)}`} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {ui.docs}
            </a>
            <a href={websitePath("/risk-disclosure", locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {ui.risk}
            </a>
            <a href={websitePath("/terms", locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {ui.terms}
            </a>
            <a href={websitePath("/privacy", locale)} className="hover:text-zinc-700 dark:hover:text-zinc-300">
              {ui.privacy}
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
