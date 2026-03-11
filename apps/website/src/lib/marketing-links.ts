export const marketingCopy = {
  brandName: "Glitch",
  productName: "Glitch NinjaTrader AddOn",
  monthlyPriceLabel: "$95/mo",
  annualPriceLabel: "$995/yr",
};

function readPublicUrl(name: string, fallback: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    return fallback;
  }

  return value;
}

export const marketingLinks = {
  monthlyCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL", "/pricing"),
  annualCheckoutUrl: readPublicUrl("NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL", "/pricing?plan=annual"),
  memberHubUrl: readPublicUrl("NEXT_PUBLIC_WHOP_MEMBER_HUB_URL", "https://whop.com/joined/glitchtrader/"),
};
