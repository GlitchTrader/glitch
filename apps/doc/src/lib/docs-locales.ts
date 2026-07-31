export const docsLocales = ["en", "pt", "es", "zh", "fr", "ru"] as const;

export type DocsLocale = (typeof docsLocales)[number];

export const defaultDocsLocale: DocsLocale = "en";
export const installationGuideSlug = "installation-guide-troubleshooting";

type DocsUiCopy = {
  language: string;
  onThisPage: string;
  previous: string;
  next: string;
  overview: string;
  product: string;
  reference: string;
  documentationHome: string;
  documentationMenu: string;
  headerNav: {
    home: string;
    pricing: string;
    affiliate: string;
    download: string;
    guide: string;
    startFree: string;
    memberHub: string;
    goPro: string;
  };
  shellTitle: string;
  shellDescription: string;
  accessTitle: string;
  accessDescription: string;
  backToWebsite: string;
  footerTitle: string;
  footerDescription: string;
  risk: string;
  terms: string;
  privacy: string;
  docs: string;
};

export const docsLocaleDetails: Record<
  DocsLocale,
  { languageTag: string; label: string; fileSuffix: string; ui: DocsUiCopy }
> = {
  en: {
    languageTag: "en-US",
    label: "English",
    fileSuffix: "",
    ui: {
      language: "Language", onThisPage: "On this page", previous: "Previous", next: "Next",
      overview: "Overview", product: "Product", reference: "Reference", documentationHome: "Documentation Home",
      documentationMenu: "Documentation menu",
      headerNav: { home: "Home", pricing: "Pricing", affiliate: "Affiliate", download: "Download", guide: "Guide", startFree: "Start Free", memberHub: "Member Hub", goPro: "Go Pro" },
      shellTitle: "Glitch Docs", shellDescription: "Product documentation for the AddOn, bridge indicator, persistence, and internal contracts.",
      accessTitle: "Looking for pricing or onboarding?", accessDescription: "Pricing, product pages, and member actions stay on the main website.", backToWebsite: "Back to glitchtrader.com",
      footerTitle: "Need product access or account help?", footerDescription: "Use the same official links across Website, Docs, and Download.", risk: "Risk", terms: "Terms", privacy: "Privacy", docs: "Docs",
    },
  },
  pt: {
    languageTag: "pt-BR", label: "Português", fileSuffix: ".pt",
    ui: {
      language: "Idioma", onThisPage: "Nesta página", previous: "Anterior", next: "Próximo",
      overview: "Visão geral", product: "Produto", reference: "Referência", documentationHome: "Início da documentação", documentationMenu: "Menu da documentação",
      headerNav: { home: "Início", pricing: "Preços", affiliate: "Afiliados", download: "Download", guide: "Guia", startFree: "Começar grátis", memberHub: "Área de membros", goPro: "Virar Pro" },
      shellTitle: "Docs do Glitch", shellDescription: "Documentação do AddOn, indicador bridge, persistência e contratos internos.",
      accessTitle: "Procurando preços ou onboarding?", accessDescription: "Preços, páginas do produto e ações de membros ficam no site principal.", backToWebsite: "Voltar ao glitchtrader.com",
      footerTitle: "Precisa de acesso ou ajuda com a conta?", footerDescription: "Use os mesmos links oficiais no Site, Docs e Download.", risk: "Risco", terms: "Termos", privacy: "Privacidade", docs: "Docs",
    },
  },
  es: {
    languageTag: "es-ES", label: "Español", fileSuffix: ".es",
    ui: {
      language: "Idioma", onThisPage: "En esta página", previous: "Anterior", next: "Siguiente",
      overview: "Resumen", product: "Producto", reference: "Referencia", documentationHome: "Inicio de documentación", documentationMenu: "Menú de documentación",
      headerNav: { home: "Inicio", pricing: "Precios", affiliate: "Afiliados", download: "Descargar", guide: "Guía", startFree: "Empezar gratis", memberHub: "Área de miembros", goPro: "Hazte Pro" },
      shellTitle: "Docs de Glitch", shellDescription: "Documentación del AddOn, indicador bridge, persistencia y contratos internos.",
      accessTitle: "¿Buscas precios u onboarding?", accessDescription: "Precios, producto y acciones de miembros están en el sitio principal.", backToWebsite: "Volver a glitchtrader.com",
      footerTitle: "¿Necesitas acceso o ayuda con la cuenta?", footerDescription: "Usa los mismos enlaces oficiales en Sitio, Docs y Descargas.", risk: "Riesgo", terms: "Términos", privacy: "Privacidad", docs: "Docs",
    },
  },
  zh: {
    languageTag: "zh-CN", label: "中文", fileSuffix: ".zh",
    ui: {
      language: "语言", onThisPage: "本页内容", previous: "上一页", next: "下一页",
      overview: "概览", product: "产品", reference: "参考", documentationHome: "文档首页", documentationMenu: "文档菜单",
      headerNav: { home: "首页", pricing: "价格", affiliate: "合作伙伴", download: "下载", guide: "指南", startFree: "免费开始", memberHub: "会员中心", goPro: "升级 Pro" },
      shellTitle: "Glitch 文档", shellDescription: "AddOn、Bridge 指标、持久化和内部契约的产品文档。",
      accessTitle: "需要价格或入门信息？", accessDescription: "价格、产品页面和会员操作位于主站。", backToWebsite: "返回 glitchtrader.com",
      footerTitle: "需要产品访问或账户帮助？", footerDescription: "网站、文档和下载均使用相同的官方链接。", risk: "风险", terms: "条款", privacy: "隐私", docs: "文档",
    },
  },
  fr: {
    languageTag: "fr-FR", label: "Français", fileSuffix: ".fr",
    ui: {
      language: "Langue", onThisPage: "Sur cette page", previous: "Précédent", next: "Suivant",
      overview: "Vue d’ensemble", product: "Produit", reference: "Référence", documentationHome: "Accueil documentation", documentationMenu: "Menu documentation",
      headerNav: { home: "Accueil", pricing: "Tarifs", affiliate: "Affiliation", download: "Télécharger", guide: "Guide", startFree: "Commencer gratuitement", memberHub: "Espace membre", goPro: "Passer Pro" },
      shellTitle: "Docs Glitch", shellDescription: "Documentation de l’AddOn, de l’indicateur bridge, de la persistance et des contrats internes.",
      accessTitle: "Vous cherchez les tarifs ou l’onboarding ?", accessDescription: "Tarifs, pages produit et actions membre restent sur le site principal.", backToWebsite: "Retour à glitchtrader.com",
      footerTitle: "Besoin d’un accès ou d’aide ?", footerDescription: "Utilisez les mêmes liens officiels sur le Site, les Docs et le Téléchargement.", risk: "Risque", terms: "Conditions", privacy: "Confidentialité", docs: "Docs",
    },
  },
  ru: {
    languageTag: "ru-RU", label: "Русский", fileSuffix: ".ru",
    ui: {
      language: "Язык", onThisPage: "На этой странице", previous: "Назад", next: "Далее",
      overview: "Обзор", product: "Продукт", reference: "Справочник", documentationHome: "Главная документации", documentationMenu: "Меню документации",
      headerNav: { home: "Главная", pricing: "Цены", affiliate: "Партнёры", download: "Скачать", guide: "Инструкция", startFree: "Начать бесплатно", memberHub: "Кабинет", goPro: "Перейти на Pro" },
      shellTitle: "Документация Glitch", shellDescription: "Документация AddOn, bridge-индикатора, хранения данных и внутренних контрактов.",
      accessTitle: "Ищете цены или onboarding?", accessDescription: "Цены, страницы продукта и действия участника находятся на основном сайте.", backToWebsite: "Вернуться на glitchtrader.com",
      footerTitle: "Нужен доступ или помощь со счётом?", footerDescription: "Используйте единые официальные ссылки на сайте, в документации и загрузках.", risk: "Риск", terms: "Условия", privacy: "Конфиденциальность", docs: "Документы",
    },
  },
};

export function isDocsLocale(value: string): value is DocsLocale {
  return docsLocales.includes(value as DocsLocale);
}

export function getDocsHref(locale: DocsLocale, slug?: string | null): string {
  const path = slug ? `/${slug}` : "/";
  return locale === defaultDocsLocale ? path : `/${locale}${path === "/" ? "" : path}`;
}

export function getDocLanguages(slug?: string | null): Record<string, string> {
  return {
    ...Object.fromEntries(docsLocales.map((locale) => [docsLocaleDetails[locale].languageTag, getDocsHref(locale, slug)])),
    "x-default": getDocsHref(defaultDocsLocale, slug),
  };
}

export function getInstallationGuideHref(locale: DocsLocale): string {
  return getDocsHref(locale, installationGuideSlug);
}

export function getInstallationGuideLanguages(): Record<string, string> {
  return getDocLanguages(installationGuideSlug);
}
