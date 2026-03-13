"use client";

import Image from "next/image";
import { usePathname } from "next/navigation";
import { useEffect, useEffectEvent, useState } from "react";
import { Link } from "@/i18n/navigation";
import { ExternalLink } from "@/components/external-link";
import { LanguageSwitcher } from "@/components/language-switcher";

type SiteHeaderLabels = {
  home: string;
  offer: string;
  pricing: string;
  affiliate: string;
  docs: string;
  memberHub: string;
  goPro: string;
  ariaHome: string;
  languageLabel: string;
};

type SiteHeaderLinks = {
  docsUrl: string;
  memberHubUrl: string;
  goProCheckoutUrl: string;
};

type SiteHeaderProps = {
  labels: SiteHeaderLabels;
  links: SiteHeaderLinks;
};

const navLinkClass = "text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100";
const memberHubClass = "font-medium text-glitch-teal hover:text-glitch-teal/80";
const goProClass = "font-medium text-glitch-orange hover:text-glitch-orange/80";
const mobileItemClass =
  "flex items-center justify-between rounded-2xl border border-zinc-200/80 bg-zinc-50/85 px-4 py-3 text-sm text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-800 dark:bg-zinc-900/90 dark:text-zinc-100 dark:hover:bg-zinc-800";

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

export function SiteHeader({ labels, links }: SiteHeaderProps) {
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const closeMenuOnNavigation = useEffectEvent(() => {
    setOpen(false);
  });

  useEffect(() => {
    closeMenuOnNavigation();
  }, [pathname]);

  return (
    <header className="sticky top-0 z-50 border-b border-zinc-200 bg-white/95 backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/95">
      <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
        <Link href="/" aria-label={labels.ariaHome} className="flex items-center">
          <Image
            src="/images/branding/Glitch%20Logo.svg"
            alt="Glitch"
            width={90}
            height={24}
            className="h-6 w-auto"
            unoptimized
            priority
          />
        </Link>

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
          <Link href="/" className={navLinkClass}>
            {labels.home}
          </Link>
          <Link href="/offer" className={navLinkClass}>
            {labels.offer}
          </Link>
          <Link href="/pricing" className={navLinkClass}>
            {labels.pricing}
          </Link>
          <Link href="/affiliate" className={navLinkClass}>
            {labels.affiliate}
          </Link>
          <ExternalLink href={links.docsUrl} className={navLinkClass}>
            {labels.docs}
          </ExternalLink>
          <ExternalLink href={links.memberHubUrl} className={memberHubClass}>
            {labels.memberHub}
          </ExternalLink>
          <ExternalLink href={links.goProCheckoutUrl} className={goProClass}>
            {labels.goPro}
          </ExternalLink>
          <LanguageSwitcher />
        </nav>
      </div>

      <div className="relative md:hidden">
        <div
          className={`fixed inset-0 top-14 z-40 bg-zinc-950/35 transition-opacity duration-200 ${
            open ? "opacity-100" : "pointer-events-none opacity-0"
          }`}
          aria-hidden
          onClick={() => setOpen(false)}
        />
        <div
          id="mobile-site-menu"
          className={`absolute inset-x-0 top-full z-50 border-b border-zinc-200 bg-white/96 shadow-2xl backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/96 ${
            open ? "pointer-events-auto translate-y-0 opacity-100" : "pointer-events-none -translate-y-2 opacity-0"
          } transition-all duration-200`}
        >
          <div className="mx-auto max-w-6xl px-4 py-4 sm:px-6">
            <nav className="flex flex-col gap-2">
              <Link href="/" className={mobileItemClass} onClick={() => setOpen(false)}>
                <span>{labels.home}</span>
              </Link>
              <Link href="/offer" className={mobileItemClass} onClick={() => setOpen(false)}>
                <span>{labels.offer}</span>
              </Link>
              <Link href="/pricing" className={mobileItemClass} onClick={() => setOpen(false)}>
                <span>{labels.pricing}</span>
              </Link>
              <Link href="/affiliate" className={mobileItemClass} onClick={() => setOpen(false)}>
                <span>{labels.affiliate}</span>
              </Link>
              <ExternalLink href={links.docsUrl} className={mobileItemClass} onClick={() => setOpen(false)}>
                <span>{labels.docs}</span>
              </ExternalLink>
              <ExternalLink href={links.memberHubUrl} className={`${mobileItemClass} ${memberHubClass}`} onClick={() => setOpen(false)}>
                <span>{labels.memberHub}</span>
              </ExternalLink>
              <ExternalLink href={links.goProCheckoutUrl} className={`${mobileItemClass} ${goProClass}`} onClick={() => setOpen(false)}>
                <span>{labels.goPro}</span>
              </ExternalLink>
              <div className="mt-1 flex items-center justify-between rounded-2xl border border-zinc-200/80 bg-zinc-50/85 px-4 py-3 dark:border-zinc-800 dark:bg-zinc-900/90">
                <span className="text-sm text-zinc-600 dark:text-zinc-300">{labels.languageLabel}</span>
                <LanguageSwitcher />
              </div>
            </nav>
          </div>
        </div>
      </div>
    </header>
  );
}
