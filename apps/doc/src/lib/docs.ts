import fs from "node:fs";
import path from "node:path";
import { cache } from "react";
import {
  defaultDocsLocale,
  docsLocaleDetails,
  getDocsHref,
  installationGuideSlug,
  type DocsLocale,
} from "@/lib/docs-locales";

const DOCS_ROOT = path.resolve(process.cwd(), "../../ninjatrader/Glitch/Docs");

type DocDefinition = {
  slug: string;
  baseFileName: string;
  section: "product" | "reference";
};

const publicDocDefinitions: DocDefinition[] = [
  { slug: installationGuideSlug, baseFileName: installationGuideSlug, section: "product" },
  { slug: "architecture", baseFileName: "architecture", section: "product" },
  { slug: "addon", baseFileName: "addon", section: "product" },
  { slug: "indicator", baseFileName: "indicator", section: "product" },
  { slug: "data-flow-and-bridge", baseFileName: "data-flow-and-bridge", section: "product" },
  { slug: "persistence", baseFileName: "persistence", section: "product" },
  { slug: "api-reference", baseFileName: "api-reference", section: "reference" },
];

const summaryCopy: Record<DocsLocale, Record<string, string>> = {
  en: {
    [installationGuideSlug]: "Install or upgrade Standard and Experimental AI editions without mixing packages or losing local state.",
    architecture: "See the Standard AddOn, bridge indicator, replication boundary, and separate data paths as they exist in v0.0.2.0.",
    addon: "Understand the four-tab operating window, Chart Trader widget, execution-driven replication, risk controls, and licensing.",
    indicator: "Review the GlitchAnalyticsBridge timeframes, public parameters, publishing contract, freshness, and recovery behavior.",
    "data-flow-and-bridge": "Follow analytics, shell actions, and native executions through separate, auditable runtime paths.",
    persistence: "Learn what Glitch stores in GlitchData, what remains native NinjaTrader truth, and how to back up or migrate safely.",
    "api-reference": "Reference the principal internal C# contracts in Standard Glitch; this is not a public trading API.",
  },
  pt: {
    [installationGuideSlug]: "Instale ou atualize Standard e AI Experimental sem misturar pacotes nem perder o estado local.",
    architecture: "Veja o AddOn Standard, o indicador bridge, o limite de replicação e os fluxos separados do v0.0.2.0.",
    addon: "Entenda a janela de quatro abas, Chart Trader, replicação por execução, controles de risco e licença.",
    indicator: "Revise timeframes, parâmetros, publicação, freshness e recuperação do GlitchAnalyticsBridge.",
    "data-flow-and-bridge": "Acompanhe analytics, ações da interface e execuções nativas em fluxos separados e auditáveis.",
    persistence: "Saiba o que fica em GlitchData, o que continua sendo verdade nativa do NinjaTrader e como migrar com segurança.",
    "api-reference": "Consulte os principais contratos C# internos do Standard; não é uma API pública de trading.",
  },
  es: {
    [installationGuideSlug]: "Instala o actualiza Standard y AI Experimental sin mezclar paquetes ni perder estado local.",
    architecture: "Conoce el AddOn Standard, el indicador bridge, el límite de replicación y los flujos separados de v0.0.2.0.",
    addon: "Entiende la ventana de cuatro pestañas, Chart Trader, replicación por ejecución, riesgo y licencia.",
    indicator: "Revisa marcos, parámetros, publicación, frescura y recuperación de GlitchAnalyticsBridge.",
    "data-flow-and-bridge": "Sigue analytics, acciones de UI y ejecuciones nativas por rutas separadas y auditables.",
    persistence: "Aprende qué guarda GlitchData, qué sigue siendo verdad nativa y cómo migrar con seguridad.",
    "api-reference": "Consulta los contratos C# internos principales de Standard; no es una API pública de trading.",
  },
  zh: {
    [installationGuideSlug]: "安装或升级 Standard 与 Experimental AI，避免混装并保留本地状态。",
    architecture: "了解 v0.0.2.0 的 Standard AddOn、Bridge 指标、复制边界和独立数据路径。",
    addon: "了解四标签页主窗口、Chart Trader、成交驱动复制、风险控制和许可。",
    indicator: "查看 GlitchAnalyticsBridge 的周期、参数、发布、新鲜度和恢复行为。",
    "data-flow-and-bridge": "沿独立且可审计的路径追踪 analytics、界面动作和原生成交。",
    persistence: "了解 GlitchData 的内容、NinjaTrader 原生事实边界以及安全备份和迁移。",
    "api-reference": "参考 Standard 的主要内部 C# 契约；本页不是公开交易 API。",
  },
  fr: {
    [installationGuideSlug]: "Installez ou mettez à niveau Standard et AI Expérimentale sans mélanger les paquets ni perdre l’état local.",
    architecture: "Découvrez l’AddOn Standard, l’indicateur bridge, la limite de réplication et les flux séparés de v0.0.2.0.",
    addon: "Comprenez la fenêtre à quatre onglets, Chart Trader, la réplication par exécution, le risque et la licence.",
    indicator: "Consultez timeframes, paramètres, publication, fraîcheur et récupération de GlitchAnalyticsBridge.",
    "data-flow-and-bridge": "Suivez analytics, actions UI et exécutions natives dans des chemins séparés et auditables.",
    persistence: "Découvrez GlitchData, la vérité native NinjaTrader et les sauvegardes ou migrations sûres.",
    "api-reference": "Référence des principaux contrats C# internes de Standard ; ce n’est pas une API de trading publique.",
  },
  ru: {
    [installationGuideSlug]: "Установите или обновите Standard и Experimental AI без смешивания пакетов и потери локальных данных.",
    architecture: "Изучите Standard AddOn, bridge-индикатор, границу репликации и отдельные пути v0.0.2.0.",
    addon: "Изучите четыре вкладки, Chart Trader, репликацию по исполнениям, риск и лицензию.",
    indicator: "Параметры, таймфреймы, публикация, свежесть и восстановление GlitchAnalyticsBridge.",
    "data-flow-and-bridge": "Проследите analytics, действия UI и нативные исполнения по отдельным аудируемым путям.",
    persistence: "Что хранит GlitchData, что остаётся истиной NinjaTrader и как безопасно переносить данные.",
    "api-reference": "Справочник главных внутренних C# контрактов Standard; это не публичный торговый API.",
  },
};

const leadCopy: Record<DocsLocale, { intro: string; secondary: string }> = {
  en: { intro: "Code-grounded documentation for Standard Glitch v0.0.2.0 and its NinjaTrader runtime.", secondary: "Start with Architecture, then use the focused pages for the AddOn, indicator, data paths, persistence, and internal contracts." },
  pt: { intro: "Documentação baseada no código do Glitch Standard v0.0.2.0 e seu runtime NinjaTrader.", secondary: "Comece por Arquitetura e use as páginas específicas para AddOn, indicador, fluxos, persistência e contratos internos." },
  es: { intro: "Documentación basada en código de Glitch Standard v0.0.2.0 y su runtime NinjaTrader.", secondary: "Empieza por Arquitectura y usa las páginas específicas para AddOn, indicador, flujos, persistencia y contratos internos." },
  zh: { intro: "基于源代码的 Glitch Standard v0.0.2.0 与 NinjaTrader 运行时文档。", secondary: "先阅读架构，再按需查看 AddOn、指标、数据路径、持久化和内部契约。" },
  fr: { intro: "Documentation fondée sur le code de Glitch Standard v0.0.2.0 et de son runtime NinjaTrader.", secondary: "Commencez par Architecture, puis consultez les pages AddOn, indicateur, flux, persistance et contrats internes." },
  ru: { intro: "Документация Glitch Standard v0.0.2.0 и runtime NinjaTrader, основанная на исходном коде.", secondary: "Начните с архитектуры, затем используйте страницы AddOn, индикатора, потоков, хранения и внутренних контрактов." },
};

export type DocSummary = {
  slug: string;
  href: string;
  navTitle: string;
  title: string;
  section: string;
  sectionKey: DocDefinition["section"];
  summary: string;
};

export type DocHeading = { id: string; text: string; level: 2 | 3 };
export type DocPage = DocSummary & { content: string; spotlight: string; headings: DocHeading[] };
type DocNavigationSection = { title: string; items: Array<{ href: string; label: string; slug: string | null }> };

function readDocFile(definition: DocDefinition, locale: DocsLocale): string {
  const suffix = docsLocaleDetails[locale].fileSuffix;
  return fs.readFileSync(path.join(DOCS_ROOT, `${definition.baseFileName}${suffix}.md`), "utf8");
}

function stripMarkdown(value: string): string {
  return value.replace(/`([^`]+)`/g, "$1").replace(/\[([^\]]+)\]\(([^)]+)\)/g, "$1").replace(/[*_~>#-]/g, "").replace(/\|/g, " ").replace(/\s+/g, " ").trim();
}

function getHeading(markdown: string, fallback: string): string {
  const match = markdown.match(/^#\s+(.+)$/m);
  return match ? stripMarkdown(match[1]) : fallback;
}

function slugifyHeading(value: string): string {
  return stripMarkdown(value).toLowerCase().replace(/['".,()]/g, "").replace(/[^a-z0-9\u00C0-\u024F\u0400-\u04FF\u4E00-\u9FFF\s-]/g, "").trim().replace(/\s+/g, "-");
}

function getDocHeadings(markdown: string): DocHeading[] {
  return markdown.split(/\r?\n/).map((line) => {
    const match = /^(##|###)\s+(.+)$/.exec(line.trim());
    if (!match) return null;
    const text = stripMarkdown(match[2]);
    if (!text) return null;
    return { id: slugifyHeading(text), text, level: match[1] === "##" ? 2 : 3 } satisfies DocHeading;
  }).filter((heading): heading is DocHeading => Boolean(heading));
}

export const getDocsLead = cache((locale: DocsLocale = defaultDocsLocale) => leadCopy[locale]);

export const getDocSummaries = cache((locale: DocsLocale = defaultDocsLocale): DocSummary[] => {
  const ui = docsLocaleDetails[locale].ui;
  return publicDocDefinitions.map((definition) => {
    const markdown = readDocFile(definition, locale);
    const title = getHeading(markdown, definition.slug);
    return {
      slug: definition.slug,
      href: getDocsHref(locale, definition.slug),
      navTitle: title,
      title,
      section: definition.section === "product" ? ui.product : ui.reference,
      sectionKey: definition.section,
      summary: summaryCopy[locale][definition.slug],
    };
  });
});

export const getDocPage = cache((slug: string, locale: DocsLocale = defaultDocsLocale): DocPage | null => {
  const definition = publicDocDefinitions.find((item) => item.slug === slug);
  if (!definition) return null;
  const markdown = readDocFile(definition, locale);
  const summary = getDocSummaries(locale).find((item) => item.slug === slug);
  if (!summary) return null;
  return { ...summary, content: markdown, spotlight: summary.summary, headings: getDocHeadings(markdown) };
});

export function getDocNavigation(locale: DocsLocale = defaultDocsLocale): DocNavigationSection[] {
  const docs = getDocSummaries(locale);
  const ui = docsLocaleDetails[locale].ui;
  return [
    { title: ui.overview, items: [{ href: getDocsHref(locale), label: ui.documentationHome, slug: null }] },
    { title: ui.product, items: docs.filter((item) => item.sectionKey === "product").map((item) => ({ href: item.href, label: item.navTitle, slug: item.slug })) },
    { title: ui.reference, items: docs.filter((item) => item.sectionKey === "reference").map((item) => ({ href: item.href, label: item.navTitle, slug: item.slug })) },
  ];
}

export function getAdjacentDocs(slug: string, locale: DocsLocale = defaultDocsLocale) {
  const docs = getDocSummaries(locale);
  const index = docs.findIndex((item) => item.slug === slug);
  return { previous: index > 0 ? docs[index - 1] : null, next: index >= 0 && index < docs.length - 1 ? docs[index + 1] : null };
}

export function resolveMarkdownHref(href: string | undefined, locale: DocsLocale = defaultDocsLocale): string | null {
  if (!href) return null;
  if (href.startsWith("#") || /^(https?:)?\/\//i.test(href) || href.startsWith("mailto:")) return href;
  const [pathname, hash] = href.split("#");
  if (!pathname.endsWith(".md")) return href;
  if (pathname === "README.md") return hash ? `${getDocsHref(locale)}#${hash}` : getDocsHref(locale);
  const baseName = pathname.replace(/\.(pt|es|zh|fr|ru)\.md$/i, "").replace(/\.md$/i, "");
  const doc = publicDocDefinitions.find((item) => item.baseFileName === baseName);
  if (!doc) return null;
  const resolved = getDocsHref(locale, doc.slug);
  return hash ? `${resolved}#${hash}` : resolved;
}
