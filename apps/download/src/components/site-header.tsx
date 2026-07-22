"use client";

import Image from "next/image";
import { useState } from "react";
import { LanguageSwitcher } from "@/components/language-switcher";

type SiteHeaderProps = {
  websiteUrl: string;
  homeUrl: string;
  pricingUrl: string;
  affiliateUrl: string;
  docsUrl: string;
  guideUrl: string;
  startFreeUrl: string;
  memberHubUrl: string;
  goProUrl: string;
  labels: { home: string; pricing: string; affiliate: string; docs: string; guide: string; startFree: string; memberHub: string; goPro: string };
  languageLabel: string;
  locale: string;
  languageLinks: Array<{ locale: string; label: string; languageTag: string; href: string }>;
};

const navLinkClass = "text-white hover:text-white/80";
const memberHubClass = "font-medium !text-glitch-teal hover:!text-glitch-teal/80";
const goProClass = "font-medium !text-glitch-orange hover:!text-glitch-orange/80";
const mobileItemClass =
  "flex items-center justify-between rounded-2xl border border-zinc-200/80 bg-zinc-50/85 px-4 py-3 text-sm transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-900/90 dark:hover:bg-zinc-800";
const mobileNeutralTextClass = "text-white";

function MenuIcon({ open }: { open: boolean }) {
  return (
    <span className="relative block h-4 w-5" aria-hidden>
      <span
        className={`absolute left-0 top-0.5 h-0.5 w-5 rounded-full bg-current transition-transform duration-200 ${
          open ? "translate-y-[6px] rotate-45" : ""
        }`}
      />
      <span
        className={`absolute left-0 top-[7px] h-0.5 rounded-full bg-current transition-all duration-200 ${
          open ? "w-0 opacity-0" : "w-3.5 opacity-100"
        }`}
      />
      <span
        className={`absolute left-0 top-[13px] h-0.5 w-5 rounded-full bg-current transition-transform duration-200 ${
          open ? "-translate-y-[6px] -rotate-45" : ""
        }`}
      />
    </span>
  );
}

export function SiteHeaderClient({
  websiteUrl,
  homeUrl,
  pricingUrl,
  affiliateUrl,
  docsUrl,
  guideUrl,
  startFreeUrl,
  memberHubUrl,
  goProUrl,
  labels,
  languageLabel,
  locale,
  languageLinks,
}: SiteHeaderProps) {
  const [open, setOpen] = useState(false);

  const closeMenu = () => setOpen(false);

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

        <button
          type="button"
          onClick={() => setOpen((value) => !value)}
          className="inline-flex h-10 w-10 items-center justify-center rounded-full border border-zinc-200 bg-zinc-50 text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100 dark:hover:bg-zinc-800 md:hidden"
          aria-expanded={open}
          aria-controls="mobile-site-menu"
          aria-label={open ? "Close menu" : "Open menu"}
        >
          <MenuIcon open={open} />
        </button>

        <nav className="hidden items-center gap-6 text-sm md:flex">
          <a href={homeUrl} className={navLinkClass}>
            {labels.home}
          </a>
          <a href={pricingUrl} className={navLinkClass}>
            {labels.pricing}
          </a>
          <a href={affiliateUrl} className={navLinkClass}>
            {labels.affiliate}
          </a>
          <a href={docsUrl} className={navLinkClass}>
            {labels.docs}
          </a>
          <a href={guideUrl} className={navLinkClass}>
            {labels.guide}
          </a>
          <a href={startFreeUrl} className={navLinkClass}>
            {labels.startFree}
          </a>
          <a href={memberHubUrl} className={memberHubClass}>
            {labels.memberHub}
          </a>
          <a href={goProUrl} className={goProClass}>
            {labels.goPro}
          </a>
          <LanguageSwitcher locale={locale} label={languageLabel} options={languageLinks} />
        </nav>
      </div>

      <div className="relative md:hidden">
        <div
          className={`fixed inset-0 top-14 z-40 bg-zinc-950/35 transition-opacity duration-200 ${
            open ? "opacity-100" : "pointer-events-none opacity-0"
          }`}
          aria-hidden
          onClick={closeMenu}
        />
        <div
          id="mobile-site-menu"
          className={`absolute inset-x-0 top-full z-50 border-b border-zinc-200 bg-white/96 shadow-2xl backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/96 ${
            open ? "pointer-events-auto translate-y-0 opacity-100" : "pointer-events-none -translate-y-2 opacity-0"
          } transition-all duration-200`}
        >
          <div className="mx-auto max-w-6xl px-4 py-4 sm:px-6">
            <nav className="flex flex-col gap-2">
              <a href={homeUrl} className={`${mobileItemClass} ${mobileNeutralTextClass}`} onClick={closeMenu}>
                <span>{labels.home}</span>
              </a>
              <a href={pricingUrl} className={`${mobileItemClass} ${mobileNeutralTextClass}`} onClick={closeMenu}>
                <span>{labels.pricing}</span>
              </a>
              <a href={affiliateUrl} className={`${mobileItemClass} ${mobileNeutralTextClass}`} onClick={closeMenu}>
                <span>{labels.affiliate}</span>
              </a>
              <a href={docsUrl} className={`${mobileItemClass} ${mobileNeutralTextClass}`} onClick={closeMenu}>
                <span>{labels.docs}</span>
              </a>
              <a href={guideUrl} className={`${mobileItemClass} ${mobileNeutralTextClass}`} onClick={closeMenu}>
                <span>{labels.guide}</span>
              </a>
              <a href={startFreeUrl} className={`${mobileItemClass} ${mobileNeutralTextClass}`} onClick={closeMenu}>
                <span>{labels.startFree}</span>
              </a>
              <a href={memberHubUrl} className={`${mobileItemClass} ${memberHubClass}`} onClick={closeMenu}>
                <span>{labels.memberHub}</span>
              </a>
              <a href={goProUrl} className={`${mobileItemClass} ${goProClass}`} onClick={closeMenu}>
                <span>{labels.goPro}</span>
              </a>
              <div className={`${mobileItemClass} ${mobileNeutralTextClass}`}>
                <span>{languageLabel}</span>
                <LanguageSwitcher locale={locale} label={languageLabel} options={languageLinks} />
              </div>
            </nav>
          </div>
        </div>
      </div>
    </header>
  );
}

