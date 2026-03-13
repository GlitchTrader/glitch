import { redirect } from "next/navigation";
import { routing } from "@/i18n/routing";

/**
 * Root path: redirect to default locale. Middleware normally redirects / to the detected locale;
 * this handles cases where middleware does not run (e.g. some static hosts).
 */
export default function RootPage() {
  redirect(`/${routing.defaultLocale}`);
}
