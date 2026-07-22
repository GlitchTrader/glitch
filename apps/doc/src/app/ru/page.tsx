import type { Metadata } from "next";
import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";

export const metadata: Metadata = { title: docsLocaleDetails.ru.ui.shellTitle, description: docsLocaleDetails.ru.ui.shellDescription, alternates: { canonical: getDocsHref("ru"), languages: getDocLanguages() }, openGraph: { locale: "ru_RU" } };
export default function Page() { return <DocsHomeContent locale="ru" />; }
