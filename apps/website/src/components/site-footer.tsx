import Link from "next/link";
import { CoreCtas } from "@/components/core-ctas";

export function SiteFooter() {
  return (
    <footer className="border-t border-zinc-200 dark:border-zinc-800">
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <h2 className="text-lg font-semibold tracking-tight">Ready to trade with guardrails?</h2>
        <p className="mt-2 max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
          Start free. Upgrade to Go Pro when you want deeper analytics and larger replication scale.
        </p>
        <CoreCtas className="mt-6" />

        <div className="mt-8 flex flex-wrap items-center justify-between gap-4 border-t border-zinc-200 pt-6 text-sm text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
          <span>Copyright Glitch. Risk-first NinjaTrader AddOn for prop traders.</span>
          <div className="flex gap-6">
            <Link href="/risk-disclosure" className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Risk disclosure
            </Link>
            <Link href="/terms" className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Terms
            </Link>
            <Link href="/privacy" className="hover:text-zinc-700 dark:hover:text-zinc-300">
              Privacy
            </Link>
          </div>
        </div>
      </div>
    </footer>
  );
}
