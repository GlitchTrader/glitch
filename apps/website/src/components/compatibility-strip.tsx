import Image from "next/image";
import { getLocale } from "next-intl/server";
import { getUiContent } from "@/lib/localized-ui";

const compatibilityItems = [
  {
    name: "NinjaTrader",
    src: "/images/partner-logos/6650a8ebd9b327bfef4625e5_ninjatrader-full-logo-p-800.png",
    shellClassName: "h-5 w-28",
  },
  {
    name: "Apex Trader Funding",
    src: "/images/partner-logos/apex-logo-light.svg",
    shellClassName: "h-8 w-26",
  },
  {
    name: "Take Profit Trader",
    src: "/images/partner-logos/desktop-logo.svg",
    shellClassName: "h-5 w-26",
  },
  {
    name: "TradeDay",
    src: "/images/partner-logos/668d7fed23c5c4c88db20796_TD_Logo-02-p-2000.webp",
    shellClassName: "h-6 w-22",
  },
  {
    name: "Lucid Trading",
    src: "/images/partner-logos/Untitled-design-2025-10-22T181841.622.png.webp",
    shellClassName: "h-10 w-26",
  },
];

export async function CompatibilityStrip() {
  const locale = await getLocale();
  const ui = getUiContent(locale);
  return (
    <section className="border-y border-zinc-200/80 bg-zinc-950/80 dark:border-zinc-800">
      <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6">
        <div className="flex flex-col items-center gap-5 xl:flex-row xl:items-center xl:justify-between xl:gap-3">
          <div className="mx-auto max-w-3xl text-center xl:mx-0 xl:max-w-[360px] xl:text-left">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-glitch-teal">{ui.compatibility.eyebrow}</p>
            <p className="mt-2 text-sm text-zinc-300 xl:max-w-[340px]">
              {ui.compatibility.lead}
            </p>
          </div>
          <div className="mx-auto flex max-w-[920px] flex-wrap justify-center gap-2.5 xl:mx-0 xl:w-[680px] xl:max-w-none xl:flex-none xl:flex-nowrap xl:justify-end">
            {compatibilityItems.map((item) => (
              <div
                key={item.name}
                className="flex h-16 w-[126px] shrink-0 items-center justify-center rounded-[0.95rem] border border-zinc-800 bg-zinc-900/75 px-3 py-2 sm:w-[142px]"
                title={item.name}
              >
                <div className={`relative ${item.shellClassName}`}>
                  <Image
                    src={item.src}
                    alt={`${item.name} logo`}
                    fill
                    sizes="140px"
                    className="object-contain"
                    unoptimized={item.src.endsWith(".svg")}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
