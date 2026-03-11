import Link from "next/link";
import { marketingCopy, marketingLinks } from "@/lib/marketing-links";

export const metadata = {
  title: "The Glitch Offer - Risk-First NinjaTrader AddOn",
  description:
    "Full offer: compliance engine, replication control, analytics, macro context, and journal visibility. $95/mo or $995/yr.",
};

export default function OfferPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6 sm:py-24">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Stop failing from preventable violations
          </h1>
          <p className="mt-6 text-lg text-zinc-600 dark:text-zinc-400">
            The risk-first {marketingCopy.productName} that helps prop traders protect eval and funded accounts with
            compliance controls, replication safety, and cleaner execution context.
          </p>
          <div className="mt-10 flex flex-wrap justify-center gap-4">
            <Link
              href={marketingLinks.monthlyCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Start at {marketingCopy.monthlyPriceLabel}
            </Link>
            <Link
              href={marketingLinks.annualCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal px-6 font-medium text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get Annual ({marketingCopy.annualPriceLabel}) - Save $145
            </Link>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">What one breach can cost you</h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            A single rule violation can reset an eval, lock a funded account, or kill a payout you have been building
            toward. Most of those breaches are preventable: over-sizing, wrong session, missed daily loss limits, or
            replication drift.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Glitch does not promise profits. It is built to help you stay within the rules and execution discipline you
            already know you need.
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">What you get</h2>
          <div className="mt-10 grid gap-8 sm:grid-cols-2">
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Compliance and lock controls</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Firm-aware limits, risk buffers, and lock logic so you stay inside rules before a breach happens.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Replication control layer</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Master/follower sync, contract scaling, and protective stops so funded and eval accounts stay aligned.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Analytics command center</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Multi-timeframe signal framework and regime context in one workspace.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Journal and warning ledger</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Visibility into what happened, where warnings fired, and what to fix fast.
              </p>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Choose your plan</h2>
          <div className="mt-10 flex flex-wrap justify-center gap-8">
            <div className="rounded-2xl border-2 border-zinc-200 p-8 dark:border-zinc-700">
              <p className="text-sm text-zinc-500 dark:text-zinc-400">Monthly</p>
              <p className="mt-2 text-3xl font-bold">
                $95<span className="text-lg font-normal text-zinc-500">/mo</span>
              </p>
              <Link
                href={marketingLinks.monthlyCheckoutUrl}
                className="mt-6 inline-block rounded-full bg-zinc-900 px-6 py-3 text-center font-medium text-white dark:bg-zinc-100 dark:text-zinc-900 hover:opacity-90"
              >
                Start at {marketingCopy.monthlyPriceLabel}
              </Link>
            </div>
            <div className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-8 dark:bg-glitch-teal/10">
              <p className="text-sm font-medium text-glitch-teal">Best value</p>
              <p className="mt-2 text-3xl font-bold">
                $995<span className="text-lg font-normal text-zinc-500 dark:text-zinc-400">/yr</span>
              </p>
              <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">Save $145/yr</p>
              <Link
                href={marketingLinks.annualCheckoutUrl}
                className="mt-6 inline-block rounded-full bg-glitch-teal px-6 py-3 text-center font-medium text-white hover:opacity-90"
              >
                Get Annual ({marketingCopy.annualPriceLabel})
              </Link>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Already a member? Go straight to setup and downloads
          </h2>
          <div className="mt-10 flex flex-wrap justify-center gap-4">
            <Link
              href={marketingLinks.memberHubUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal px-6 font-medium text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Open Member Hub
            </Link>
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full border border-zinc-300 px-6 font-medium text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900"
            >
              Compare plans
            </Link>
          </div>
        </div>
      </section>

      <footer className="border-t border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-8 sm:px-6">
          <div className="flex flex-wrap items-center justify-between gap-4 text-sm text-zinc-500 dark:text-zinc-400">
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
    </div>
  );
}