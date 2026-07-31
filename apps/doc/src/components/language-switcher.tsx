"use client";

import Image from "next/image";
import { useCallback, useState } from "react";

export type LanguageOption = {
  locale: string;
  label: string;
  languageTag: string;
  href: string;
};

const flagSrcByLocale: Record<string, string> = {
  en: "/images/flags/square/en.svg",
  pt: "/images/flags/square/br.svg",
  es: "/images/flags/square/es.svg",
  zh: "/images/flags/square/zh.svg",
  fr: "/images/flags/square/fr.svg",
  ru: "/images/flags/square/ru.svg",
};

function Flag({ locale }: { locale: string }) {
  return (
    <span className="inline-flex h-5 w-5 shrink-0 overflow-hidden rounded-full border border-zinc-300/70 bg-zinc-100 dark:border-zinc-600 dark:bg-zinc-900">
      <Image src={flagSrcByLocale[locale] ?? flagSrcByLocale.en} alt="" width={20} height={20} className="h-full w-full object-cover" unoptimized />
    </span>
  );
}

export function LanguageSwitcher({ locale, label, options }: { locale: string; label: string; options: LanguageOption[] }) {
  const [open, setOpen] = useState(false);
  const current = options.find((option) => option.locale === locale) ?? options[0];

  const handleSelect = useCallback((option: LanguageOption) => {
    if (option.locale === locale) {
      setOpen(false);
      return;
    }

    const target = new URL(option.href, window.location.origin);
    const nextHref = `${target.pathname}${window.location.search}${window.location.hash}`;
    window.location.assign(nextHref);
  }, [locale]);

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        className="flex h-9 items-center gap-2 rounded-md border border-zinc-200 bg-transparent px-2.5 py-1.5 text-sm text-zinc-700 transition-colors hover:bg-zinc-100/70 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-900/70"
        aria-expanded={open}
        aria-haspopup="listbox"
        aria-label={label}
      >
        <Flag locale={current.locale} />
        <span className="hidden sm:inline">{current.label}</span>
        <svg className="h-4 w-4 shrink-0 text-zinc-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden>
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {open ? (
        <>
          <div className="fixed inset-0 z-40" aria-hidden onClick={() => setOpen(false)} />
          <ul role="listbox" className="absolute right-0 top-full z-50 mt-1 min-w-[180px] rounded-lg border border-zinc-200 bg-zinc-800 py-1 shadow-lg dark:border-zinc-700">
            {options.map((option) => {
              const selected = option.locale === locale;
              return (
                <li key={option.locale} role="option" aria-selected={selected}>
                  <button
                    type="button"
                    onClick={() => handleSelect(option)}
                    className={`flex w-full items-center gap-3 px-3 py-2.5 text-left text-sm text-zinc-100 transition-colors hover:bg-zinc-700 ${selected ? "bg-glitch-teal text-white hover:bg-glitch-teal/90" : ""}`}
                    lang={option.languageTag}
                  >
                    <Flag locale={option.locale} />
                    <span>{option.label}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      ) : null}
    </div>
  );
}
