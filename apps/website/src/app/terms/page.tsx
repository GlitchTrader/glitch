import Link from "next/link";

export const metadata = {
  title: "Terms — Glitch",
  description: "Terms of service and subscription terms.",
};

export default function TermsPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-24">
        <h1 className="text-3xl font-bold tracking-tight">Terms of Service</h1>
        <p className="mt-6 text-zinc-600 dark:text-zinc-400">
          [Full terms placeholder. Include: subscription terms, billing and
          cancel policy, refund/14-day guarantee details, affiliate commission
          term length, acceptable use, no performance guarantees.]
        </p>
        <p className="mt-10">
          <Link href="/" className="text-glitch-teal hover:underline">Back to Home</Link>
        </p>
      </div>
    </div>
  );
}
