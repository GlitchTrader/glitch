export const downloadLocales = ["en", "pt", "es", "zh", "fr", "ru"] as const;
export type DownloadLocale = (typeof downloadLocales)[number];
export const defaultDownloadLocale: DownloadLocale = "en";

type DownloadCopy = {
  languageTag: string; label: string; language: string;
  nav: { home: string; pricing: string; affiliate: string; docs: string; guide: string; startFree: string; memberHub: string; goPro: string };
  official: string; title: string; subtitle: string; intro: string;
  standardLatest: string; standardLatestNote: string; aiLatest: string; aiLatestNote: string;
  standard: string; ai: string; experimental: string; standardDescription: string; aiDescription: string;
  file: string; size: string; released: string; status: string; download: string; hermesSetup: string; installationGuide: string; sha: string;
  notPublished: string; versions: string; files: string; noRelease: string; standardHistory: string; aiHistory: string;
  standardInstall: string; importTitle: string; installSteps: string[];
  aiInstall: string; profileTitle: string; profileDescription: string; profileLink: string;
  needHelp: string; website: string; documentation: string; catalogUnavailable: string; copied: string;
  footerTitle: string; footerDescription: string; risk: string; terms: string; privacy: string;
};

export const downloadCopy: Record<DownloadLocale, DownloadCopy> = {
  en: {
    languageTag: "en-US", label: "English", language: "Language",
    nav: { home: "Home", pricing: "Pricing", affiliate: "Affiliate", docs: "Docs", guide: "Guide", startFree: "Start Free", memberHub: "Member Hub", goPro: "Go Pro" },
    official: "Official Downloads", title: "Download Glitch", subtitle: "for NinjaTrader 8", intro: "Standard remains the default update channel. Glitch AI is a separate Experimental package.",
    standardLatest: "Standard latest link", standardLatestNote: "The stable default used by existing Glitch installations.", aiLatest: "AI latest link", aiLatestNote: "Experimental AI channel; requires the Glitch Hermes profile.",
    standard: "Standard", ai: "Glitch AI", experimental: "Experimental", standardDescription: "Official package for manual trading, account management, analytics, and replication. It contains no Glitch AI tab or Hermes runtime.", aiDescription: "Experimental AI edition. AI Auto is off after fresh setup. Account selection comes from your configured Glitch group. No profitability, unattended-operation, or live-readiness claim is made.",
    file: "File", size: "Size", released: "Released", status: "Status", download: "Download", hermesSetup: "Hermes Setup", installationGuide: "Installation Guide", sha: "SHA-256", notPublished: "Not published yet", versions: "Versions", files: "files", noRelease: "No release has been published on this channel.", standardHistory: "Standard history", aiHistory: "AI history",
    standardInstall: "Standard Install", importTitle: "Import into NinjaTrader", installSteps: ["Download the Standard ZIP.", "NinjaTrader > Tools > Import > NinjaScript Add-On.", "Select the ZIP and restart NinjaTrader when prompted.", "Open Glitch and validate your license in Settings."],
    aiInstall: "AI Install", profileTitle: "Add the Hermes profile", profileDescription: "Import the AI ZIP, then follow the public profile instructions. Profile installation does not start trading or resume cron jobs.", profileLink: "Hermes profile instructions",
    needHelp: "Need Help?", website: "Website", documentation: "Documentation", catalogUnavailable: "Release catalog unavailable", copied: "Copied!",
    footerTitle: "Need product access or account help?", footerDescription: "Use the same official links across Website, Docs, and Download.", risk: "Risk", terms: "Terms", privacy: "Privacy",
  },
  pt: {
    languageTag: "pt-BR", label: "Português", language: "Idioma",
    nav: { home: "Início", pricing: "Preços", affiliate: "Afiliados", docs: "Docs", guide: "Guia", startFree: "Começar grátis", memberHub: "Área de membros", goPro: "Virar Pro" },
    official: "Downloads oficiais", title: "Baixe o Glitch", subtitle: "para NinjaTrader 8", intro: "Standard continua sendo o canal padrão. Glitch AI é um pacote Experimental separado.",
    standardLatest: "Link mais recente do Standard", standardLatestNote: "Canal estável usado pelas instalações existentes.", aiLatest: "Link mais recente da AI", aiLatestNote: "Canal Experimental; requer o perfil Hermes do Glitch.",
    standard: "Standard", ai: "Glitch AI", experimental: "Experimental", standardDescription: "Pacote oficial para trading manual, contas, analytics e replicação. Não inclui aba Glitch AI nem runtime Hermes.", aiDescription: "Edição AI Experimental. AI Auto fica desligado após uma instalação nova. As contas vêm do grupo configurado no Glitch. Não há promessa de lucro, operação autônoma ou prontidão live.",
    file: "Arquivo", size: "Tamanho", released: "Lançado", status: "Status", download: "Baixar", hermesSetup: "Configurar Hermes", installationGuide: "Guia de instalação", sha: "SHA-256", notPublished: "Ainda não publicado", versions: "Versões", files: "arquivos", noRelease: "Nenhuma versão publicada neste canal.", standardHistory: "Histórico Standard", aiHistory: "Histórico AI",
    standardInstall: "Instalação Standard", importTitle: "Importar no NinjaTrader", installSteps: ["Baixe o ZIP Standard.", "NinjaTrader > Tools > Import > NinjaScript Add-On.", "Selecione o ZIP e reinicie quando solicitado.", "Abra o Glitch e valide a licença em Settings."],
    aiInstall: "Instalação AI", profileTitle: "Adicione o perfil Hermes", profileDescription: "Importe o ZIP AI e siga as instruções do perfil público. A instalação do perfil não inicia trading nem retoma cron jobs.", profileLink: "Instruções do perfil Hermes",
    needHelp: "Precisa de ajuda?", website: "Site", documentation: "Documentação", catalogUnavailable: "Catálogo de versões indisponível", copied: "Copiado!",
    footerTitle: "Precisa de acesso ou ajuda com a conta?", footerDescription: "Use os mesmos links oficiais no Site, Docs e Download.", risk: "Risco", terms: "Termos", privacy: "Privacidade",
  },
  es: {
    languageTag: "es-ES", label: "Español", language: "Idioma",
    nav: { home: "Inicio", pricing: "Precios", affiliate: "Afiliados", docs: "Docs", guide: "Guía", startFree: "Empezar gratis", memberHub: "Área de miembros", goPro: "Hazte Pro" },
    official: "Descargas oficiales", title: "Descarga Glitch", subtitle: "para NinjaTrader 8", intro: "Standard sigue siendo el canal predeterminado. Glitch AI es un paquete Experimental separado.",
    standardLatest: "Enlace más reciente de Standard", standardLatestNote: "Canal estable de las instalaciones existentes.", aiLatest: "Enlace más reciente de AI", aiLatestNote: "Canal Experimental; requiere el perfil Hermes de Glitch.",
    standard: "Standard", ai: "Glitch AI", experimental: "Experimental", standardDescription: "Paquete oficial para trading manual, cuentas, analytics y replicación. No incluye pestaña Glitch AI ni runtime Hermes.", aiDescription: "Edición AI Experimental. AI Auto queda apagado tras una instalación nueva. Las cuentas proceden del grupo configurado. No se afirma rentabilidad, operación desatendida ni preparación live.",
    file: "Archivo", size: "Tamaño", released: "Publicado", status: "Estado", download: "Descargar", hermesSetup: "Configurar Hermes", installationGuide: "Guía de instalación", sha: "SHA-256", notPublished: "Aún no publicado", versions: "Versiones", files: "archivos", noRelease: "No hay versiones publicadas en este canal.", standardHistory: "Historial Standard", aiHistory: "Historial AI",
    standardInstall: "Instalación Standard", importTitle: "Importar en NinjaTrader", installSteps: ["Descarga el ZIP Standard.", "NinjaTrader > Tools > Import > NinjaScript Add-On.", "Selecciona el ZIP y reinicia cuando se solicite.", "Abre Glitch y valida la licencia en Settings."],
    aiInstall: "Instalación AI", profileTitle: "Añade el perfil Hermes", profileDescription: "Importa el ZIP AI y sigue las instrucciones públicas. Instalar el perfil no inicia trading ni reanuda cron jobs.", profileLink: "Instrucciones del perfil Hermes",
    needHelp: "¿Necesitas ayuda?", website: "Sitio web", documentation: "Documentación", catalogUnavailable: "Catálogo de versiones no disponible", copied: "¡Copiado!",
    footerTitle: "¿Necesitas acceso o ayuda con la cuenta?", footerDescription: "Usa los mismos enlaces oficiales en Sitio, Docs y Descargas.", risk: "Riesgo", terms: "Términos", privacy: "Privacidad",
  },
  zh: {
    languageTag: "zh-CN", label: "中文", language: "语言",
    nav: { home: "首页", pricing: "价格", affiliate: "合作伙伴", docs: "文档", guide: "指南", startFree: "免费开始", memberHub: "会员中心", goPro: "升级 Pro" },
    official: "官方下载", title: "下载 Glitch", subtitle: "适用于 NinjaTrader 8", intro: "Standard 仍是默认更新通道。Glitch AI 是独立的 Experimental 安装包。",
    standardLatest: "Standard 最新链接", standardLatestNote: "现有 Glitch 安装使用的稳定默认通道。", aiLatest: "AI 最新链接", aiLatestNote: "Experimental AI 通道；需要 Glitch Hermes Profile。",
    standard: "Standard", ai: "Glitch AI", experimental: "Experimental", standardDescription: "用于手动交易、账户管理、analytics 和复制的官方安装包。不含 Glitch AI 标签页或 Hermes 运行时。", aiDescription: "Experimental AI 版。全新安装后 AI Auto 默认关闭。账户来自 Glitch 中配置的组。不承诺盈利、无人值守运行或 live 就绪。",
    file: "文件", size: "大小", released: "发布日期", status: "状态", download: "下载", hermesSetup: "Hermes 设置", installationGuide: "安装指南", sha: "SHA-256", notPublished: "尚未发布", versions: "版本", files: "个文件", noRelease: "此通道尚无发布版本。", standardHistory: "Standard 历史", aiHistory: "AI 历史",
    standardInstall: "Standard 安装", importTitle: "导入 NinjaTrader", installSteps: ["下载 Standard ZIP。", "NinjaTrader > Tools > Import > NinjaScript Add-On。", "选择 ZIP，并按提示重启。", "打开 Glitch，在 Settings 中验证许可。"],
    aiInstall: "AI 安装", profileTitle: "添加 Hermes Profile", profileDescription: "导入 AI ZIP 后按照公开 Profile 说明操作。安装 Profile 不会启动交易或恢复 cron jobs。", profileLink: "Hermes Profile 说明",
    needHelp: "需要帮助？", website: "网站", documentation: "文档", catalogUnavailable: "发布目录不可用", copied: "已复制！",
    footerTitle: "需要产品访问或账户帮助？", footerDescription: "网站、文档和下载均使用相同的官方链接。", risk: "风险", terms: "条款", privacy: "隐私",
  },
  fr: {
    languageTag: "fr-FR", label: "Français", language: "Langue",
    nav: { home: "Accueil", pricing: "Tarifs", affiliate: "Affiliation", docs: "Docs", guide: "Guide", startFree: "Commencer gratuitement", memberHub: "Espace membre", goPro: "Passer Pro" },
    official: "Téléchargements officiels", title: "Télécharger Glitch", subtitle: "pour NinjaTrader 8", intro: "Standard reste le canal par défaut. Glitch AI est un paquet Experimental séparé.",
    standardLatest: "Dernier lien Standard", standardLatestNote: "Canal stable utilisé par les installations existantes.", aiLatest: "Dernier lien AI", aiLatestNote: "Canal Experimental ; nécessite le profil Hermes Glitch.",
    standard: "Standard", ai: "Glitch AI", experimental: "Experimental", standardDescription: "Paquet officiel pour trading manuel, comptes, analytics et réplication. Il ne contient ni onglet Glitch AI ni runtime Hermes.", aiDescription: "Édition AI Experimental. AI Auto est désactivé après une nouvelle installation. Les comptes viennent du groupe Glitch configuré. Aucune promesse de rentabilité, d’autonomie ou de disponibilité live.",
    file: "Fichier", size: "Taille", released: "Publié", status: "Statut", download: "Télécharger", hermesSetup: "Configurer Hermes", installationGuide: "Guide d’installation", sha: "SHA-256", notPublished: "Pas encore publié", versions: "Versions", files: "fichiers", noRelease: "Aucune version publiée sur ce canal.", standardHistory: "Historique Standard", aiHistory: "Historique AI",
    standardInstall: "Installation Standard", importTitle: "Importer dans NinjaTrader", installSteps: ["Téléchargez le ZIP Standard.", "NinjaTrader > Tools > Import > NinjaScript Add-On.", "Sélectionnez le ZIP et redémarrez à la demande.", "Ouvrez Glitch et validez la licence dans Settings."],
    aiInstall: "Installation AI", profileTitle: "Ajouter le profil Hermes", profileDescription: "Importez le ZIP AI puis suivez les instructions publiques. Installer le profil ne démarre pas le trading et ne reprend pas les cron jobs.", profileLink: "Instructions du profil Hermes",
    needHelp: "Besoin d’aide ?", website: "Site", documentation: "Documentation", catalogUnavailable: "Catalogue des versions indisponible", copied: "Copié !",
    footerTitle: "Besoin d’un accès ou d’aide ?", footerDescription: "Utilisez les mêmes liens officiels sur le Site, les Docs et le Téléchargement.", risk: "Risque", terms: "Conditions", privacy: "Confidentialité",
  },
  ru: {
    languageTag: "ru-RU", label: "Русский", language: "Язык",
    nav: { home: "Главная", pricing: "Цены", affiliate: "Партнёры", docs: "Документы", guide: "Инструкция", startFree: "Начать бесплатно", memberHub: "Кабинет", goPro: "Перейти на Pro" },
    official: "Официальные загрузки", title: "Скачать Glitch", subtitle: "для NinjaTrader 8", intro: "Standard остаётся каналом по умолчанию. Glitch AI — отдельный Experimental-пакет.",
    standardLatest: "Последняя ссылка Standard", standardLatestNote: "Стабильный канал для существующих установок.", aiLatest: "Последняя ссылка AI", aiLatestNote: "Experimental AI; требуется профиль Hermes Glitch.",
    standard: "Standard", ai: "Glitch AI", experimental: "Experimental", standardDescription: "Официальный пакет для ручной торговли, счетов, analytics и репликации. Без вкладки Glitch AI и runtime Hermes.", aiDescription: "Experimental AI. После новой установки AI Auto выключен. Счета берутся из настроенной группы Glitch. Нет заявлений о доходности, автономности или готовности live.",
    file: "Файл", size: "Размер", released: "Дата", status: "Статус", download: "Скачать", hermesSetup: "Настройка Hermes", installationGuide: "Инструкция по установке", sha: "SHA-256", notPublished: "Ещё не опубликовано", versions: "Версии", files: "файлов", noRelease: "В этом канале нет опубликованных версий.", standardHistory: "История Standard", aiHistory: "История AI",
    standardInstall: "Установка Standard", importTitle: "Импорт в NinjaTrader", installSteps: ["Скачайте Standard ZIP.", "NinjaTrader > Tools > Import > NinjaScript Add-On.", "Выберите ZIP и перезапустите по запросу.", "Откройте Glitch и проверьте лицензию в Settings."],
    aiInstall: "Установка AI", profileTitle: "Добавьте профиль Hermes", profileDescription: "Импортируйте AI ZIP и следуйте публичной инструкции. Установка профиля не запускает торговлю и cron jobs.", profileLink: "Инструкция профиля Hermes",
    needHelp: "Нужна помощь?", website: "Сайт", documentation: "Документация", catalogUnavailable: "Каталог версий недоступен", copied: "Скопировано!",
    footerTitle: "Нужен доступ или помощь со счётом?", footerDescription: "Используйте единые официальные ссылки на сайте, в документации и загрузках.", risk: "Риск", terms: "Условия", privacy: "Конфиденциальность",
  },
};

export function isDownloadLocale(value: string): value is DownloadLocale {
  return downloadLocales.includes(value as DownloadLocale);
}

export function getDownloadHomeHref(locale: DownloadLocale): string {
  return locale === defaultDownloadLocale ? "/" : `/${locale}`;
}
