import Image from "next/image";
import { CheckList } from "@/components/check-list";
import { CoreCtas } from "@/components/core-ctas";
import { ExternalLink } from "@/components/external-link";
import { FaqList } from "@/components/faq-list";
import { HeroScreenshotsCarousel } from "@/components/hero-screenshots-carousel";
import { PricingCards } from "@/components/pricing-cards";
import { SiteFooter } from "@/components/site-footer";
import { marketingLinks } from "@/lib/marketing-links";
import { freeTierFeatures, paidAccessFeatures } from "@/lib/pricing";
import { buildPageMetadata } from "@/lib/seo";

export const metadata = buildPageMetadata({
  title: "Glitch - Prop Trading Assistant for NinjaTrader",
  description:
    "Glitch helps prop traders protect accounts, scale replication, and trade with Glitch Score, compliance controls, and performance insight.",
  path: "/",
});

const faqItems = [
  {
    question: "What exactly is Glitch?",
    answer:
      "Glitch is a risk-first trading assistant for NinjaTrader. It centralizes compliance, replication, analytics, and performance review in one operating layer.",
  },
  {
    question: "Is Glitch an auto-trading bot?",
    answer:
      "No. You control strategy and execution. Glitch helps you enforce risk and improve decisions before avoidable mistakes become account damage.",
  },
  {
    question: "Can I use my current indicators and strategies?",
    answer:
      "Yes. Keep your existing indicators, automated strategies, and bots. Glitch is designed to complement your workflow, not replace it.",
  },
  {
    question: "Does Glitch work across prop firm models?",
    answer:
      "Yes. Glitch is built for cross-prop workflows with preloaded firm-rule frameworks and configurable compliance behavior.",
  },
  {
    question: "Can Glitch handle high account counts?",
    answer:
      "Paid access supports up to 10 masters/groups and up to 100 followers per group, designed for serious multi-account operations.",
  },
  {
    question: "What is Glitch Score?",
    answer:
      "Glitch Score is Glitch's composite signal layer that consolidates multi-timeframe context so you can read conditions faster and with more structure.",
  },
  {
    question: "How do the paid plans work?",
    answer:
      "Choose Monthly / Annual for flexible billing, or Lifetime access for one payment. After checkout, Member Hub handles download, updates, and activation.",
  },
  {
    question: "Is there a marketplace coming?",
    answer:
      "Yes. The roadmap includes a marketplace for third-party indicators, strategies, and partner prop firm offers.",
  },
  {
    question: "Does Glitch guarantee profits?",
    answer:
      "No. Glitch improves process quality and risk discipline. Trading outcomes still depend on strategy quality and execution discipline.",
  },
];

export default function Home() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <section aria-label="Glitch banner">
        <Image
          src="/images/Glitch Banner 4-1 .jpg"
          alt="Glitch trading assistant for NinjaTrader prop traders"
          width={3438}
          height={860}
          priority
          className="h-auto w-full"
          sizes="100vw"
        />
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 pb-0 pt-12 sm:px-6 sm:pt-24">
          <p className="inline-flex rounded-full border border-glitch-teal/40 bg-glitch-teal/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.12em] text-glitch-teal">
            Prop Trading Assistant for NinjaTrader
          </p>
          <h1 className="mt-4 max-w-4xl text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            The Risk-First Operating System for Prop Traders
          </h1>
          <p className="mt-6 max-w-3xl text-lg text-zinc-600 dark:text-zinc-400">
            Glitch helps you protect accounts, scale replication, and execute with context. If your process is serious,
            your tooling should be too.
          </p>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            From single-account evaluations to high-account-count operations, Glitch unifies compliance enforcement,
            replication, analytics, and performance intelligence in one assistant layer.
          </p>
          <div className="mt-5 flex flex-wrap gap-2 text-xs font-medium uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
            <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">Glitch Score</span>
            <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">Compliance Layer</span>
            <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">Replication Control</span>
            <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">Real-Time Analysis</span>
            <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">Performance + Insights</span>
          </div>
          <CoreCtas className="mt-10" />
        </div>
        <div className="mt-14 pb-14 sm:mt-16 sm:pb-24">
          <HeroScreenshotsCarousel />
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-5xl px-4 py-12 sm:px-6 sm:py-20">
          <div className="flex flex-col gap-8 md:flex-row md:items-end md:gap-12">
            <div className="w-full md:flex-1">
              <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Why accounts fail, even with good strategy</h2>
              <p className="mt-4 text-zinc-600 dark:text-zinc-400">
                Most losses are not from lacking ideas. They come from avoidable operational errors: wrong account,
                wrong size, rule drift, replication mismatch, or no context when volatility spikes.
              </p>
              <p className="mt-4 text-zinc-600 dark:text-zinc-400">
                Glitch was built to solve that layer first. Think of it as your mission-control assistant for disciplined
                prop trading workflows.
              </p>
            </div>
            <div className="-mb-12 ml-auto h-auto w-[200px] sm:-mb-20 sm:w-[260px] md:ml-0">
              <div className="overflow-hidden rounded-2xl">
                <Image
                  src="/images/character/idea.png"
                  alt="Glitch trading assistant illustration"
                  width={1200}
                  height={1200}
                  className="h-auto w-full object-cover"
                />
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Glitch helps you grow faster</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            Start with protection. Upgrade to paid access when you want scale, deeper signal context, and
            higher-account-count operations.
          </p>

          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <div id="free-tier" className="rounded-2xl border-2 border-zinc-200 bg-white p-6 sm:p-8 dark:border-zinc-700 dark:bg-zinc-900">
              <p className="text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">Start Free</p>
              <h3 className="mt-2 text-xl font-semibold">Core protection, no credit card pressure</h3>
              <div className="mt-5">
                <CheckList items={freeTierFeatures} />
              </div>
              <ExternalLink
                href={marketingLinks.freeAccessUrl}
                className="mt-6 inline-flex h-11 items-center justify-center rounded-full border border-zinc-300 px-5 text-sm font-medium text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-800"
              >
                Start Free
              </ExternalLink>
            </div>

            <div id="go-pro" className="rounded-2xl border-2 border-glitch-teal bg-glitch-teal/5 p-6 sm:p-8 dark:bg-glitch-teal/10">
              <p className="text-xs font-semibold uppercase tracking-wide text-glitch-teal">Go Pro</p>
              <h3 className="mt-2 text-xl font-semibold">Scale, context, and control for serious operators</h3>
              <div className="mt-5">
                <CheckList items={paidAccessFeatures} />
              </div>
              <div className="mt-6 flex flex-wrap gap-2 text-xs font-semibold uppercase tracking-wide text-zinc-600 dark:text-zinc-400">
                <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">$95 / month</span>
                <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">$995 / year</span>
                <span className="rounded-full border border-zinc-200 px-3 py-1 dark:border-zinc-700">$2,450 one time</span>
              </div>
              <p className="mt-3 text-sm text-zinc-600 dark:text-zinc-400">
                Choose monthly, annual, or lifetime at checkout.
              </p>
              <ExternalLink
                href={marketingLinks.goProCheckoutUrl}
                className="mt-5 inline-flex h-11 items-center justify-center rounded-full bg-glitch-orange px-5 text-sm font-medium text-white hover:opacity-90"
              >
                Go Pro
              </ExternalLink>
            </div>
          </div>
        </div>
      </section>

      <section id="pricing" className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Pricing that respects how traders actually buy</h2>
          <p className="mt-3 max-w-3xl text-zinc-600 dark:text-zinc-400">
            Free for proof. Flexible billing when you want premium power. Lifetime access when Glitch becomes part of
            the business.
          </p>
          <PricingCards className="mt-10" />
          <p className="mt-6 text-sm text-zinc-600 dark:text-zinc-400">
            Already joined?{" "}
            <ExternalLink href={marketingLinks.memberHubUrl} className="font-medium text-glitch-teal hover:underline">
              Member Hub
            </ExternalLink>{" "}
            is where downloads, updates, and activation steps live.
          </p>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Setup in minutes, improve for years</h2>
          <div className="mt-8 grid gap-8 md:grid-cols-2">
            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">Activation flow</h3>
              <ol className="mt-4 list-inside list-decimal space-y-2 text-zinc-600 dark:text-zinc-400">
                <li>Choose Free, Monthly / Annual, or Lifetime access.</li>
                <li>Open Member Hub and follow Start Here.</li>
                <li>Download and install the latest Glitch build.</li>
                <li>Open New &gt; Glitch in NinjaTrader.</li>
                <li>Paste your key in Settings and click Validate License.</li>
              </ol>
            </div>
            <div className="rounded-2xl border border-zinc-200 p-6 dark:border-zinc-800">
              <h3 className="font-semibold">Daily operating cadence</h3>
              <ol className="mt-4 list-inside list-decimal space-y-2 text-zinc-600 dark:text-zinc-400">
                <li>Review risk status, warning count, and account posture.</li>
                <li>Validate replication and compliance before session open.</li>
                <li>Read Glitch Score and macro context before execution.</li>
                <li>Journal outcomes and review metrics after the close.</li>
                <li>Iterate process weekly. Professionals review, amateurs react.</li>
              </ol>
            </div>
          </div>
        </div>
      </section>

      <section className="border-b border-zinc-200 dark:border-zinc-800">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-20">
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">FAQ</h2>
          <p className="mt-2 max-w-3xl text-zinc-600 dark:text-zinc-400">
            Professional answers for traders who care about performance and process.
          </p>
          <div className="mt-6">
            <FaqList items={faqItems} />
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  );
}
