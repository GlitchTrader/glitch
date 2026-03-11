export const marketingCopy = {
  brandName: "Glitch",
  productName: "Glitch NinjaTrader AddOn",
  freePriceLabel: "Free",
  monthlyPriceLabel: "$95/mo",
  annualPriceLabel: "$995/yr",
  lifetimePriceLabel: "$4,995 lifetime",
};

function readPublicUrl(name: string, fallback: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    return fallback;
  }

  return value;
}

export const marketingLinks = {
  freeAccessUrl: readPublicUrl("NEXT_PUBLIC_WHOP_FREE_ACCESS_URL", "/offer#free-tier"),
  goProCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL", "/pricing#go-pro"),
  monthlyCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL", "/pricing"),
  annualCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL", "/pricing?plan=annual"),
  lifetimeCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_LIFETIME_URL", "/pricing#life-time-access"),
  memberHubUrl: readPublicUrl("NEXT_PUBLIC_WHOP_MEMBER_HUB_URL", "https://whop.com/joined/glitchtrader/"),
};
