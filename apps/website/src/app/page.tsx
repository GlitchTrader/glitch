import Link from "next/link";

export default function Home() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      {/* 1. Hero — problem + outcome + CTA */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-24">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            The Risk-First NinjaTrader AddOn Built for Prop Traders
          </h1>
          <p className="mt-6 max-w-2xl text-lg text-zinc-600 dark:text-zinc-400">
            Glitch helps you reduce preventable rule breaches, control replication
            risk, and execute with cleaner multi-timeframe context.
          </p>
          <ul className="mt-8 space-y-2 text-zinc-700 dark:text-zinc-300">
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Compliance-aware account controls
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Replication caps + lock logic
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Analytics + macro context in one workspace
            </li>
          </ul>
          <div className="mt-10 flex flex-wrap gap-4">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white transition-colors hover:opacity-90"
            >
              Start at $95/mo
            </Link>
            <Link
              href="/pricing?plan=annual"
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal bg-transparent px-6 font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get Annual ($995/yr)
            </Link>
          </div>
        </div>
      </section>

      {/* 2. Why traders fail evals/funded — pain framing */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Why traders fail evals and funded accounts
          </h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Most blowouts come from preventable mistakes: rule violations,
            over-sizing, tilt, or poor replication discipline. Glitch is built to
            put a guardrail around your account before the next mistake costs
            you a payout or a reset.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            We don’t promise profits—we help you reduce avoidable risk and
            improve decision quality so you can focus on execution instead of
            wondering if you’re about to breach.
          </p>
        </div>
      </section>

      {/* 3. How Glitch works — 3-step mechanism */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            How Glitch works
          </h2>
          <p className="mt-2 text-lg text-glitch-teal font-medium">
            Most tools chase entries. Glitch protects the account first.
          </p>
          <ol className="mt-10 grid gap-10 sm:grid-cols-3">
            <li>
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-glitch-teal/20 text-glitch-teal font-bold">
                1
              </span>
              <h3 className="mt-4 font-semibold">Compliance layer</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Firm-aware limits, lock logic, and risk buffers so you stay
                within rules before a breach happens.
              </p>
            </li>
            <li>
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-glitch-teal/20 text-glitch-teal font-bold">
                2
              </span>
              <h3 className="mt-4 font-semibold">Replication control</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Master/follower sync, contract scaling, and protective stops so
                funded and eval accounts follow your plan.
              </p>
            </li>
            <li>
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-glitch-teal/20 text-glitch-teal font-bold">
                3
              </span>
              <h3 className="mt-4 font-semibold">Execution context</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Multi-timeframe analytics and macro/headlines in one workspace so
                you trade with clearer signals, not guesswork.
              </p>
            </li>
          </ol>
        </div>
      </section>

      {/* 4. Product proof blocks — compliance, replication, analytics, journal */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Built for real prop workflows
          </h2>
          <p className="mt-2 text-zinc-600 dark:text-zinc-400">
            Eval, PA/funded, and strategy-assisted execution.
          </p>
          <div className="mt-10 grid gap-8 sm:grid-cols-2">
            <div className="rounded-xl border border-zinc-200 bg-zinc-50/50 p-6 dark:border-zinc-800 dark:bg-zinc-900/50">
              <h3 className="font-semibold text-glitch-teal">Compliance</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Risk compliance engine and lock controls so you avoid
                preventable rule breaches.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 bg-zinc-50/50 p-6 dark:border-zinc-800 dark:bg-zinc-900/50">
              <h3 className="font-semibold text-glitch-teal">Replication</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Replication control layer with caps and sync so funded and eval
                accounts stay in line.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 bg-zinc-50/50 p-6 dark:border-zinc-800 dark:bg-zinc-900/50">
              <h3 className="font-semibold text-glitch-teal">Analytics</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Analytics command center: multi-timeframe signals and regime
                context in one place.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 bg-zinc-50/50 p-6 dark:border-zinc-800 dark:bg-zinc-900/50">
              <h3 className="font-semibold text-glitch-teal">Journal</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Journal and warning ledger visibility so you see what happened
                and fix misconfigs fast.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* 5. Pricing snapshot */}
      <section id="pricing" className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Simple pricing
          </h2>
          <p className="mt-2 text-zinc-600 dark:text-zinc-400">
            One membership. Full access. Cancel anytime.
          </p>
          <div className="mt-10 flex flex-wrap items-center gap-8">
            <div className="rounded-2xl border-2 border-zinc-200 bg-white p-8 dark:border-zinc-700 dark:bg-zinc-900">
              <p className="text-sm font-medium text-zinc-500 dark:text-zinc-400">
                Monthly
              </p>
              <p className="mt-2 text-3xl font-bold">$95<span className="text-lg font-normal text-zinc-500">/mo</span></p>
              <Link
                href="/pricing"
                className="mt-6 inline-block rounded-full bg-glitch-orange px-6 py-3 text-center font-medium text-white hover:opacity-90"
              >
                Start at $95/mo
              </Link>
            </div>
            <div className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-8 dark:bg-glitch-teal/10">
              <p className="text-sm font-medium text-glitch-teal">Best value</p>
              <p className="mt-2 text-3xl font-bold">$995<span className="text-lg font-normal text-zinc-500 dark:text-zinc-400">/yr</span></p>
              <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
                Save $145/yr vs monthly
              </p>
              <Link
                href="/pricing?plan=annual"
                className="mt-6 inline-block rounded-full border-2 border-glitch-teal bg-transparent px-6 py-3 text-center font-medium text-glitch-teal hover:bg-glitch-teal/20"
              >
                Get Annual ($995/yr)
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* 6. Social proof / testimonials — placeholder */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Built for traders who take risk seriously
          </h2>
          <div className="mt-10 grid gap-8 sm:grid-cols-2">
            <blockquote className="rounded-xl border border-zinc-200 bg-zinc-50/50 p-6 dark:border-zinc-800 dark:bg-zinc-900/50">
              <p className="text-zinc-700 dark:text-zinc-300">
                [Testimonial placeholder — Eval grinder / funded protector /
                semi-automated operator quote]
              </p>
              <footer className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
                — Trader, [firm type]
              </footer>
            </blockquote>
            <blockquote className="rounded-xl border border-zinc-200 bg-zinc-50/50 p-6 dark:border-zinc-800 dark:bg-zinc-900/50">
              <p className="text-zinc-700 dark:text-zinc-300">
                [Testimonial placeholder — outcome-focused, no hype]
              </p>
              <footer className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">
                — Trader, [firm type]
              </footer>
            </blockquote>
          </div>
        </div>
      </section>

      {/* 7. Final CTA */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Stop failing from preventable violations
          </h2>
          <p className="mt-4 text-lg text-zinc-600 dark:text-zinc-400">
            Trade with a risk guardrail before your next mistake costs your
            account.
          </p>
          <div className="mt-10 flex flex-wrap gap-4">
            <Link
              href="/offer"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white transition-colors hover:opacity-90"
            >
              See the full offer
            </Link>
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal bg-transparent px-6 font-medium text-glitch-teal transition-colors hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Go to pricing
            </Link>
          </div>
        </div>
      </section>

      <footer className="border-t border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-8 sm:px-6">
          <div className="flex flex-wrap items-center justify-between gap-4 text-sm text-zinc-500 dark:text-zinc-400">
            <span>© Glitch. Risk-first prop trading OS for NinjaTrader.</span>
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
