import Link from "next/link";

export const metadata = {
  title: "Privacy — Glitch",
  description: "Privacy policy.",
};

export default function PrivacyPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-24">
        <h1 className="text-3xl font-bold tracking-tight">Privacy Policy</h1>
        <p className="mt-6 text-zinc-600 dark:text-zinc-400">
          [Full privacy policy placeholder. Include: data collected, how it’s
          used, cookies, payment processor, retention, and user rights.]
        </p>
        <p className="mt-10">
          <Link href="/" className="text-glitch-teal hover:underline">Back to Home</Link>
        </p>
      </div>
    </div>
  );
}
