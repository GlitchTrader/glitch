import Link from "next/link";
import { CheckList } from "@/components/check-list";
import { CoreCtas } from "@/components/core-ctas";
import { FaqList } from "@/components/faq-list";
import { PricingCards } from "@/components/pricing-cards";
import { SiteFooter } from "@/components/site-footer";
import { marketingLinks } from "@/lib/marketing-links";
import { buildPageMetadata } from "@/lib/seo";

const offerFreeHighlights = [
  "Manual + auto replication",
  "Compliance + firm rules",
  "1 master + 2 followers",
  "Risk control indicators",
  "Replicate + Flatten All",
  "Core assistant layer",
];

const offerPaidHighlights = [
  "10 groups + 100 followers each",
  "Glitch Score across 1m, 5m, 15m, and 60m",
  "Journal, Metrics + Insights",
  "Technical, macro + sentiment context",
  "Nasdaq + Mag7 enriched data",
  "Bring your own indicators + bots",
];

export const metadata = buildPageMetadata({
  title: "Glitch Offer - Risk-First Trading Assistant for NinjaTrader",
  description:
    "Explore the Glitch offer: compliance enforcement, replication control, Glitch Score analytics, and premium scaling for prop traders.",
  path: "/offer",
});

const faqItems = [
  {
    question: "Why call Glitch a trading assistant?",
    answer:
      "Because it assists every step of your operating process: risk controls, replication discipline, context analysis, and performance review.",
  },
  {
    question: "Does Glitch replace my strategy?",
    answer:
      "No. Your strategy remains yours. Glitch improves execution quality and risk discipline around that strategy.",
  },
  {
    question: "Can I run Glitch with automated systems?",
    answer:
      "Yes. Glitch is built to work alongside manual and automated workflows so you keep flexibility while enforcing guardrails.",
  },
  {
    question: "What makes paid access worth it?",
    answer:
      "Scale, depth, and speed: higher account limits, stronger context layers, and a serious feedback loop through Journal, Metrics, and Insights.",
  },
  {
    question: "How do monthly, yearly, and lifetime access work?",
    answer:
      "Monthly and yearly sit inside the flexible paid plan. Lifetime access is the one-payment option. Both unlock the same premium Glitch stack.",
  },
  {
    question: "Do you promise payouts or profits?",
    answer:
      "No. We promise professional-grade tooling for risk and execution discipline. Outcomes depend on the trader and market.",
  },
];

export default function OfferPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 text-center sm:px-6 sm:py-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            The Glitch Offer
          </p>
          <h1 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Built for traders who treat this like a business
          </h1>
          <p className="mx-auto mt-6 max-w-3xl text-lg text-zinc-600 dark:text-zinc-400">
            If you are done with avoidable rule breaks, replication chaos, and context-blind execution, this is the
            operating layer you were missing.
          </p>
          <p className="mx-auto mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            Glitch is defining a new category: the risk-first trading assistant for prop traders who run real
            operations, not toy setups.
          </p>
          <CoreCtas className="mt-10" centered />
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">The cost of preventable errors is brutal</h2>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            One broken process can reset an eval, pause a payout account, or burn confidence for weeks. Most of that is
            avoidable if the operating layer is strong enough.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Glitch is designed to make the right actions easier and the wrong actions harder. That is how durable
            trading operations are built.
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Start free. Upgrade when the business case is clear.</h2>
          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div id="free-tier" className="flex h-full flex-col rounded-2xl border-2 border-zinc-200 p-6 sm:p-8 dark:border-zinc-700">
              <p className="text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">Start Free</p>
              <h3 className="mt-2 text-xl font-semibold">Core account protection</h3>
              <div className="mt-4">
                <CheckList items={offerFreeHighlights} />
              </div>
              <div className="mt-8 border-t border-zinc-200 pt-5 dark:border-zinc-800">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400">Best for</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  Traders validating the workflow, building discipline, and protecting accounts before scaling.
                </p>
              </div>
            </div>
            <div id="go-pro" className="flex h-full flex-col rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-6 sm:p-8 dark:bg-glitch-teal/10">
              <p className="text-xs font-semibold uppercase tracking-wide text-glitch-teal">Go Pro</p>
              <h3 className="mt-2 text-xl font-semibold">Scale, depth, and precision</h3>
              <div className="mt-4">
                <CheckList items={offerPaidHighlights} />
              </div>
              <div className="mt-8 border-t border-glitch-teal/20 pt-5">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-glitch-teal">Best for</p>
                <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                  Traders running larger account stacks, deeper analysis, and faster review loops.
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">What makes Glitch tick</h2>
          <div className="mt-8 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Compliance enforcement</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Rule-aware operating logic to keep your accounts inside firm constraints before breaches happen.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Replication system</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Multi-account synchronization, follower scaling, and control surfaces designed for serious account stacks.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Glitch Score</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Structured directional context across multiple timeframes, reducing signal noise and emotional entries.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Journal + Insights engine</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Performance feedback loops that transform random outcomes into measurable process improvement.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Macro and sentiment stack</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Nasdaq, Mag7, macro and news layers in one view so context is no longer an afterthought.
              </p>
            </div>
            <div className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold text-glitch-teal">Open workflow philosophy</h3>
              <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
                Bring your own indicators, automation, and strategy stack. Glitch centralizes and hardens the operation.
              </p>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Pricing in one clean view</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            Free gets you started. Monthly / Annual gives you flexible premium access. Lifetime access is the permanent
            seat for traders who already know Glitch belongs in the stack.
          </p>
          <PricingCards className="mt-10" />
          <p className="mt-6 text-sm text-zinc-600 dark:text-zinc-400">
            Already joined?{" "}
            <Link href={marketingLinks.memberHubUrl} target="_blank" rel="noopener noreferrer" className="font-medium text-glitch-teal hover:underline">
              Member Hub
            </Link>{" "}
            is where your downloads, updates, and onboarding steps live.
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">FAQ</h2>
          <div className="mt-6">
            <FaqList items={faqItems} />
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  );
}
