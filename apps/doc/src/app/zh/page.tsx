import type { Metadata } from "next";
import { DocsHomeContent } from "@/components/docs-home-content";
import { docsLocaleDetails, getDocLanguages, getDocsHref } from "@/lib/docs-locales";

export const metadata: Metadata = { title: docsLocaleDetails.zh.ui.shellTitle, description: docsLocaleDetails.zh.ui.shellDescription, alternates: { canonical: getDocsHref("zh"), languages: getDocLanguages() }, openGraph: { locale: "zh_CN" } };
export default function Page() { return <DocsHomeContent locale="zh" />; }
