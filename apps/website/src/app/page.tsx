import Link from "next/link";
import Image from "next/image";
import { marketingCopy, marketingLinks } from "@/lib/marketing-links";
import { HeroScreenshotsCarousel } from "@/components/hero-screenshots-carousel";

export default function Home() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section aria-label="Glitch banner">
        <Image
          src="/images/Glitch Banner 4-1 .jpg"
          alt="Glitch banner"
          width={3438}
          height={860}
          priority
          className="h-auto w-full"
          sizes="100vw"
        />
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 pb-0 pt-16 sm:px-6 sm:pt-24">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            The Risk-First {marketingCopy.productName} Built for Prop Traders
          </h1>
          <p className="mt-6 max-w-2xl text-lg text-zinc-600 dark:text-zinc-400">
            Glitch helps you reduce preventable rule breaches, control replication risk, and execute with cleaner
            multi-timeframe context.
          </p>
        </div>
        <div className="mx-auto max-w-4xl px-4 pb-0 pt-8 sm:px-6">
          <ul className="mt-8 space-y-2 text-zinc-700 dark:text-zinc-300">
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Compliance-aware account controls
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Replication caps and lock logic
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Analytics and macro context in one workspace
            </li>
          </ul>
          <div className="mt-10 flex flex-wrap gap-4">
            <Link
              href={marketingLinks.monthlyCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white transition-colors hover:opacity-90"
            >
              Start at {marketingCopy.monthlyPriceLabel}
            </Link>
            <Link
              href={marketingLinks.annualCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal bg-transparent px-6 font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get Annual ({marketingCopy.annualPriceLabel})
            </Link>
            <Link
              href={marketingLinks.memberHubUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border border-zinc-300 px-6 font-medium text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900"
            >
              Already a member? Open Member Hub
            </Link>
          </div>
        </div>
        <div className="mt-16 pb-16 sm:pb-24">
          <HeroScreenshotsCarousel />
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Why traders fail evals and funded accounts</h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Most blowouts come from preventable mistakes: rule violations, over-sizing, tilt, or poor replication
            discipline. Glitch is built to put a guardrail around your account before the next mistake costs a payout
            or a reset.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            We do not promise profits. We help you reduce avoidable risk and improve decision quality so you can focus
            on execution instead of wondering if you are about to breach.
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">How Glitch works</h2>
          <p className="mt-2 text-lg font-medium text-glitch-teal">Most tools chase entries. Glitch protects the account first.</p>
          <ol className="mt-10 grid gap-10 sm:grid-cols-3">
            <li>
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-glitch-teal/20 font-bold text-glitch-teal">
                1
              </span>
              <h3 className="mt-4 font-semibold">Compliance layer</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Firm-aware limits, lock logic, and risk buffers so you stay within rules before a breach happens.
              </p>
            </li>
            <li>
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-glitch-teal/20 font-bold text-glitch-teal">
                2
              </span>
              <h3 className="mt-4 font-semibold">Replication control</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Master/follower sync, contract scaling, and protective stops so funded and eval accounts follow your
                plan.
              </p>
            </li>
            <li>
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-glitch-teal/20 font-bold text-glitch-teal">
                3
              </span>
              <h3 className="mt-4 font-semibold">Execution context</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Multi-timeframe analytics and macro headlines in one workspace so you trade with clearer signals.
              </p>
            </li>
          </ol>
        </div>
      </section>

      <section id="pricing" className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Simple pricing</h2>
          <p className="mt-2 text-zinc-600 dark:text-zinc-400">One membership. Full access. Cancel anytime.</p>
          <div className="mt-10 flex flex-wrap items-center gap-8">
            <div className="rounded-2xl border-2 border-zinc-200 bg-white p-8 dark:border-zinc-700 dark:bg-zinc-900">
              <p className="text-sm font-medium text-zinc-500 dark:text-zinc-400">Monthly</p>
              <p className="mt-2 text-3xl font-bold">
                $95<span className="text-lg font-normal text-zinc-500">/mo</span>
              </p>
              <Link
                href={marketingLinks.monthlyCheckoutUrl}
                className="mt-6 inline-block rounded-full bg-glitch-orange px-6 py-3 text-center font-medium text-white hover:opacity-90"
              >
                Start at {marketingCopy.monthlyPriceLabel}
              </Link>
            </div>
            <div className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-8 dark:bg-glitch-teal/10">
              <p className="text-sm font-medium text-glitch-teal">Best value</p>
              <p className="mt-2 text-3xl font-bold">
                $995<span className="text-lg font-normal text-zinc-500 dark:text-zinc-400">/yr</span>
              </p>
              <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">Save $145/yr vs monthly</p>
              <Link
                href={marketingLinks.annualCheckoutUrl}
                className="mt-6 inline-block rounded-full border-2 border-glitch-teal bg-transparent px-6 py-3 text-center font-medium text-glitch-teal hover:bg-glitch-teal/20"
              >
                Get Annual ({marketingCopy.annualPriceLabel})
              </Link>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Stop failing from preventable violations</h2>
          <p className="mt-4 text-lg text-zinc-600 dark:text-zinc-400">
            Trade with a risk guardrail before your next mistake costs your account.
          </p>
          <div className="mt-10 flex flex-wrap gap-4">
            <Link
              href={marketingLinks.monthlyCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white transition-colors hover:opacity-90"
            >
              Start at {marketingCopy.monthlyPriceLabel}
            </Link>
            <Link
              href={marketingLinks.annualCheckoutUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal bg-transparent px-6 font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get Annual ({marketingCopy.annualPriceLabel})
            </Link>
            <Link
              href={marketingLinks.memberHubUrl}
              className="inline-flex h-12 items-center justify-center rounded-full border border-zinc-300 px-6 font-medium text-zinc-700 transition-colors hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900"
            >
              Member Hub
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
