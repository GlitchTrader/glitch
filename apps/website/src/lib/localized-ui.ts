import type { Locale } from "@/i18n/routing";

type UiContent = {
  site: {
    description: string;
    homeMetadataTitle: string;
    homeMetadataDescription: string;
    softwareApplicationDescription: string;
  };
  actions: {
    startFree: string;
    goPro: string;
    memberHub: string;
  };
  compatibility: {
    eyebrow: string;
    lead: string;
  };
};

const uiContentByLocale: Record<Locale, UiContent> = {
  en: {
    site: {
      description:
        "Glitch helps prop traders protect eval and funded accounts with compliance enforcement, replication guardrails, Glitch Score analytics, and macro context.",
      homeMetadataTitle: "Glitch - Prop Trading Assistant for NinjaTrader",
      homeMetadataDescription:
        "Glitch helps prop traders protect accounts, scale replication, and trade with Glitch Score, compliance controls, and performance insight.",
      softwareApplicationDescription:
        "Risk-first NinjaTrader software for prop traders focused on compliance, replication control, Glitch Score context, and performance review.",
    },
    actions: {
      startFree: "Start Free",
      goPro: "Go Pro",
      memberHub: "Member Hub",
    },
    compatibility: {
      eyebrow: "Compatibility",
      lead: "Built for NinjaTrader and aligned with prop workflows traders already recognize.",
    },
  },
  pt: {
    site: {
      description:
        "Glitch ajuda prop traders a proteger contas de avaliação e financiadas com conformidade, guardrails de replicação, analytics do Glitch Score e contexto macro.",
      homeMetadataTitle: "Glitch - Assistente de Prop Trading para NinjaTrader",
      homeMetadataDescription:
        "Glitch ajuda prop traders a proteger contas, escalar replicação e operar com Glitch Score, controles de conformidade e inteligência de performance.",
      softwareApplicationDescription:
        "Software NinjaTrader com foco em risco para prop traders, com conformidade, controle de replicação, contexto do Glitch Score e revisão de performance.",
    },
    actions: {
      startFree: "Começar grátis",
      goPro: "Go Pro",
      memberHub: "Member Hub",
    },
    compatibility: {
      eyebrow: "Compatibilidade",
      lead: "Feito para NinjaTrader e alinhado a fluxos de prop trading que os traders já reconhecem.",
    },
  },
  es: {
    site: {
      description:
        "Glitch ayuda a los prop traders a proteger cuentas de evaluación y fondeadas con cumplimiento, guardrails de replicación, analítica de Glitch Score y contexto macro.",
      homeMetadataTitle: "Glitch - Asistente de Prop Trading para NinjaTrader",
      homeMetadataDescription:
        "Glitch ayuda a los prop traders a proteger cuentas, escalar replicación y operar con Glitch Score, controles de cumplimiento e inteligencia de rendimiento.",
      softwareApplicationDescription:
        "Software para NinjaTrader con enfoque en riesgo para prop traders, centrado en cumplimiento, control de replicación, contexto de Glitch Score y revisión de rendimiento.",
    },
    actions: {
      startFree: "Empezar gratis",
      goPro: "Go Pro",
      memberHub: "Member Hub",
    },
    compatibility: {
      eyebrow: "Compatibilidad",
      lead: "Creado para NinjaTrader y alineado con flujos prop que los traders ya reconocen.",
    },
  },
  fr: {
    site: {
      description:
        "Glitch aide les prop traders à protéger comptes d’évaluation et comptes financés grâce à la conformité, aux garde-fous de réplication, au Glitch Score et au contexte macro.",
      homeMetadataTitle: "Glitch - Assistant de Prop Trading pour NinjaTrader",
      homeMetadataDescription:
        "Glitch aide les prop traders à protéger leurs comptes, à scaler la réplication et à trader avec Glitch Score, contrôles de conformité et intelligence de performance.",
      softwareApplicationDescription:
        "Logiciel NinjaTrader axé risque pour prop traders, centré sur la conformité, le contrôle de réplication, le contexte Glitch Score et la revue de performance.",
    },
    actions: {
      startFree: "Commencer gratuitement",
      goPro: "Go Pro",
      memberHub: "Member Hub",
    },
    compatibility: {
      eyebrow: "Compatibilité",
      lead: "Conçu pour NinjaTrader et aligné sur des workflows prop que les traders reconnaissent déjà.",
    },
  },
  ru: {
    site: {
      description:
        "Glitch помогает prop-трейдерам защищать оценочные и funded-счета с помощью контроля соответствия, защиты репликации, аналитики Glitch Score и макроконтекста.",
      homeMetadataTitle: "Glitch - Ассистент Prop Trading для NinjaTrader",
      homeMetadataDescription:
        "Glitch помогает prop-трейдерам защищать счета, масштабировать репликацию и торговать с Glitch Score, контролем соблюдения правил и аналитикой производительности.",
      softwareApplicationDescription:
        "Risk-first ПО для NinjaTrader, созданное для prop-трейдеров и сфокусированное на соблюдении правил, контроле репликации, контексте Glitch Score и обзоре результатов.",
    },
    actions: {
      startFree: "Начать бесплатно",
      goPro: "Go Pro",
      memberHub: "Member Hub",
    },
    compatibility: {
      eyebrow: "Совместимость",
      lead: "Создано для NinjaTrader и выстроено под prop-процессы, которые трейдеры уже знают.",
    },
  },
  zh: {
    site: {
      description:
        "Glitch 通过合规控制、复制护栏、Glitch Score 分析与宏观上下文，帮助资管交易员保护评估账户和 funded 账户。",
      homeMetadataTitle: "Glitch - NinjaTrader 资管交易助手",
      homeMetadataDescription:
        "Glitch 帮助资管交易员保护账户、扩展复制，并借助 Glitch Score、合规控制和绩效洞察进行交易。",
      softwareApplicationDescription:
        "面向 NinjaTrader 的风险优先软件，服务于资管交易员，聚焦合规、复制控制、Glitch Score 上下文与绩效复盘。",
    },
    actions: {
      startFree: "免费开始",
      goPro: "Go Pro",
      memberHub: "Member Hub",
    },
    compatibility: {
      eyebrow: "兼容性",
      lead: "为 NinjaTrader 打造，并与交易员熟悉的资管工作流保持一致。",
    },
  },
};

export function getUiContent(locale?: string): UiContent {
  switch (locale) {
    case "pt":
    case "es":
    case "fr":
    case "ru":
    case "zh":
      return uiContentByLocale[locale];
    default:
      return uiContentByLocale.en;
  }
}
