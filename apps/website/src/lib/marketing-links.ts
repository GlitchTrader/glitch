export const marketingCopy = {
  brandName: "Glitch",
  productName: "Glitch NinjaTrader AddOn",
  freePriceLabel: "Free",
  monthlyPriceLabel: "$95/mo",
  annualPriceLabel: "$995/yr",
  lifetimePriceLabel: "$2,450 lifetime",
};

const whopUrls = {
  freeLiteProductUrl: "https://whop.com/joined/glitchtrader/products/glitch-lite-free-access/",
  goProProductUrl: "https://whop.com/joined/glitchtrader/products/glitch-ninjatrader-addon/",
  affiliateDashboardUrl:
    "https://whop.com/glitchtrader/affiliates/?affiliate_links:page=0&company_id=biz_In1cZIkY3QNdd9",
  memberHubUrl: "https://whop.com/joined/glitchtrader/",
};

function readPublicUrl(name: string, fallback: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    return fallback;
  }

  return value;
}

export const marketingLinks = {
  freeAccessUrl: readPublicUrl("NEXT_PUBLIC_WHOP_FREE_ACCESS_URL", whopUrls.freeLiteProductUrl),
  goProCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL", whopUrls.goProProductUrl),
  monthlyCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL", whopUrls.goProProductUrl),
  annualCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL", whopUrls.goProProductUrl),
  lifetimeCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_LIFETIME_URL", whopUrls.goProProductUrl),
  affiliateDashboardUrl: readPublicUrl("NEXT_PUBLIC_WHOP_AFFILIATE_URL", whopUrls.affiliateDashboardUrl),
  memberHubUrl: readPublicUrl("NEXT_PUBLIC_WHOP_MEMBER_HUB_URL", whopUrls.memberHubUrl),
};
