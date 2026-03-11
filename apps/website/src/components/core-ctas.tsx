import Link from "next/link";
import { marketingLinks } from "@/lib/marketing-links";

type CoreCtasProps = {
  className?: string;
  centered?: boolean;
  compact?: boolean;
};

export function CoreCtas({ className, centered = false, compact = false }: CoreCtasProps) {
  const buttonHeight = compact ? "h-11" : "h-12";
  const rowClass = `glitch-cta-row ${centered ? "justify-center" : ""} ${className ?? ""}`.trim();

  return (
    <div className={rowClass}>
      <Link
        href={marketingLinks.freeAccessUrl}
        className={`inline-flex ${buttonHeight} items-center justify-center rounded-full border border-zinc-300 px-6 font-medium text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900`}
      >
        Start Free
      </Link>
      <Link
        href={marketingLinks.goProCheckoutUrl}
        className={`inline-flex ${buttonHeight} items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white transition-colors hover:opacity-90`}
      >
        Go Pro
      </Link>
      <Link
        href={marketingLinks.memberHubUrl}
        className={`inline-flex ${buttonHeight} items-center justify-center rounded-full border-2 border-glitch-teal bg-transparent px-6 font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20`}
      >
        Member Hub
      </Link>
    </div>
  );
}
