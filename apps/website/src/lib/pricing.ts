import { marketingLinks } from "@/lib/marketing-links";

export type PricingPlanTone = "neutral" | "featured" | "premium";

export type PricingPlan = {
  id: string;
  eyebrow: string;
  title: string;
  description: string;
  price: string;
  priceSuffix: string;
  secondaryPrice: string;
  note: string;
  ctaLabel: string;
  ctaHref: string;
  tone: PricingPlanTone;
  features: string[];
  badge?: string;
};

export const freeTierFeatures = [
  "Manual and automated replication",
  "Compliance enforcement",
  "1 master group with up to 2 followers",
  "Risk control indicators",
  "Preloaded prop firm rules",
];

export const paidAccessFeatures = [
  "Up to 10 groups / masters",
  "Up to 100 followers per group",
  "GlitchScore with 1m, 5m, 15m, and 60m dials",
  "Journal, Metrics, and Insights performance engine",
  "Regime, technical, order-flow, fundamental, and macro context",
  "Nasdaq and Mag7 enriched data plus news sentiment overlays",
  "Bring your own indicators, strategies, and bots",
];

export const pricingPlans: PricingPlan[] = [
  {
    id: "free-tier",
    eyebrow: "Free",
    title: "Start with guardrails",
    description:
      "Protect accounts, validate the workflow, and get the core assistant layer running before you pay.",
    price: "$0",
    priceSuffix: "to start",
    secondaryPrice: "No credit card required",
    note: "Install quickly, learn the workflow, and upgrade only when the operation is ready for more scale.",
    ctaLabel: "Start Free",
    ctaHref: marketingLinks.freeAccessUrl,
    tone: "neutral",
    features: freeTierFeatures,
  },
  {
    id: "go-pro",
    eyebrow: "Monthly / Annual",
    title: "Flexible premium access",
    description:
      "Unlock the full premium stack with billing that fits active traders and growing account operations.",
    price: "$95",
    priceSuffix: "/ month",
    secondaryPrice: "or $995 / year",
    note: "Choose monthly or yearly in checkout. Same premium toolset either way.",
    ctaLabel: "Choose Monthly or Annual",
    ctaHref: marketingLinks.goProCheckoutUrl,
    tone: "featured",
    badge: "Most popular",
    features: [
      "Up to 10 groups / masters",
      "Up to 100 followers per group",
      "GlitchScore with 1m, 5m, 15m, and 60m dials",
      "Journal, Metrics, and Insights engine",
      "Nasdaq, Mag7, macro, and news sentiment context",
    ],
  },
  {
    id: "life-time-access",
    eyebrow: "Life time access",
    title: "Own the operating layer",
    description:
      "Get the same premium Glitch stack without recurring billing when you already know this is core infrastructure.",
    price: "$4,995",
    priceSuffix: "one time",
    secondaryPrice: "No recurring bill",
    note: "One payment. Full premium access. Best fit for traders building a long-term operating stack.",
    ctaLabel: "Get Life time access",
    ctaHref: marketingLinks.lifetimeCheckoutUrl,
    tone: "premium",
    features: [
      "Everything in Monthly / Annual",
      "Bring your own indicators, strategies, and bots",
      "Same premium compliance, replication, and analytics stack",
      "One payment instead of recurring billing",
      "Best economics for long-term operators",
    ],
  },
];
