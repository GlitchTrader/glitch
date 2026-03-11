import Link from "next/link";
import { SiteFooter } from "@/components/site-footer";
import { buildPageMetadata } from "@/lib/seo";

export const metadata = buildPageMetadata({
  title: "Risk Disclosure - Glitch",
  description:
    "Important trading risk disclosure for futures, options, NinjaTrader workflows, and prop firm trading environments.",
  path: "/risk-disclosure",
});

const LAST_UPDATED = "2026-03-11";

export default function RiskDisclosurePage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-4xl px-4 py-12 sm:px-6 sm:py-24">
        <p className="text-sm text-zinc-500 dark:text-zinc-400">Last updated: {LAST_UPDATED}</p>
        <h1 className="mt-2 text-3xl font-bold tracking-tight">Risk Disclosure</h1>
        <p className="mt-6 text-zinc-600 dark:text-zinc-400">
          Trading futures, options, and prop firm accounts involves substantial risk of loss. Losses can exceed your
          expectations and may occur quickly in volatile market conditions. Read this disclosure carefully. By using
          Glitch, you acknowledge that you understand and accept these risks.
        </p>

        <div className="mt-8 space-y-6">
          <section className="rounded-xl border border-amber-200 bg-amber-50/50 p-6 dark:border-amber-900/50 dark:bg-amber-950/20">
            <h2 className="font-semibold text-amber-900 dark:text-amber-200">Trading is risky. You are responsible.</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Glitch is a risk and execution support platform for NinjaTrader. It does not guarantee profits, prevent
              losses, or promise any specific trading outcomes. <strong>You trade at your own risk and responsibility.</strong>{" "}
              Performance varies by trader, strategy, market regime, and discipline. Past performance is not indicative
              of future results. Future results do not guarantee future performance. Do your own research (DYOR).
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Trading and market risks</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Futures and options trading carries substantial risk; you can lose more than your initial investment.</li>
              <li>Leverage can magnify both gains and losses.</li>
              <li>Markets can move rapidly; execution may occur at worse prices than expected (slippage).</li>
              <li>Only risk capital—money you can afford to lose—should be used for trading.</li>
              <li>Trading may not be suitable for everyone; assess your experience, objectives, and financial situation.</li>
            </ul>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Software and technology risks</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              Glitch software is provided &quot;as is.&quot; We do not guarantee uninterrupted operation, accuracy of
              data, or correct behavior under all conditions. You accept that:
            </p>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Software can fail, lag, or disconnect due to bugs, network issues, or third-party services.</li>
              <li>Automations and controls may not trigger as intended in all market or system conditions.</li>
              <li>Data feeds, APIs, or broker connectivity can be delayed, incomplete, or unavailable.</li>
              <li>We are not responsible for any losses or missed trades due to technical failures—within or beyond our control.</li>
            </ul>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Proprietary trading firms (prop firms)</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              If you use Glitch with a prop firm or funded account, you are solely responsible for ensuring your use
              complies with that firm&apos;s current rules and terms. Prop firm rules change; we strive to keep our
              product updated but may miss an update or inadvertently break a rule. We are not responsible for
              disqualification, account termination, funding revocation, or clawbacks. Always check your prop
              firm&apos;s latest rules and disclosures before trading.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">No advice</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              This content and the Glitch product are for informational and operational support only. They do not
              constitute legal, investment, tax, or financial advice. We do not recommend any securities, strategies,
              or trading decisions. You are solely responsible for your trading and risk decisions.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Hypothetical or simulated results</h2>
            <p className="mt-4 text-zinc-600 dark:text-zinc-400">
              If we or any third party present hypothetical or simulated performance results, such results have inherent
              limitations and do not represent actual trading. They may not account for slippage, liquidity, or
              execution reality. No representation is made that any account will achieve profits or losses similar to
              those shown. Past performance is not necessarily indicative of future results.
            </p>
          </section>

          <section className="rounded-xl border border-zinc-200 p-6 dark:border-zinc-800">
            <h2 className="font-semibold">Important reminders</h2>
            <ul className="mt-4 list-inside list-disc space-y-2 text-zinc-600 dark:text-zinc-400">
              <li>Past performance is not indicative of future results.</li>
              <li>Automations and controls can fail under certain conditions.</li>
              <li>You remain responsible for all trading and risk decisions.</li>
              <li>We will do our best to keep disclosures and product behavior updated and comprehensive, but we may omit an update or introduce errors; you are solely responsible for your use and compliance.</li>
            </ul>
          </section>
        </div>

        <p className="mt-10 text-sm text-zinc-500 dark:text-zinc-400">
          These disclosures are a summary. Full terms, including limitation of liability and indemnification, are in our{" "}
          <Link href="/terms" className="text-glitch-teal hover:underline">
            Terms of Service
          </Link>
          . See also our{" "}
          <Link href="/privacy" className="text-glitch-teal hover:underline">
            Privacy Policy
          </Link>
          .
        </p>
        <p className="mt-6">
          <Link href="/" className="text-glitch-teal hover:underline">
            Back to Home
          </Link>
        </p>
      </div>

      <SiteFooter />
    </div>
  );
}
