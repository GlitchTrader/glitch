export const marketingCopy = {
  brandName: "Glitch",
  productName: "Glitch NinjaTrader AddOn",
  freePriceLabel: "Free",
  monthlyPriceLabel: "$95/mo",
  annualPriceLabel: "$995/yr",
  lifetimePriceLabel: "$2,450 lifetime",
};

const whopUrls = {
  /** Glitch Lite — free tier checkout */
  freeLiteCheckoutUrl: "https://whop.com/checkout/plan_IROhfJAbF79K6",
  /** Glitch Pro — paid subscription checkout (monthly / annual on Whop) */
  goProCheckoutDefault: "https://whop.com/checkout/plan_G81vTccV19dNA",
  affiliateDashboardUrl:
    "https://whop.com/glitchtrader/affiliates/?affiliate_links:page=0&company_id=biz_In1cZIkY3QNdd9",
  memberHubUrl: "https://whop.com/joined/glitchtrader/",
};

const publicUrls = {
  docsUrl: "https://docs.glitchtrader.com",
};

function readPublicUrl(name: string, fallback: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    return fallback;
  }

  return value;
}

export const marketingLinks = {
  freeAccessUrl: readPublicUrl("NEXT_PUBLIC_WHOP_FREE_ACCESS_URL", whopUrls.freeLiteCheckoutUrl),
  goProCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL", whopUrls.goProCheckoutDefault),
  monthlyCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL", whopUrls.goProCheckoutDefault),
  annualCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL", whopUrls.goProCheckoutDefault),
  lifetimeCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_LIFETIME_URL", whopUrls.goProCheckoutDefault),
  affiliateDashboardUrl: readPublicUrl("NEXT_PUBLIC_WHOP_AFFILIATE_URL", whopUrls.affiliateDashboardUrl),
  memberHubUrl: readPublicUrl("NEXT_PUBLIC_WHOP_MEMBER_HUB_URL", whopUrls.memberHubUrl),
  docsUrl: readPublicUrl("NEXT_PUBLIC_DOCS_URL", publicUrls.docsUrl),
};
