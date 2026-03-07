import Link from "next/link";

export const metadata = {
  title: "The Glitch Offer — Risk-First Prop Trading OS",
  description:
    "Full offer: compliance engine, replication control, analytics, macro context, journal. $95/mo or $995/yr. 14-day guarantee.",
};

export default function OfferPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      {/* 1. Big hook headline */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6 sm:py-24">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Stop failing from preventable violations
          </h1>
          <p className="mt-6 text-lg text-zinc-600 dark:text-zinc-400">
            The risk-first NinjaTrader addon that helps prop traders protect
            eval and funded accounts—with compliance controls, replication
            safety, and clearer execution context.
          </p>
          <div className="mt-10 flex flex-wrap justify-center gap-4">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Start at $95/mo
            </Link>
            <Link
              href="/pricing?plan=annual"
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal px-6 font-medium text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get Annual ($995/yr) — Save $145
            </Link>
          </div>
        </div>
      </section>

      {/* 2. Agitation — cost of breaches */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            What one breach can cost you
          </h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            A single rule violation can reset an eval, lock a funded account, or
            kill a payout you’ve been building toward. Most of those breaches
            are preventable: over-sizing, wrong session, missed daily loss limit,
            or replication drift. The cost isn’t just the fee—it’s the time,
            mental load, and the next run that might never feel the same.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Glitch doesn’t promise to make you profitable. It’s built to help you
            stay within the rules and execution discipline you already know you
            need—so the next mistake doesn’t have to be the one that costs the
            account.
          </p>
          <div className="mt-10 flex justify-center">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Protect my account
            </Link>
          </div>
        </div>
      </section>

      {/* 3. Mechanism — risk-first operating layer */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Most tools chase entries. Glitch protects the account first.
          </h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Glitch is a risk and execution operating layer that sits on top of
            your NinjaTrader workflow. It doesn’t replace your strategy—it
            adds firm-aware compliance, replication control, and multi-timeframe
            context so you can execute with guardrails instead of hoping you
            didn’t miss a rule.
          </p>
          <ul className="mt-8 space-y-3 text-zinc-700 dark:text-zinc-300">
            <li className="flex items-start gap-2">
              <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Compliance engine and lock controls to avoid preventable breaches
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Replication layer so eval/funded accounts follow your plan
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Analytics and macro context in one workspace
            </li>
            <li className="flex items-start gap-2">
              <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-glitch-teal" />
              Journal and warning ledger so you see what happened and fix it
            </li>
          </ul>
          <div className="mt-10 flex justify-center">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal px-6 font-medium text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get access
            </Link>
          </div>
        </div>
      </section>

      {/* 4. Feature-to-outcome mapping */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            What you get → what it does for you
          </h2>
          <div className="mt-10 space-y-8">
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Compliance + lock controls</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Firm-aware limits, risk buffers, and lock logic so you stay
                within rules before a breach. Less “did I blow it?”—more “I’m
                within guardrails.”
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Replication control</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Master/follower sync, contract scaling, protective stops. Keeps
                eval and funded accounts aligned with your plan so replication
                doesn’t become the reason you lose the account.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Analytics command center</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Multi-timeframe signal framework and regime context in one
                place. Clearer execution context instead of juggling charts and
                guesswork.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Macro + headlines + calendar</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Macro/Nasdaq context window so you have headlines and calendar
                where you’re already working—no tab chaos.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Journal + warning ledger</h3>
              <p className="mt-2 text-zinc-600 dark:text-zinc-400">
                Visibility into what happened and where warnings fired. Fix
                misconfigs and improve discipline with real data, not memory.
              </p>
            </div>
          </div>
          <div className="mt-10 flex justify-center">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Start at $95/mo
            </Link>
          </div>
        </div>
      </section>

      {/* 5. Value stack + bonuses */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            What it’s worth vs what you pay
          </h2>
          <div className="mt-10 space-y-3 text-zinc-700 dark:text-zinc-300">
            <div className="flex justify-between border-b border-zinc-200 py-2 dark:border-zinc-700">
              <span>Risk compliance engine + lock controls</span>
              <span className="font-medium">$1,500 value</span>
            </div>
            <div className="flex justify-between border-b border-zinc-200 py-2 dark:border-zinc-700">
              <span>Replication control layer</span>
              <span className="font-medium">$1,000 value</span>
            </div>
            <div className="flex justify-between border-b border-zinc-200 py-2 dark:border-zinc-700">
              <span>Analytics command center</span>
              <span className="font-medium">$1,200 value</span>
            </div>
            <div className="flex justify-between border-b border-zinc-200 py-2 dark:border-zinc-700">
              <span>Macro + headlines + calendar integration</span>
              <span className="font-medium">$600 value</span>
            </div>
            <div className="flex justify-between border-b border-zinc-200 py-2 dark:border-zinc-700">
              <span>Journal + warning ledger visibility</span>
              <span className="font-medium">$500 value</span>
            </div>
            <div className="flex justify-between border-b border-zinc-200 py-2 dark:border-zinc-700">
              <span>Ongoing updates + support</span>
              <span className="font-medium">$1,000+/yr</span>
            </div>
          </div>
          <p className="mt-6 text-right text-lg font-semibold">
            Total stated value: $5,800+
          </p>
          <div className="mt-8 rounded-xl border-2 border-glitch-teal bg-glitch-teal/5 p-6 dark:bg-glitch-teal/10">
            <p className="text-lg font-semibold text-glitch-teal">Your price today</p>
            <p className="mt-2 text-2xl font-bold">$95/month</p>
            <p className="mt-1 text-xl font-bold">$995/year (best value — save $145/yr)</p>
          </div>
          <div className="mt-8">
            <p className="font-medium text-zinc-700 dark:text-zinc-300">Bonuses (phase-released)</p>
            <ul className="mt-2 list-inside list-disc space-y-1 text-zinc-600 dark:text-zinc-400">
              <li>Prop Firm Survival Playbook (PDF)</li>
              <li>First 7-Day Setup Checklist (prevent common misconfigs)</li>
              <li>Creator Market Access Waitlist (indicator/strategy marketplace priority)</li>
            </ul>
          </div>
          <div className="mt-10 flex justify-center">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Get Annual ($995/yr)
            </Link>
          </div>
        </div>
      </section>

      {/* 6. Pricing + annual anchor — compact */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Choose your plan
          </h2>
          <div className="mt-10 flex flex-wrap justify-center gap-8">
            <div className="rounded-2xl border-2 border-zinc-200 p-8 dark:border-zinc-700">
              <p className="text-sm text-zinc-500 dark:text-zinc-400">Monthly</p>
              <p className="mt-2 text-3xl font-bold">$95<span className="text-lg font-normal text-zinc-500">/mo</span></p>
              <Link href="/pricing" className="mt-6 inline-block rounded-full bg-zinc-900 px-6 py-3 text-center font-medium text-white dark:bg-zinc-100 dark:text-zinc-900 hover:opacity-90">
                Start at $95/mo
              </Link>
            </div>
            <div className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-8 dark:bg-glitch-teal/10">
              <p className="text-sm font-medium text-glitch-teal">Best value</p>
              <p className="mt-2 text-3xl font-bold">$995<span className="text-lg font-normal text-zinc-500 dark:text-zinc-400">/yr</span></p>
              <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">Save $145/yr</p>
              <Link href="/pricing?plan=annual" className="mt-6 inline-block rounded-full bg-glitch-teal px-6 py-3 text-center font-medium text-white hover:opacity-90">
                Get Annual
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* 7. Guarantee */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            14-day implementation guarantee
          </h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            If you install Glitch, follow the onboarding checklist, and decide
            it’s not a fit—we’ll refund you. No drama. We’d rather you try it
            with a real safety net than wonder.
          </p>
          <div className="mt-10 flex justify-center">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal px-6 font-medium text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get started
            </Link>
          </div>
        </div>
      </section>

      {/* 8. FAQ + objections */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Common questions
          </h2>
          <dl className="mt-10 space-y-8">
            <div>
              <dt className="font-semibold text-zinc-900 dark:text-zinc-100">Is this a bot?</dt>
              <dd className="mt-2 text-zinc-600 dark:text-zinc-400">
                No. Glitch is a risk and execution operating layer for
                NinjaTrader workflows. It helps you stay within rules and
                replicate with control—it doesn’t trade for you.
              </dd>
            </div>
            <div>
              <dt className="font-semibold text-zinc-900 dark:text-zinc-100">Does this guarantee profits?</dt>
              <dd className="mt-2 text-zinc-600 dark:text-zinc-400">
                No. It helps reduce avoidable risk and improve decision quality.
                Results depend on you, your discipline, and market conditions.
              </dd>
            </div>
            <div>
              <dt className="font-semibold text-zinc-900 dark:text-zinc-100">Does it work for my firm?</dt>
              <dd className="mt-2 text-zinc-600 dark:text-zinc-400">
                Glitch supports major prop-firm rule models with ongoing
                updates. If your firm’s rules are common (daily loss, max
                contracts, drawdown, etc.), we likely have or can add support.
              </dd>
            </div>
            <div>
              <dt className="font-semibold text-zinc-900 dark:text-zinc-100">Can I cancel?</dt>
              <dd className="mt-2 text-zinc-600 dark:text-zinc-400">
                Yes. Clear subscription cancel policy—you can cancel anytime and
                keep access until the end of your billing period.
              </dd>
            </div>
            <div>
              <dt className="font-semibold text-zinc-900 dark:text-zinc-100">Do promo codes stack?</dt>
              <dd className="mt-2 text-zinc-600 dark:text-zinc-400">
                No. One promo per order. No stacking, no self-referrals.
              </dd>
            </div>
          </dl>
          <div className="mt-10 flex justify-center">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Start at $95/mo
            </Link>
          </div>
        </div>
      </section>

      {/* 9. Final CTA */}
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-3xl px-4 py-16 text-center sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Trade with a risk guardrail before your next mistake costs your account
          </h2>
          <div className="mt-10 flex flex-wrap justify-center gap-4">
            <Link
              href="/pricing"
              className="inline-flex h-12 items-center justify-center rounded-full bg-glitch-orange px-6 font-medium text-white hover:opacity-90"
            >
              Start at $95/mo
            </Link>
            <Link
              href="/pricing?plan=annual"
              className="inline-flex h-12 items-center justify-center rounded-full border-2 border-glitch-teal px-6 font-medium text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20"
            >
              Get Annual ($995/yr)
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
