import Link from "next/link";
import { CheckList } from "@/components/check-list";
import { ExternalLink } from "@/components/external-link";
import { getPricingContent, type PricingPlan, type PricingPlanTone } from "@/lib/pricing";

type PricingCardsProps = {
  className?: string;
  useAnchors?: boolean;
  plans?: PricingPlan[];
  featureLabel?: string;
};

const baseCardClass =
  "relative flex h-full flex-col overflow-hidden rounded-[2rem] border p-6 shadow-[0_20px_70px_rgba(15,23,42,0.06)] transition-colors sm:p-8 dark:shadow-[0_24px_80px_rgba(0,0,0,0.28)]";

const cardToneClass: Record<PricingPlanTone, string> = {
  neutral:
    "border-zinc-200 bg-white hover:border-zinc-300 dark:border-zinc-800 dark:bg-zinc-900/90 dark:hover:border-zinc-700",
  featured:
    "border-glitch-teal/70 bg-[linear-gradient(180deg,rgba(6,24,22,0.98)_0%,rgba(5,18,16,0.98)_100%)] text-zinc-100 hover:border-glitch-teal",
  premium:
    "border-zinc-200 bg-[linear-gradient(180deg,rgba(255,255,255,0.98)_0%,rgba(248,248,248,0.98)_100%)] hover:border-zinc-300 dark:border-zinc-800 dark:bg-[linear-gradient(180deg,rgba(26,26,30,0.98)_0%,rgba(17,17,20,0.98)_100%)] dark:hover:border-zinc-700",
};

const eyebrowToneClass: Record<PricingPlanTone, string> = {
  neutral: "text-zinc-500 dark:text-zinc-400",
  featured: "text-glitch-teal",
  premium: "text-zinc-500 dark:text-zinc-400",
};

const bodyToneClass: Record<PricingPlanTone, string> = {
  neutral: "text-zinc-600 dark:text-zinc-400",
  featured: "text-zinc-300",
  premium: "text-zinc-600 dark:text-zinc-400",
};

const dividerToneClass: Record<PricingPlanTone, string> = {
  neutral: "border-zinc-200 dark:border-zinc-800",
  featured: "border-white/10",
  premium: "border-zinc-200 dark:border-zinc-800",
};

const buttonToneClass: Record<PricingPlanTone, string> = {
  neutral:
    "border border-zinc-300 text-zinc-800 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-800",
  featured: "bg-glitch-orange text-white hover:opacity-90",
  premium:
    "border-2 border-glitch-teal text-glitch-teal hover:bg-glitch-teal/10 dark:hover:bg-glitch-teal/20",
};

const defaultPricingContent = getPricingContent("en");

export function PricingCards({ className, useAnchors = false, plans, featureLabel }: PricingCardsProps) {
  const resolvedPlans = plans ?? defaultPricingContent.pricingPlans;
  const resolvedFeatureLabel = featureLabel ?? defaultPricingContent.featureLabel;

  return (
    <div className={`grid gap-6 lg:grid-cols-3 ${className ?? ""}`.trim()}>
      {resolvedPlans.map((plan) => {
        const isExternalCta = plan.ctaHref.startsWith("http");
        const buttonClassName =
          `mt-6 inline-flex h-12 w-full items-center justify-center rounded-full px-6 text-center font-medium transition-colors ${buttonToneClass[plan.tone]}`;

        return (
          <article
            key={plan.id}
            id={useAnchors ? plan.id : undefined}
            className={`${baseCardClass} ${cardToneClass[plan.tone]}`}
          >
            <div
              aria-hidden="true"
              className={
                plan.tone === "featured"
                  ? "pointer-events-none absolute inset-x-0 top-0 h-32 bg-[radial-gradient(circle_at_top,rgba(26,188,156,0.18),transparent_72%)]"
                  : plan.tone === "premium"
                    ? "pointer-events-none absolute right-0 top-0 h-28 w-28 bg-[radial-gradient(circle_at_center,rgba(255,66,0,0.12),transparent_72%)]"
                    : "pointer-events-none absolute left-0 top-0 h-24 w-24 bg-[radial-gradient(circle_at_center,rgba(255,255,255,0.04),transparent_72%)]"
              }
            />

            <div className="relative">
              <div className="flex items-start justify-between gap-4">
                <p className={`text-xs font-semibold uppercase tracking-[0.18em] ${eyebrowToneClass[plan.tone]}`}>
                  {plan.eyebrow}
                </p>
                {plan.badge ? (
                  <span className="relative -top-[5px] -mb-[7px] inline-flex shrink-0 whitespace-nowrap rounded-full border border-glitch-orange/40 bg-glitch-orange/10 px-3 py-1 text-xs font-semibold text-glitch-orange">
                    {plan.badge}
                  </span>
                ) : null}
              </div>

              <h3 className="mt-4 max-w-xs text-[1.75rem] font-semibold tracking-tight sm:text-[2rem]">{plan.title}</h3>

              <p className={`mt-4 max-w-sm text-sm leading-6 ${bodyToneClass[plan.tone]}`}>{plan.description}</p>

              <div className={`mt-8 border-t pt-6 ${dividerToneClass[plan.tone]}`}>
                <div className="flex items-end gap-2">
                  <span className="text-4xl font-semibold tracking-tight sm:text-5xl">{plan.price}</span>
                  <span className={`pb-1 text-sm font-medium ${bodyToneClass[plan.tone]}`}>{plan.priceSuffix}</span>
                </div>
                <p className={`mt-3 text-sm ${bodyToneClass[plan.tone]}`}>{plan.secondaryPrice}</p>
              </div>

              <div className="mt-8">
                <p className={`text-xs font-semibold uppercase tracking-[0.18em] ${bodyToneClass[plan.tone]}`}>
                  {resolvedFeatureLabel}
                </p>
                <CheckList
                  className="mt-4"
                  items={plan.features}
                  itemClassName={plan.tone === "featured" ? "text-zinc-100" : undefined}
                />
              </div>
            </div>

            <div className="relative mt-auto pt-8">
              <p className={`text-sm leading-6 ${bodyToneClass[plan.tone]}`}>{plan.note}</p>
              {isExternalCta ? (
                <ExternalLink href={plan.ctaHref} className={buttonClassName}>
                  {plan.ctaLabel}
                </ExternalLink>
              ) : (
                <Link href={plan.ctaHref} className={buttonClassName}>
                  {plan.ctaLabel}
                </Link>
              )}
            </div>
          </article>
        );
      })}
    </div>
  );
}
