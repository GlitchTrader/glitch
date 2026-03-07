import Link from "next/link";

export const metadata = {
  title: "Risk Disclosure — Glitch",
  description: "Trading and prop trading risk disclaimer. No performance guarantees.",
};

export default function RiskDisclosurePage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-24">
        <h1 className="text-3xl font-bold tracking-tight">Risk Disclosure</h1>
        <div className="prose prose-zinc mt-8 dark:prose-invert">
          <p className="text-zinc-600 dark:text-zinc-400">
            Trading futures, options, and prop firm accounts involves substantial
            risk of loss. Past performance is not indicative of future results.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Glitch does not guarantee profits, “infinite money,” or that you will
            never lose. Glitch is a risk and execution support tool for
            NinjaTrader. Results vary by user, market conditions, and discipline.
          </p>
          <p className="mt-4 text-zinc-600 dark:text-zinc-400">
            Always read your prop firm’s rules and risk disclosures. This page
            does not replace professional or legal advice.
          </p>
        </div>
        <p className="mt-10">
          <Link href="/" className="text-glitch-teal hover:underline">Back to Home</Link>
        </p>
      </div>
    </div>
  );
}
