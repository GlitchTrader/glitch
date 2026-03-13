type FaqItem = {
  question: string;
  answer: string;
};

type OfferFeatureCard = {
  title: string;
  body: string;
};

type MarketingContent = {
  home: {
    featurePills: string[];
    faqItems: FaqItem[];
    activationSteps: string[];
    dailySteps: string[];
    premiumCheckoutNote: string;
  };
  offer: {
    metadataTitle: string;
    metadataDescription: string;
    badge: string;
    title: string;
    lead: string;
    sublead: string;
    alreadyJoinedLabel: string;
    costTitle: string;
    costParagraphs: string[];
    startTitle: string;
    freeEyebrow: string;
    freeTitle: string;
    freeBestForLabel: string;
    freeBestForBody: string;
    proEyebrow: string;
    proTitle: string;
    proBestForLabel: string;
    proBestForBody: string;
    freeHighlights: string[];
    paidHighlights: string[];
    engineTitle: string;
    engineCards: OfferFeatureCard[];
    pricingTitle: string;
    pricingLead: string;
    memberHubBlurb: string;
    faqTitle: string;
    faqItems: FaqItem[];
  };
  pricing: {
    metadataTitle: string;
    metadataDescription: string;
    badge: string;
    title: string;
    lead: string;
    memberHubLead: string;
    memberHubBlurb: string;
    upgradeTitle: string;
    upgradeLead: string;
    freeFoundationTitle: string;
    freeBestForLabel: string;
    freeBestForBody: string;
    paidUnlocksTitle: string;
    paidBestForLabel: string;
    paidBestForBody: string;
    paidNote: string;
    accessTitle: string;
    upgradeFlowTitle: string;
    upgradeFlowSteps: string[];
    roadmapTitle: string;
    roadmapItems: string[];
    faqTitle: string;
    promoCodePolicy: string;
    faqItems: FaqItem[];
  };
  affiliate: {
    metadataTitle: string;
    metadataDescription: string;
    badge: string;
    title: string;
    lead: string;
    commissionTitle: string;
    commissionBullets: string[];
    commissionNote: string;
    promoTiersTitle: string;
    promoTiersBullets: string[];
    rulesTitle: string;
    rulesBullets: string[];
    rulesNote: string;
    dashboardCta: string;
    productPageCta: string;
    dashboardNote: string;
    termsLead: string;
    termsLink: string;
    termsTail: string;
  };
};

const en: MarketingContent = {
  home: {
    featurePills: [
      "Glitch Score",
      "Compliance Layer",
      "Replication Control",
      "Real-Time Analysis",
      "Performance + Insights",
    ],
    faqItems: [
      {
        question: "What exactly is Glitch?",
        answer: "Glitch is a risk-first trading assistant for NinjaTrader. It centralizes compliance, replication, analytics, and performance review in one operating layer.",
      },
      {
        question: "Is Glitch an auto-trading bot?",
        answer: "No. You control strategy and execution. Glitch helps you enforce risk and improve decisions before avoidable mistakes become account damage.",
      },
      {
        question: "Can I use my current indicators and strategies?",
        answer: "Yes. Keep your existing indicators, automated strategies, and bots. Glitch is designed to complement your workflow, not replace it.",
      },
      {
        question: "Does Glitch work across prop firm models?",
        answer: "Yes. Glitch is built for cross-prop workflows with preloaded firm-rule frameworks and configurable compliance behavior.",
      },
      {
        question: "Can Glitch handle high account counts?",
        answer: "Paid access supports up to 10 masters/groups and up to 100 followers per group, designed for serious multi-account operations.",
      },
      {
        question: "What is Glitch Score?",
        answer: "Glitch Score is Glitch's composite signal layer that consolidates multi-timeframe context so you can read conditions faster and with more structure.",
      },
      {
        question: "How do the paid plans work?",
        answer: "Choose Monthly / Annual for flexible billing, or Lifetime access for one payment. After checkout, Member Hub handles download, updates, and activation.",
      },
      {
        question: "Is there a marketplace coming?",
        answer: "Yes. The roadmap includes a marketplace for third-party indicators, strategies, and partner prop firm offers.",
      },
      {
        question: "Does Glitch guarantee profits?",
        answer: "No. Glitch improves process quality and risk discipline. Trading outcomes still depend on strategy quality and execution discipline.",
      },
    ],
    activationSteps: [
      "Choose Free, Monthly / Annual, or Lifetime access.",
      "Open Member Hub and follow Start Here.",
      "Download and install the latest Glitch build.",
      "Open New > Glitch in NinjaTrader.",
      "Paste your key in Settings and click Validate License.",
    ],
    dailySteps: [
      "Review risk status, warning count, and account posture.",
      "Validate replication and compliance before session open.",
      "Read Glitch Score and macro context before execution.",
      "Journal outcomes and review metrics after the close.",
      "Iterate process weekly. Professionals review, amateurs react.",
    ],
    premiumCheckoutNote: "Choose monthly, annual, or lifetime at checkout.",
  },
  offer: {
    metadataTitle: "Glitch Offer - Risk-First Trading Assistant for NinjaTrader",
    metadataDescription: "Explore the Glitch offer: compliance enforcement, replication control, Glitch Score analytics, and premium scaling for prop traders.",
    badge: "The Glitch Offer",
    title: "Built for traders who treat this like a business",
    lead: "If you are done with avoidable rule breaks, replication chaos, and context-blind execution, this is the operating layer you were missing.",
    sublead: "Glitch is defining a new category: the risk-first trading assistant for prop traders who run real operations, not toy setups.",
    alreadyJoinedLabel: "Already joined?",
    costTitle: "The cost of preventable errors is brutal",
    costParagraphs: [
      "One broken process can reset an eval, pause a payout account, or burn confidence for weeks. Most of that is avoidable if the operating layer is strong enough.",
      "Glitch is designed to make the right actions easier and the wrong actions harder. That is how durable trading operations are built.",
    ],
    startTitle: "Start free. Upgrade when the business case is clear.",
    freeEyebrow: "Start Free",
    freeTitle: "Core account protection",
    freeBestForLabel: "Best for",
    freeBestForBody: "Traders validating the workflow, building discipline, and protecting accounts before scaling.",
    proEyebrow: "Go Pro",
    proTitle: "Scale, depth, and precision",
    proBestForLabel: "Best for",
    proBestForBody: "Traders running larger account stacks, deeper analysis, and faster review loops.",
    freeHighlights: [
      "Manual + auto replication",
      "Compliance + firm rules",
      "1 master + 2 followers",
      "Risk control indicators",
      "Replicate + Flatten All",
      "Core assistant layer",
    ],
    paidHighlights: [
      "10 groups + 100 followers each",
      "Glitch Score across 1m, 5m, 15m, and 60m",
      "Journal, Metrics + Insights",
      "Technical, macro + sentiment context",
      "Nasdaq + Mag7 enriched data",
      "Bring your own indicators + bots",
    ],
    engineTitle: "What makes Glitch tick",
    engineCards: [
      { title: "Compliance enforcement", body: "Rule-aware operating logic to keep your accounts inside firm constraints before breaches happen." },
      { title: "Replication system", body: "Multi-account synchronization, follower scaling, and control surfaces designed for serious account stacks." },
      { title: "Glitch Score", body: "Structured directional context across multiple timeframes, reducing signal noise and emotional entries." },
      { title: "Journal + Insights engine", body: "Performance feedback loops that transform random outcomes into measurable process improvement." },
      { title: "Macro and sentiment stack", body: "Nasdaq, Mag7, macro and news layers in one view so context is no longer an afterthought." },
      { title: "Open workflow philosophy", body: "Bring your own indicators, automation, and strategy stack. Glitch centralizes and hardens the operation." },
    ],
    pricingTitle: "Pricing in one clean view",
    pricingLead: "Free gets you started. Monthly / Annual gives you flexible premium access. Lifetime access is the permanent seat for traders who already know Glitch belongs in the stack.",
    memberHubBlurb: "is where your downloads, updates, and onboarding steps live.",
    faqTitle: "FAQ",
    faqItems: [
      { question: "Why call Glitch a trading assistant?", answer: "Because it assists every step of your operating process: risk controls, replication discipline, context analysis, and performance review." },
      { question: "Does Glitch replace my strategy?", answer: "No. Your strategy remains yours. Glitch improves execution quality and risk discipline around that strategy." },
      { question: "Can I run Glitch with automated systems?", answer: "Yes. Glitch is built to work alongside manual and automated workflows so you keep flexibility while enforcing guardrails." },
      { question: "What makes paid access worth it?", answer: "Scale, depth, and speed: higher account limits, stronger context layers, and a serious feedback loop through Journal, Metrics, and Insights." },
      { question: "How do monthly, yearly, and lifetime access work?", answer: "Monthly and yearly sit inside the flexible paid plan. Lifetime access is the one-payment option. Both unlock the same premium Glitch stack." },
      { question: "Do you promise payouts or profits?", answer: "No. We promise professional-grade tooling for risk and execution discipline. Outcomes depend on the trader and market." },
    ],
  },
  pricing: {
    metadataTitle: "Glitch Pricing - Free, Monthly or Annual, and Lifetime Access",
    metadataDescription: "Compare Glitch pricing for prop traders: start free, upgrade to monthly or annual premium access, or lock in lifetime access.",
    badge: "Pricing",
    title: "Straight pricing for serious operators.",
    lead: "Three plans. Clear trade-offs. Free gets you the guardrails. Monthly / Annual gives you the full premium stack with flexible billing. Lifetime access gives you the same premium stack without recurring charges.",
    memberHubLead: "After checkout,",
    memberHubBlurb: "is where downloads, onboarding, updates, and activation steps live.",
    upgradeTitle: "What changes when you upgrade",
    upgradeLead: "The jump from Free to paid is not a cosmetic upgrade. It is where Glitch becomes a full operating layer for serious multi-account trading.",
    freeFoundationTitle: "Free foundation",
    freeBestForLabel: "Best for",
    freeBestForBody: "Traders validating the workflow, building discipline, and protecting accounts before scaling.",
    paidUnlocksTitle: "Every paid plan unlocks",
    paidBestForLabel: "Best for",
    paidBestForBody: "Traders running larger account stacks, deeper analysis, and faster review loops.",
    paidNote: "Monthly, yearly, and lifetime access all unlock the same premium stack. Only the billing model changes.",
    accessTitle: "How access works",
    upgradeFlowTitle: "Upgrade flow",
    upgradeFlowSteps: [
      "Choose Free, Monthly / Annual, or Lifetime access.",
      "Complete checkout, then open Member Hub.",
      "Download the latest build and activate your license.",
      "Launch Glitch and configure your workflow.",
      "Review your rules, replication, and daily operating setup.",
    ],
    roadmapTitle: "Roadmap",
    roadmapItems: [
      "Marketplace for third-party indicators",
      "Marketplace for third-party strategies",
      "Integrated partner prop firm offers",
      "Expanded trading assistant automations and workflows",
    ],
    faqTitle: "FAQ",
    promoCodePolicy: "Promo code policy: one promo per order. Attribution details are on Affiliate and Terms pages.",
    faqItems: [
      { question: "What is the difference between Monthly / Annual and Lifetime access?", answer: "The premium product is the same. Monthly / Annual gives you flexible billing. Lifetime access gives you the same premium stack with one payment and no recurring charge." },
      { question: "Can I start free and upgrade later?", answer: "Yes. Start free, validate fit, and upgrade when the workflow has earned a bigger role in your operation." },
      { question: "Do all paid plans include the full premium toolset?", answer: "Yes. Monthly, yearly, and lifetime access all unlock the same premium compliance, replication, analytics, and insight stack." },
      { question: "Can I use my own indicators and automation stack?", answer: "Yes. Glitch is designed to sit on top of your existing setup as the risk and execution assistant layer." },
      { question: "Is Glitch suitable for high account-count operations?", answer: "Yes. Paid access supports up to 10 masters/groups and up to 100 followers per group for serious scaling." },
      { question: "Where do download and activation happen?", answer: "Inside Member Hub. That is where you access the latest build, onboarding steps, updates, and activation guidance after joining." },
      { question: "Do promo codes stack?", answer: "No. One promo code per order." },
      { question: "Is marketplace support coming?", answer: "Yes. The roadmap includes third-party indicators, strategies, and partner prop firm offers in a unified marketplace." },
    ],
  },
  affiliate: {
    metadataTitle: "Glitch Affiliate Program - Creator and Partner Commissions",
    metadataDescription: "Apply for the Glitch affiliate program and get commission details, promo rules, attribution terms, and access to affiliate links.",
    badge: "Glitch Affiliate Program",
    title: "Promote a serious product. Earn serious commission.",
    lead: "If your audience cares about risk discipline, prop firm compliance, and account longevity, this program was designed for you.",
    commissionTitle: "Commission model",
    commissionBullets: [
      "20% recurring commission on active subscriptions",
      "Promo code or last-click attribution",
      "No commission on Free / Lite plan",
    ],
    commissionNote: "Commission keeps paying as long as membership remains active.",
    promoTiersTitle: "Promo tiers",
    promoTiersBullets: [
      "Public promo: up to 20% off",
      "Selected creators: up to 50% off",
      "Exclusive campaigns: up to 100% off",
    ],
    rulesTitle: "Program rules",
    rulesBullets: [
      "No stacking promos",
      "No self-referrals",
      "Unique code + UTM required for clean attribution",
      "No fake traffic, cookie stuffing, or misleading claims",
    ],
    rulesNote: "Violations may lead to immediate disqualification, payout reversal, and permanent removal.",
    dashboardCta: "Affiliate Dashboard",
    productPageCta: "Product Page",
    dashboardNote: "This opens Whop's affiliate dashboard, where approved offers expose your unique referral link and assets.",
    termsLead: "Participation in the affiliate program is subject to our",
    termsLink: "Terms of Service",
    termsTail: "Commission terms, payout timing, and attribution are at our discretion and may change; see Terms for limitations of liability and other legal terms.",
  },
};

const pt: MarketingContent = {
  home: {
    featurePills: ["Glitch Score", "Camada de conformidade", "Controle de replicação", "Análise em tempo real", "Performance + Insights"],
    faqItems: [
      { question: "O que é exatamente o Glitch?", answer: "Glitch é um assistente de trading com foco em risco para NinjaTrader. Ele centraliza conformidade, replicação, análise e revisão de performance em uma única camada operacional." },
      { question: "O Glitch é um bot de trading automático?", answer: "Não. Você controla a estratégia e a execução. O Glitch ajuda a aplicar risco e melhorar decisões antes que erros evitáveis virem dano de conta." },
      { question: "Posso usar meus indicadores e estratégias atuais?", answer: "Sim. Mantenha seus indicadores, estratégias automáticas e bots. O Glitch complementa seu fluxo; não substitui." },
      { question: "O Glitch funciona em diferentes modelos de prop firm?", answer: "Sim. O Glitch foi feito para fluxos cross-prop, com estruturas de regras pré-carregadas e comportamento de conformidade configurável." },
      { question: "O Glitch aguenta muitas contas?", answer: "O acesso pago suporta até 10 masters/grupos e até 100 seguidores por grupo, pensado para operações sérias com múltiplas contas." },
      { question: "O que é o Glitch Score?", answer: "O Glitch Score é a camada composta de sinal do Glitch, que consolida contexto em múltiplos timeframes para leitura mais rápida e estruturada." },
      { question: "Como funcionam os planos pagos?", answer: "Escolha Mensal / Anual para cobrança flexível, ou Vitalício em pagamento único. Depois do checkout, o Member Hub cuida de download, updates e ativação." },
      { question: "Vai existir marketplace?", answer: "Sim. O roadmap inclui marketplace para indicadores de terceiros, estratégias e ofertas de prop firms parceiras." },
      { question: "O Glitch garante lucro?", answer: "Não. O Glitch melhora qualidade de processo e disciplina de risco. O resultado ainda depende da estratégia e da execução do trader." },
    ],
    activationSteps: [
      "Escolha acesso Grátis, Mensal / Anual ou Vitalício.",
      "Abra o Member Hub e siga o Start Here.",
      "Baixe e instale a versão mais recente do Glitch.",
      "Abra New > Glitch no NinjaTrader.",
      "Cole sua chave em Settings e clique em Validate License.",
    ],
    dailySteps: [
      "Revise status de risco, contagem de alertas e postura da conta.",
      "Valide replicação e conformidade antes da abertura da sessão.",
      "Leia o Glitch Score e o contexto macro antes de executar.",
      "Registre os resultados e revise métricas após o fechamento.",
      "Itere o processo semanalmente. Profissionais revisam; amadores reagem.",
    ],
    premiumCheckoutNote: "Escolha mensal, anual ou vitalício no checkout.",
  },
  offer: {
    metadataTitle: "Oferta Glitch - Assistente de Trading com Foco em Risco para NinjaTrader",
    metadataDescription: "Explore a oferta do Glitch: conformidade, controle de replicação, analytics com Glitch Score e escala premium para prop traders.",
    badge: "A oferta Glitch",
    title: "Feito para traders que tratam isso como negócio",
    lead: "Se você cansou de quebras de regra evitáveis, caos na replicação e execução sem contexto, essa é a camada operacional que estava faltando.",
    sublead: "O Glitch está definindo uma nova categoria: o assistente de trading com foco em risco para prop traders que operam de verdade, não setups de brinquedo.",
    alreadyJoinedLabel: "Já é membro?",
    costTitle: "O custo dos erros evitáveis é brutal",
    costParagraphs: [
      "Um processo quebrado pode resetar uma avaliação, pausar uma conta de payout ou destruir sua confiança por semanas. A maior parte disso é evitável quando a camada operacional é forte.",
      "O Glitch foi projetado para tornar as ações certas mais fáceis e as erradas mais difíceis. É assim que operações duráveis são construídas.",
    ],
    startTitle: "Comece grátis. Atualize quando o caso de negócio ficar claro.",
    freeEyebrow: "Começar grátis",
    freeTitle: "Proteção central de conta",
    freeBestForLabel: "Melhor para",
    freeBestForBody: "Traders validando o fluxo, construindo disciplina e protegendo contas antes de escalar.",
    proEyebrow: "Go Pro",
    proTitle: "Escala, profundidade e precisão",
    proBestForLabel: "Melhor para",
    proBestForBody: "Traders rodando mais contas, análise mais profunda e ciclos de revisão mais rápidos.",
    freeHighlights: [
      "Replicação manual + automática",
      "Conformidade + regras da firma",
      "1 master + 2 seguidores",
      "Indicadores de risco",
      "Replicate + Flatten All",
      "Camada central do assistente",
    ],
    paidHighlights: [
      "10 grupos + 100 seguidores cada",
      "Glitch Score em 1m, 5m, 15m e 60m",
      "Journal, Metrics + Insights",
      "Contexto técnico, macro + sentimento",
      "Dados enriquecidos de Nasdaq + Mag7",
      "Use seus indicadores + bots",
    ],
    engineTitle: "O que faz o Glitch funcionar",
    engineCards: [
      { title: "Aplicação de conformidade", body: "Lógica operacional orientada por regras para manter suas contas dentro das restrições da firma antes que as violações aconteçam." },
      { title: "Sistema de replicação", body: "Sincronização multi-conta, escala de seguidores e controles pensados para stacks sérias de contas." },
      { title: "Glitch Score", body: "Contexto direcional estruturado em múltiplos timeframes, reduzindo ruído de sinal e entradas emocionais." },
      { title: "Engine de Journal + Insights", body: "Loops de feedback de performance que transformam resultados aleatórios em melhoria de processo mensurável." },
      { title: "Stack macro e sentimento", body: "Nasdaq, Mag7, macro e notícias em uma única visão para que contexto deixe de ser um detalhe." },
      { title: "Filosofia de workflow aberto", body: "Use seus próprios indicadores, automações e estratégias. O Glitch centraliza e endurece a operação." },
    ],
    pricingTitle: "Preço em uma visão limpa",
    pricingLead: "O plano grátis coloca você em movimento. Mensal / Anual entrega acesso premium flexível. Vitalício é o assento permanente para quem já sabe que o Glitch pertence à stack.",
    memberHubBlurb: "é onde ficam seus downloads, atualizações e passos de onboarding.",
    faqTitle: "Perguntas frequentes",
    faqItems: [
      { question: "Por que chamar o Glitch de assistente de trading?", answer: "Porque ele assiste cada etapa do processo operacional: controles de risco, disciplina de replicação, análise de contexto e revisão de performance." },
      { question: "O Glitch substitui minha estratégia?", answer: "Não. Sua estratégia continua sendo sua. O Glitch melhora a qualidade de execução e a disciplina de risco em torno dela." },
      { question: "Posso usar o Glitch com sistemas automáticos?", answer: "Sim. O Glitch foi feito para trabalhar ao lado de fluxos manuais e automáticos, mantendo flexibilidade com guardrails." },
      { question: "O que torna o acesso pago valioso?", answer: "Escala, profundidade e velocidade: mais contas, camadas de contexto mais fortes e um loop sério de Journal, Metrics e Insights." },
      { question: "Como funcionam os acessos mensal, anual e vitalício?", answer: "Mensal e anual ficam dentro do plano pago flexível. Vitalício é a opção de pagamento único. Ambos destravam a mesma stack premium do Glitch." },
      { question: "Vocês prometem payout ou lucro?", answer: "Não. Prometemos ferramentas profissionais para disciplina de risco e execução. Os resultados dependem do trader e do mercado." },
    ],
  },
  pricing: {
    metadataTitle: "Preços Glitch - Grátis, Mensal ou Anual e Acesso Vitalício",
    metadataDescription: "Compare os preços do Glitch para prop traders: comece grátis, faça upgrade para premium mensal ou anual, ou garanta acesso vitalício.",
    badge: "Preços",
    title: "Preço direto para operadores sérios.",
    lead: "Três planos. Trade-offs claros. O grátis entrega guardrails. Mensal / Anual entrega a stack premium completa com cobrança flexível. Vitalício entrega a mesma stack sem cobrança recorrente.",
    memberHubLead: "Depois do checkout,",
    memberHubBlurb: "é onde ficam downloads, onboarding, atualizações e passos de ativação.",
    upgradeTitle: "O que muda quando você faz upgrade",
    upgradeLead: "O salto do Grátis para o pago não é cosmético. É o ponto em que o Glitch vira uma camada operacional completa para trading sério com múltiplas contas.",
    freeFoundationTitle: "Base gratuita",
    freeBestForLabel: "Melhor para",
    freeBestForBody: "Traders validando o fluxo, construindo disciplina e protegendo contas antes de escalar.",
    paidUnlocksTitle: "Todo plano pago libera",
    paidBestForLabel: "Melhor para",
    paidBestForBody: "Traders rodando mais contas, análise mais profunda e ciclos de revisão mais rápidos.",
    paidNote: "Mensal, anual e vitalício desbloqueiam a mesma stack premium. Só o modelo de cobrança muda.",
    accessTitle: "Como o acesso funciona",
    upgradeFlowTitle: "Fluxo de upgrade",
    upgradeFlowSteps: [
      "Escolha Grátis, Mensal / Anual ou Vitalício.",
      "Conclua o checkout e depois abra o Member Hub.",
      "Baixe a versão mais recente e ative sua licença.",
      "Abra o Glitch e configure seu fluxo.",
      "Revise regras, replicação e setup operacional diário.",
    ],
    roadmapTitle: "Roadmap",
    roadmapItems: [
      "Marketplace para indicadores de terceiros",
      "Marketplace para estratégias de terceiros",
      "Ofertas integradas de prop firms parceiras",
      "Automações e workflows expandidos do assistente de trading",
    ],
    faqTitle: "Perguntas frequentes",
    promoCodePolicy: "Política de cupom: um cupom por pedido. Detalhes de atribuição estão nas páginas de Afiliados e Termos.",
    faqItems: [
      { question: "Qual a diferença entre Mensal / Anual e Vitalício?", answer: "O produto premium é o mesmo. Mensal / Anual oferece cobrança flexível. Vitalício oferece a mesma stack premium com um pagamento e sem recorrência." },
      { question: "Posso começar grátis e fazer upgrade depois?", answer: "Sim. Comece grátis, valide o encaixe e faça upgrade quando o workflow ganhar um papel maior na operação." },
      { question: "Todos os planos pagos incluem a stack premium completa?", answer: "Sim. Mensal, anual e vitalício liberam a mesma stack premium de conformidade, replicação, analytics e insights." },
      { question: "Posso usar meus próprios indicadores e automações?", answer: "Sim. O Glitch foi projetado para ficar por cima do seu setup atual como camada de risco e execução." },
      { question: "O Glitch serve para operações com muitas contas?", answer: "Sim. O acesso pago suporta até 10 masters/grupos e até 100 seguidores por grupo para escalas sérias." },
      { question: "Onde acontecem download e ativação?", answer: "Dentro do Member Hub. É ali que você acessa a build mais recente, onboarding, updates e orientação de ativação depois de entrar." },
      { question: "Cupons acumulam?", answer: "Não. Um cupom por pedido." },
      { question: "O suporte a marketplace está vindo?", answer: "Sim. O roadmap inclui indicadores de terceiros, estratégias e ofertas de prop firms em um marketplace unificado." },
    ],
  },
  affiliate: {
    metadataTitle: "Programa de Afiliados Glitch - Comissões para Criadores e Parceiros",
    metadataDescription: "Participe do programa de afiliados Glitch e veja comissões, regras de promo, atribuição e acesso aos links de afiliado.",
    badge: "Programa de afiliados Glitch",
    title: "Promova um produto sério. Ganhe comissão séria.",
    lead: "Se sua audiência se importa com disciplina de risco, conformidade de prop firm e longevidade de conta, este programa foi desenhado para ela.",
    commissionTitle: "Modelo de comissão",
    commissionBullets: [
      "20% de comissão recorrente em assinaturas ativas",
      "Atribuição por código promocional ou último clique válido",
      "Sem comissão no plano Free / Lite",
    ],
    commissionNote: "A comissão continua pagando enquanto a assinatura permanecer ativa.",
    promoTiersTitle: "Níveis promocionais",
    promoTiersBullets: [
      "Promo pública: até 20% off",
      "Criadores selecionados: até 50% off",
      "Campanhas exclusivas: até 100% off",
    ],
    rulesTitle: "Regras do programa",
    rulesBullets: [
      "Sem empilhar promos",
      "Sem autoindicação",
      "Código único + UTM obrigatórios para atribuição limpa",
      "Sem tráfego falso, cookie stuffing ou alegações enganosas",
    ],
    rulesNote: "Violações podem levar à desclassificação imediata, reversão de payout e remoção permanente.",
    dashboardCta: "Painel de afiliado",
    productPageCta: "Página do produto",
    dashboardNote: "Isso abre o painel de afiliados da Whop, onde ofertas aprovadas mostram seu link único e materiais.",
    termsLead: "A participação no programa de afiliados está sujeita aos nossos",
    termsLink: "Termos de serviço",
    termsTail: "Os termos de comissão, timing de payout e atribuição ficam a nosso critério e podem mudar; consulte os Termos para limites de responsabilidade e demais condições legais.",
  },
};

const es: MarketingContent = {
  home: {
    featurePills: ["Glitch Score", "Capa de cumplimiento", "Control de replicación", "Análisis en tiempo real", "Rendimiento + Insights"],
    faqItems: [
      { question: "¿Qué es exactamente Glitch?", answer: "Glitch es un asistente de trading centrado en riesgo para NinjaTrader. Reúne cumplimiento, replicación, análisis y revisión de rendimiento en una sola capa operativa." },
      { question: "¿Glitch es un bot de trading automático?", answer: "No. Tú sigues controlando la estrategia y la ejecución. Glitch ayuda a reforzar el riesgo y a mejorar decisiones antes de que un error operativo se convierta en daño de cuenta." },
      { question: "¿Puedo usar mis indicadores y estrategias actuales?", answer: "Sí. Puedes mantener tus indicadores, estrategias automáticas y bots. Glitch complementa tu flujo; no lo sustituye." },
      { question: "¿Glitch funciona con distintos modelos de prop firm?", answer: "Sí. Está pensado para flujos cross-prop, con marcos de reglas precargados y comportamiento de cumplimiento configurable." },
      { question: "¿Glitch soporta muchas cuentas?", answer: "El acceso de pago admite hasta 10 masters o grupos y hasta 100 seguidores por grupo, pensado para operaciones serias de varias cuentas." },
      { question: "¿Qué es Glitch Score?", answer: "Glitch Score es la capa compuesta de señal de Glitch. Resume contexto en varios marcos temporales para que la lectura sea más rápida y estructurada." },
      { question: "¿Cómo funcionan los planes de pago?", answer: "Puedes elegir mensual o anual para facturación flexible, o vitalicio con un único pago. Después del checkout, Member Hub concentra descarga, actualización y activación." },
      { question: "¿Habrá marketplace?", answer: "Sí. El roadmap incluye un marketplace para indicadores de terceros, estrategias y ofertas de prop firms asociadas." },
      { question: "¿Glitch garantiza ganancias?", answer: "No. Glitch mejora la calidad del proceso y la disciplina de riesgo. El resultado sigue dependiendo de la estrategia y de la ejecución del trader." },
    ],
    activationSteps: [
      "Elige acceso Gratis, Mensual / Anual o Vitalicio.",
      "Abre Member Hub y sigue Start Here.",
      "Descarga e instala la versión más reciente de Glitch.",
      "Abre New > Glitch en NinjaTrader.",
      "Pega tu clave en Settings y haz clic en Validate License.",
    ],
    dailySteps: [
      "Revisa estado de riesgo, avisos y postura de cuenta.",
      "Valida replicación y cumplimiento antes de abrir la sesión.",
      "Lee Glitch Score y el contexto macro antes de ejecutar.",
      "Registra resultados y revisa métricas al cierre.",
      "Itera el proceso cada semana. Los profesionales revisan; los amateurs reaccionan.",
    ],
    premiumCheckoutNote: "Elige mensual, anual o vitalicio en el checkout.",
  },
  offer: {
    metadataTitle: "Oferta Glitch - Asistente de Trading con Enfoque en Riesgo para NinjaTrader",
    metadataDescription: "Explora la oferta de Glitch: cumplimiento, control de replicación, analítica con Glitch Score y escala premium para prop traders.",
    badge: "La oferta de Glitch",
    title: "Hecho para traders que tratan esto como un negocio",
    lead: "Si estás cansado de romper reglas evitables, del caos en la replicación y de ejecutar sin contexto, esta es la capa operativa que te faltaba.",
    sublead: "Glitch define una nueva categoría: el asistente de trading centrado en riesgo para prop traders que operan de verdad, no para setups de juguete.",
    alreadyJoinedLabel: "¿Ya te uniste?",
    costTitle: "El coste de los errores evitables es brutal",
    costParagraphs: [
      "Un proceso roto puede reiniciar una evaluación, pausar una cuenta de payout o destruir tu confianza durante semanas. Gran parte de eso se evita cuando la capa operativa es fuerte.",
      "Glitch está diseñado para hacer más fácil la acción correcta y más difícil la acción equivocada. Así se construyen operaciones duraderas.",
    ],
    startTitle: "Empieza gratis. Haz upgrade cuando el caso de negocio sea claro.",
    freeEyebrow: "Empezar gratis",
    freeTitle: "Protección central de cuenta",
    freeBestForLabel: "Mejor para",
    freeBestForBody: "Traders que validan el flujo, construyen disciplina y protegen cuentas antes de escalar.",
    proEyebrow: "Go Pro",
    proTitle: "Escala, profundidad y precisión",
    proBestForLabel: "Mejor para",
    proBestForBody: "Traders que operan más cuentas, análisis más profundo y ciclos de revisión más rápidos.",
    freeHighlights: [
      "Replicación manual + automática",
      "Cumplimiento + reglas de la firma",
      "1 master + 2 seguidores",
      "Indicadores de riesgo",
      "Replicate + Flatten All",
      "Capa central del asistente",
    ],
    paidHighlights: [
      "10 grupos + 100 seguidores cada uno",
      "Glitch Score en 1m, 5m, 15m y 60m",
      "Journal, Metrics + Insights",
      "Contexto técnico, macro y de sentimiento",
      "Datos enriquecidos de Nasdaq + Mag7",
      "Usa tus indicadores y bots",
    ],
    engineTitle: "Qué hace funcionar a Glitch",
    engineCards: [
      { title: "Cumplimiento operativo", body: "Lógica orientada por reglas para mantener las cuentas dentro de los límites de la firma antes de que ocurra una infracción." },
      { title: "Sistema de replicación", body: "Sincronización multi-cuenta, escala de seguidores y controles diseñados para stacks serias de cuentas." },
      { title: "Glitch Score", body: "Contexto direccional estructurado en varios marcos temporales para reducir ruido y entradas emocionales." },
      { title: "Motor Journal + Insights", body: "Bucles de feedback que convierten resultados aleatorios en mejora medible del proceso." },
      { title: "Stack macro y de sentimiento", body: "Nasdaq, Mag7, macro y noticias en una sola vista para que el contexto no sea un añadido tardío." },
      { title: "Filosofía de flujo abierto", body: "Trae tus indicadores, automatizaciones y estrategias. Glitch centraliza y endurece la operación." },
    ],
    pricingTitle: "Precios en una vista limpia",
    pricingLead: "Gratis te pone en marcha. Mensual / Anual te da acceso premium flexible. Vitalicio es el asiento permanente para quien ya sabe que Glitch debe estar en su stack.",
    memberHubBlurb: "es donde viven tus descargas, actualizaciones y pasos de onboarding.",
    faqTitle: "Preguntas frecuentes",
    faqItems: [
      { question: "¿Por qué llamar a Glitch un asistente de trading?", answer: "Porque asiste cada etapa del proceso operativo: controles de riesgo, disciplina de replicación, análisis de contexto y revisión de rendimiento." },
      { question: "¿Glitch sustituye mi estrategia?", answer: "No. Tu estrategia sigue siendo tuya. Glitch mejora la calidad de ejecución y la disciplina de riesgo alrededor de ella." },
      { question: "¿Puedo usar Glitch con sistemas automáticos?", answer: "Sí. Glitch está hecho para trabajar junto a flujos manuales y automáticos, manteniendo flexibilidad con guardrails." },
      { question: "¿Qué hace valioso el acceso de pago?", answer: "Escala, profundidad y velocidad: más cuentas, capas de contexto más fuertes y un bucle serio de Journal, Metrics e Insights." },
      { question: "¿Cómo funcionan mensual, anual y vitalicio?", answer: "Mensual y anual forman parte del plan flexible. Vitalicio es el pago único. Ambos desbloquean la misma stack premium de Glitch." },
      { question: "¿Prometéis payouts o ganancias?", answer: "No. Prometemos herramientas serias para disciplina de riesgo y ejecución. Los resultados dependen del trader y del mercado." },
    ],
  },
  pricing: {
    metadataTitle: "Precios Glitch - Gratis, Mensual o Anual y Acceso Vitalicio",
    metadataDescription: "Compara los precios de Glitch para prop traders: empieza gratis, pasa a premium mensual o anual, o asegura acceso vitalicio.",
    badge: "Precios",
    title: "Precios directos para operadores serios.",
    lead: "Tres planes. Diferencias claras. El gratis te da guardrails. Mensual / Anual te da la stack premium completa con facturación flexible. Vitalicio te da esa misma stack sin cobro recurrente.",
    memberHubLead: "Después del checkout,",
    memberHubBlurb: "es donde viven las descargas, el onboarding, las actualizaciones y la activación.",
    upgradeTitle: "Qué cambia cuando haces upgrade",
    upgradeLead: "El salto de Gratis a pago no es cosmético. Es el punto donde Glitch se convierte en una capa operativa completa para trading serio con varias cuentas.",
    freeFoundationTitle: "Base gratuita",
    freeBestForLabel: "Mejor para",
    freeBestForBody: "Traders que validan el flujo, construyen disciplina y protegen cuentas antes de escalar.",
    paidUnlocksTitle: "Todo plan de pago desbloquea",
    paidBestForLabel: "Mejor para",
    paidBestForBody: "Traders con más cuentas, análisis más profundo y ciclos de revisión más rápidos.",
    paidNote: "Mensual, anual y vitalicio desbloquean la misma stack premium. Solo cambia el modelo de cobro.",
    accessTitle: "Cómo funciona el acceso",
    upgradeFlowTitle: "Flujo de upgrade",
    upgradeFlowSteps: [
      "Elige Gratis, Mensual / Anual o Vitalicio.",
      "Completa el checkout y abre Member Hub.",
      "Descarga la última versión y activa tu licencia.",
      "Abre Glitch y configura tu flujo.",
      "Revisa reglas, replicación y tu setup diario.",
    ],
    roadmapTitle: "Roadmap",
    roadmapItems: [
      "Marketplace para indicadores de terceros",
      "Marketplace para estrategias de terceros",
      "Ofertas integradas de prop firms asociadas",
      "Automatizaciones y flujos ampliados del asistente de trading",
    ],
    faqTitle: "Preguntas frecuentes",
    promoCodePolicy: "Política de cupones: un cupón por pedido. Los detalles de atribución están en Afiliados y Términos.",
    faqItems: [
      { question: "¿Cuál es la diferencia entre Mensual / Anual y Vitalicio?", answer: "El producto premium es el mismo. Mensual / Anual ofrece flexibilidad de cobro. Vitalicio te da la misma stack premium con un solo pago y sin recurrencia." },
      { question: "¿Puedo empezar gratis y pasar a pago después?", answer: "Sí. Empieza gratis, valida el encaje y haz upgrade cuando el flujo haya ganado un papel mayor en tu operación." },
      { question: "¿Todos los planes de pago incluyen la stack premium completa?", answer: "Sí. Mensual, anual y vitalicio desbloquean la misma stack premium de cumplimiento, replicación, análisis e insights." },
      { question: "¿Puedo usar mis indicadores y automatizaciones?", answer: "Sí. Glitch está diseñado para sentarse encima de tu setup actual como capa de riesgo y ejecución." },
      { question: "¿Glitch sirve para muchas cuentas?", answer: "Sí. El acceso de pago soporta hasta 10 masters o grupos y hasta 100 seguidores por grupo para operaciones serias." },
      { question: "¿Dónde ocurren la descarga y la activación?", answer: "En Member Hub. Allí encuentras la build más reciente, onboarding, actualizaciones y guía de activación." },
      { question: "¿Los cupones se acumulan?", answer: "No. Un cupón por pedido." },
      { question: "¿El marketplace está en camino?", answer: "Sí. El roadmap incluye indicadores de terceros, estrategias y ofertas de prop firms en un marketplace unificado." },
    ],
  },
  affiliate: {
    metadataTitle: "Programa de Afiliados Glitch - Comisiones para Creadores y Socios",
    metadataDescription: "Únete al programa de afiliados de Glitch y consulta comisiones, reglas de promo, atribución y acceso a tus enlaces.",
    badge: "Programa de afiliados Glitch",
    title: "Promociona un producto serio. Gana una comisión seria.",
    lead: "Si a tu audiencia le importan la disciplina de riesgo, el cumplimiento en prop firms y la longevidad de cuenta, este programa está hecho para ella.",
    commissionTitle: "Modelo de comisión",
    commissionBullets: [
      "20% de comisión recurrente en suscripciones activas",
      "Atribución por código promocional o último clic válido",
      "Sin comisión en el plan Free / Lite",
    ],
    commissionNote: "La comisión sigue pagándose mientras la membresía permanezca activa.",
    promoTiersTitle: "Niveles promocionales",
    promoTiersBullets: [
      "Promo pública: hasta 20% off",
      "Creadores seleccionados: hasta 50% off",
      "Campañas exclusivas: hasta 100% off",
    ],
    rulesTitle: "Reglas del programa",
    rulesBullets: [
      "No se acumulan promos",
      "No se permiten auto-referidos",
      "Código único + UTM obligatorios para atribución limpia",
      "Sin tráfico falso, cookie stuffing ni afirmaciones engañosas",
    ],
    rulesNote: "Las infracciones pueden llevar a descalificación inmediata, reversión de payouts y expulsión permanente.",
    dashboardCta: "Panel de afiliado",
    productPageCta: "Página del producto",
    dashboardNote: "Esto abre el panel de afiliados de Whop, donde las ofertas aprobadas muestran tu enlace único y los recursos disponibles.",
    termsLead: "La participación en el programa de afiliados está sujeta a nuestros",
    termsLink: "Términos del Servicio",
    termsTail: "Los términos de comisión, timing de payout y atribución quedan a nuestro criterio y pueden cambiar; consulta los Términos para ver límites de responsabilidad y demás condiciones legales.",
  },
};

const fr: MarketingContent = {
  home: {
    featurePills: ["Glitch Score", "Couche de conformité", "Contrôle de réplication", "Analyse temps réel", "Performance + Insights"],
    faqItems: [
      { question: "Qu’est-ce que Glitch exactement ?", answer: "Glitch est un assistant de trading axé risque pour NinjaTrader. Il centralise conformité, réplication, analyse et revue de performance dans une seule couche opérationnelle." },
      { question: "Glitch est-il un bot de trading automatique ?", answer: "Non. Vous gardez le contrôle de la stratégie et de l’exécution. Glitch aide à renforcer le risque et la qualité des décisions avant qu’une erreur opérationnelle ne devienne un dommage de compte." },
      { question: "Puis-je garder mes indicateurs et stratégies ?", answer: "Oui. Vous pouvez conserver vos indicateurs, stratégies automatiques et bots. Glitch complète votre workflow ; il ne le remplace pas." },
      { question: "Glitch fonctionne-t-il avec différents modèles de prop firm ?", answer: "Oui. Glitch est conçu pour des workflows cross-prop, avec des cadres de règles préchargés et un comportement de conformité configurable." },
      { question: "Glitch peut-il gérer beaucoup de comptes ?", answer: "L’accès payant prend en charge jusqu’à 10 masters ou groupes et jusqu’à 100 followers par groupe, pour des opérations multi-comptes sérieuses." },
      { question: "Qu’est-ce que Glitch Score ?", answer: "Glitch Score est la couche composite de signal de Glitch. Elle condense le contexte sur plusieurs unités de temps pour une lecture plus rapide et plus structurée." },
      { question: "Comment fonctionnent les plans payants ?", answer: "Choisissez mensuel ou annuel pour une facturation flexible, ou à vie en paiement unique. Après le checkout, Member Hub gère téléchargement, mises à jour et activation." },
      { question: "Un marketplace arrive-t-il ?", answer: "Oui. La feuille de route inclut un marketplace pour des indicateurs tiers, des stratégies et des offres de prop firms partenaires." },
      { question: "Glitch garantit-il des profits ?", answer: "Non. Glitch améliore la qualité du process et la discipline de risque. Les résultats dépendent toujours de la stratégie et de l’exécution du trader." },
    ],
    activationSteps: [
      "Choisissez l’accès Gratuit, Mensuel / Annuel ou À vie.",
      "Ouvrez Member Hub et suivez Start Here.",
      "Téléchargez et installez la dernière version de Glitch.",
      "Ouvrez New > Glitch dans NinjaTrader.",
      "Collez votre clé dans Settings puis cliquez sur Validate License.",
    ],
    dailySteps: [
      "Passez en revue risque, alertes et posture du compte.",
      "Validez réplication et conformité avant l’ouverture.",
      "Lisez Glitch Score et le contexte macro avant d’exécuter.",
      "Journalisez les résultats et revoyez les métriques après la clôture.",
      "Itérez le process chaque semaine. Les pros révisent ; les amateurs réagissent.",
    ],
    premiumCheckoutNote: "Choisissez mensuel, annuel ou à vie au checkout.",
  },
  offer: {
    metadataTitle: "Offre Glitch - Assistant de Trading Axé Risque pour NinjaTrader",
    metadataDescription: "Explorez l’offre Glitch : conformité, contrôle de réplication, analytics Glitch Score et montée en charge premium pour prop traders.",
    badge: "L’offre Glitch",
    title: "Conçu pour les traders qui traitent cela comme un business",
    lead: "Si vous en avez assez des violations de règles évitables, du chaos de réplication et d’une exécution sans contexte, voici la couche opérationnelle qui manquait.",
    sublead: "Glitch définit une nouvelle catégorie : l’assistant de trading axé risque pour les prop traders qui opèrent sérieusement, pas pour les setups jouets.",
    alreadyJoinedLabel: "Déjà inscrit ?",
    costTitle: "Le coût des erreurs évitables est brutal",
    costParagraphs: [
      "Un process cassé peut réinitialiser une évaluation, mettre en pause un compte payout ou entamer la confiance pendant des semaines. Une grande partie de cela s’évite avec une couche opérationnelle solide.",
      "Glitch est conçu pour rendre la bonne action plus facile et la mauvaise plus difficile. C’est ainsi que se construisent des opérations durables.",
    ],
    startTitle: "Commencez gratuit. Passez au niveau supérieur quand le business case est clair.",
    freeEyebrow: "Commencer gratuitement",
    freeTitle: "Protection centrale du compte",
    freeBestForLabel: "Idéal pour",
    freeBestForBody: "Les traders qui valident le workflow, construisent leur discipline et protègent leurs comptes avant de scaler.",
    proEyebrow: "Go Pro",
    proTitle: "Échelle, profondeur et précision",
    proBestForLabel: "Idéal pour",
    proBestForBody: "Les traders qui gèrent plus de comptes, veulent plus de profondeur d’analyse et des boucles de revue plus rapides.",
    freeHighlights: [
      "Réplication manuelle + automatique",
      "Conformité + règles de firme",
      "1 master + 2 followers",
      "Indicateurs de risque",
      "Replicate + Flatten All",
      "Couche centrale de l’assistant",
    ],
    paidHighlights: [
      "10 groupes + 100 followers chacun",
      "Glitch Score sur 1m, 5m, 15m et 60m",
      "Journal, Metrics + Insights",
      "Contexte technique, macro et sentiment",
      "Données enrichies Nasdaq + Mag7",
      "Utilisez vos indicateurs et bots",
    ],
    engineTitle: "Ce qui fait tourner Glitch",
    engineCards: [
      { title: "Conformité opérationnelle", body: "Une logique orientée règles pour garder les comptes dans les limites de la firme avant qu’une violation ne survienne." },
      { title: "Système de réplication", body: "Synchronisation multi-comptes, échelle de followers et surfaces de contrôle pensées pour des stacks sérieuses." },
      { title: "Glitch Score", body: "Un contexte directionnel structuré sur plusieurs unités de temps pour réduire le bruit et les entrées émotionnelles." },
      { title: "Moteur Journal + Insights", body: "Des boucles de feedback qui transforment des résultats aléatoires en amélioration mesurable du process." },
      { title: "Stack macro et sentiment", body: "Nasdaq, Mag7, macro et actualité dans une seule vue pour que le contexte cesse d’être secondaire." },
      { title: "Workflow ouvert", body: "Apportez vos indicateurs, automatisations et stratégies. Glitch centralise et durcit l’opération." },
    ],
    pricingTitle: "Le pricing dans une vue claire",
    pricingLead: "Gratuit pour démarrer. Mensuel / Annuel pour un premium flexible. À vie pour ceux qui savent déjà que Glitch doit faire partie de la stack.",
    memberHubBlurb: "est l’endroit où vivent vos téléchargements, mises à jour et étapes d’onboarding.",
    faqTitle: "FAQ",
    faqItems: [
      { question: "Pourquoi appeler Glitch un assistant de trading ?", answer: "Parce qu’il assiste chaque étape du process opérationnel : risque, discipline de réplication, analyse du contexte et revue de performance." },
      { question: "Glitch remplace-t-il ma stratégie ?", answer: "Non. Votre stratégie reste la vôtre. Glitch améliore la qualité d’exécution et la discipline de risque autour de cette stratégie." },
      { question: "Puis-je utiliser Glitch avec des systèmes automatiques ?", answer: "Oui. Glitch est conçu pour fonctionner avec des workflows manuels comme automatiques, tout en gardant des garde-fous." },
      { question: "Qu’est-ce qui rend l’accès payant intéressant ?", answer: "Plus d’échelle, plus de profondeur et plus de vitesse : davantage de comptes, un contexte plus fort et une boucle Journal, Metrics et Insights plus sérieuse." },
      { question: "Comment fonctionnent mensuel, annuel et à vie ?", answer: "Mensuel et annuel relèvent du plan flexible. À vie est l’option paiement unique. Les deux débloquent la même stack premium." },
      { question: "Promettez-vous des payouts ou des profits ?", answer: "Non. Nous promettons des outils sérieux pour la discipline de risque et l’exécution. Les résultats dépendent du trader et du marché." },
    ],
  },
  pricing: {
    metadataTitle: "Tarifs Glitch - Gratuit, Mensuel ou Annuel, et Accès à Vie",
    metadataDescription: "Comparez les tarifs Glitch pour prop traders : démarrez gratuitement, passez au premium mensuel ou annuel, ou sécurisez l’accès à vie.",
    badge: "Tarifs",
    title: "Des tarifs clairs pour des opérateurs sérieux.",
    lead: "Trois plans. Des arbitrages clairs. Le gratuit vous donne les garde-fous. Le mensuel / annuel vous donne la stack premium complète avec une facturation flexible. L’accès à vie vous donne cette même stack sans récurrence.",
    memberHubLead: "Après le checkout,",
    memberHubBlurb: "est l’endroit où vivent téléchargements, onboarding, mises à jour et activation.",
    upgradeTitle: "Ce qui change quand vous passez au niveau supérieur",
    upgradeLead: "Le passage du Gratuit au payant n’est pas cosmétique. C’est le moment où Glitch devient une couche opérationnelle complète pour le trading multi-comptes sérieux.",
    freeFoundationTitle: "Base gratuite",
    freeBestForLabel: "Idéal pour",
    freeBestForBody: "Les traders qui valident le workflow, construisent leur discipline et protègent leurs comptes avant de scaler.",
    paidUnlocksTitle: "Chaque plan payant débloque",
    paidBestForLabel: "Idéal pour",
    paidBestForBody: "Les traders qui gèrent plus de comptes, plus de profondeur d’analyse et des boucles de revue plus rapides.",
    paidNote: "Mensuel, annuel et à vie débloquent la même stack premium. Seul le modèle de facturation change.",
    accessTitle: "Fonctionnement de l’accès",
    upgradeFlowTitle: "Parcours d’upgrade",
    upgradeFlowSteps: [
      "Choisissez Gratuit, Mensuel / Annuel ou À vie.",
      "Terminez le checkout puis ouvrez Member Hub.",
      "Téléchargez la dernière version et activez votre licence.",
      "Lancez Glitch et configurez votre workflow.",
      "Revoyez règles, réplication et setup quotidien.",
    ],
    roadmapTitle: "Feuille de route",
    roadmapItems: [
      "Marketplace pour indicateurs tiers",
      "Marketplace pour stratégies tierces",
      "Offres intégrées de prop firms partenaires",
      "Automatisations et workflows étendus de l’assistant de trading",
    ],
    faqTitle: "FAQ",
    promoCodePolicy: "Règle promo : un code par commande. Les détails d’attribution figurent sur les pages Affiliés et Conditions.",
    faqItems: [
      { question: "Quelle différence entre Mensuel / Annuel et l’accès à vie ?", answer: "Le produit premium est le même. Mensuel / Annuel apporte de la flexibilité de facturation. L’accès à vie donne cette même stack premium en paiement unique, sans récurrence." },
      { question: "Puis-je commencer gratuit et upgrader plus tard ?", answer: "Oui. Commencez gratuitement, validez l’adéquation, puis upgradez quand le workflow a gagné une place plus importante dans votre opération." },
      { question: "Tous les plans payants incluent-ils la stack premium complète ?", answer: "Oui. Mensuel, annuel et à vie débloquent la même stack premium de conformité, réplication, analytics et insights." },
      { question: "Puis-je utiliser mes propres indicateurs et automatismes ?", answer: "Oui. Glitch est conçu pour se poser au-dessus de votre setup actuel comme couche risque et exécution." },
      { question: "Glitch convient-il aux opérations avec beaucoup de comptes ?", answer: "Oui. L’accès payant prend en charge jusqu’à 10 masters ou groupes et jusqu’à 100 followers par groupe." },
      { question: "Où se font le téléchargement et l’activation ?", answer: "Dans Member Hub. C’est là que se trouvent la dernière build, l’onboarding, les mises à jour et les étapes d’activation." },
      { question: "Les codes promo se cumulent-ils ?", answer: "Non. Un code promo par commande." },
      { question: "Le marketplace arrive-t-il ?", answer: "Oui. La feuille de route inclut des indicateurs tiers, des stratégies et des offres de prop firms dans une place de marché unifiée." },
    ],
  },
  affiliate: {
    metadataTitle: "Programme d’Affiliation Glitch - Commissions pour Créateurs et Partenaires",
    metadataDescription: "Rejoignez le programme d’affiliation Glitch et consultez commissions, règles promo, attribution et accès à vos liens.",
    badge: "Programme d’affiliation Glitch",
    title: "Promouvez un produit sérieux. Gagnez une commission sérieuse.",
    lead: "Si votre audience se soucie de discipline de risque, de conformité prop firm et de longévité des comptes, ce programme a été conçu pour elle.",
    commissionTitle: "Modèle de commission",
    commissionBullets: [
      "20 % de commission récurrente sur les abonnements actifs",
      "Attribution par code promo ou dernier clic valide",
      "Aucune commission sur le plan Free / Lite",
    ],
    commissionNote: "La commission continue tant que l’adhésion reste active.",
    promoTiersTitle: "Niveaux promotionnels",
    promoTiersBullets: [
      "Promo publique : jusqu’à 20 % off",
      "Créateurs sélectionnés : jusqu’à 50 % off",
      "Campagnes exclusives : jusqu’à 100 % off",
    ],
    rulesTitle: "Règles du programme",
    rulesBullets: [
      "Pas de cumul de promos",
      "Pas d’auto-référencement",
      "Code unique + UTM requis pour une attribution propre",
      "Pas de faux trafic, de cookie stuffing ou d’allégations trompeuses",
    ],
    rulesNote: "Les violations peuvent entraîner une disqualification immédiate, l’annulation du payout et une exclusion permanente.",
    dashboardCta: "Dashboard affilié",
    productPageCta: "Page produit",
    dashboardNote: "Ceci ouvre le dashboard affilié Whop, où les offres approuvées exposent votre lien unique et vos ressources.",
    termsLead: "La participation au programme d’affiliation est soumise à nos",
    termsLink: "Conditions d’utilisation",
    termsTail: "Les conditions de commission, le calendrier de payout et l’attribution restent à notre discrétion et peuvent changer ; voir les Conditions pour les limites de responsabilité et les autres termes légaux.",
  },
};

const ru: MarketingContent = {
  home: {
    featurePills: ["Glitch Score", "Слой соответствия", "Контроль репликации", "Аналитика в реальном времени", "Performance + Insights"],
    faqItems: [
      { question: "Что такое Glitch?", answer: "Glitch — это риск-ориентированный торговый ассистент для NinjaTrader. Он объединяет контроль правил, репликацию, аналитику и обзор результатов в одном операционном слое." },
      { question: "Glitch — это автоматический торговый бот?", answer: "Нет. Стратегия и исполнение остаются под вашим контролем. Glitch помогает усиливать риск-дисциплину до того, как операционная ошибка повредит счету." },
      { question: "Могу ли я использовать свои индикаторы и стратегии?", answer: "Да. Вы можете оставить свои индикаторы, автоматические стратегии и ботов. Glitch дополняет ваш процесс, а не заменяет его." },
      { question: "Работает ли Glitch с разными prop firm моделями?", answer: "Да. Glitch рассчитан на cross-prop workflow, с предзагруженными каркасами правил и настраиваемым поведением по соблюдению ограничений." },
      { question: "Справится ли Glitch с большим числом счетов?", answer: "Платный доступ поддерживает до 10 master-групп и до 100 follower-счетов на группу — для серьезных многоаккаунтных операций." },
      { question: "Что такое Glitch Score?", answer: "Glitch Score — это составной сигнальный слой Glitch. Он собирает контекст из нескольких таймфреймов, чтобы читать рынок быстрее и структурированнее." },
      { question: "Как работают платные планы?", answer: "Можно выбрать месячную или годовую оплату, либо пожизненный доступ одним платежом. После checkout загрузка, обновления и активация происходят через Member Hub." },
      { question: "Появится ли marketplace?", answer: "Да. В roadmap входят marketplace для сторонних индикаторов, стратегий и партнерских prop firm предложений." },
      { question: "Гарантирует ли Glitch прибыль?", answer: "Нет. Glitch улучшает качество процесса и риск-дисциплину. Итог всегда зависит от стратегии и исполнения трейдера." },
    ],
    activationSteps: [
      "Выберите Бесплатно, Месяц / Год или Lifetime.",
      "Откройте Member Hub и следуйте Start Here.",
      "Скачайте и установите последнюю сборку Glitch.",
      "Откройте New > Glitch в NinjaTrader.",
      "Вставьте ключ в Settings и нажмите Validate License.",
    ],
    dailySteps: [
      "Проверьте риск-статус, предупреждения и состояние счетов.",
      "Подтвердите репликацию и соблюдение правил до открытия сессии.",
      "Оцените Glitch Score и макроконтекст до исполнения.",
      "Занесите результаты в журнал и пересмотрите метрики после закрытия.",
      "Итерируйте процесс еженедельно. Профессионалы пересматривают, любители реагируют.",
    ],
    premiumCheckoutNote: "На checkout можно выбрать месячный, годовой или lifetime доступ.",
  },
  offer: {
    metadataTitle: "Предложение Glitch - Риск-ориентированный Торговый Ассистент для NinjaTrader",
    metadataDescription: "Изучите предложение Glitch: соблюдение правил, контроль репликации, аналитика Glitch Score и premium-масштабирование для prop-трейдеров.",
    badge: "Предложение Glitch",
    title: "Создано для трейдеров, которые относятся к этому как к бизнесу",
    lead: "Если вам надоели предотвратимые нарушения правил, хаос репликации и исполнение без контекста, это тот операционный слой, которого не хватало.",
    sublead: "Glitch формирует новую категорию: риск-ориентированный торговый ассистент для prop-трейдеров, которые ведут реальную операцию, а не игрушечный сетап.",
    alreadyJoinedLabel: "Уже с нами?",
    costTitle: "Цена предотвратимых ошибок слишком высока",
    costParagraphs: [
      "Один сломанный процесс может обнулить evaluation, остановить payout-счет или подорвать уверенность на недели. Значительная часть этого уходит, когда операционный слой выстроен правильно.",
      "Glitch спроектирован так, чтобы правильное действие было проще, а неправильное — сложнее. Именно так строятся устойчивые торговые операции.",
    ],
    startTitle: "Начните бесплатно. Обновляйтесь, когда бизнес-кейс станет очевидным.",
    freeEyebrow: "Начать бесплатно",
    freeTitle: "Базовая защита счета",
    freeBestForLabel: "Лучше всего для",
    freeBestForBody: "Трейдеров, которые проверяют workflow, выстраивают дисциплину и защищают счета до масштабирования.",
    proEyebrow: "Go Pro",
    proTitle: "Масштаб, глубина и точность",
    proBestForLabel: "Лучше всего для",
    proBestForBody: "Трейдеров с большим числом счетов, более глубокой аналитикой и быстрыми циклами пересмотра.",
    freeHighlights: [
      "Ручная и автоматическая репликация",
      "Соблюдение правил и лимитов фирмы",
      "1 master + 2 followers",
      "Индикаторы риска",
      "Replicate + Flatten All",
      "Центральный слой ассистента",
    ],
    paidHighlights: [
      "10 групп + 100 followers в каждой",
      "Glitch Score на 1m, 5m, 15m и 60m",
      "Journal, Metrics + Insights",
      "Технический, макро- и sentiment-контекст",
      "Обогащенные данные Nasdaq + Mag7",
      "Используйте свои индикаторы и ботов",
    ],
    engineTitle: "Что делает Glitch сильным",
    engineCards: [
      { title: "Контроль соблюдения правил", body: "Логика, ориентированная на правила, чтобы счета оставались в рамках ограничений фирмы до нарушения." },
      { title: "Система репликации", body: "Мультиаккаунтная синхронизация, масштаб followers и поверхности управления для серьезных account stacks." },
      { title: "Glitch Score", body: "Структурированный направленный контекст по нескольким таймфреймам, который снижает шум и эмоциональные входы." },
      { title: "Journal + Insights engine", body: "Цикл обратной связи, превращающий случайные результаты в измеримое улучшение процесса." },
      { title: "Макро и sentiment stack", body: "Nasdaq, Mag7, macro и новости в одной картине, чтобы контекст перестал быть второстепенным." },
      { title: "Открытая философия workflow", body: "Используйте свои индикаторы, автоматизацию и стратегии. Glitch централизует и укрепляет операцию." },
    ],
    pricingTitle: "Тарифы в одном чистом обзоре",
    pricingLead: "Бесплатный доступ помогает стартовать. Monthly / Annual дает гибкий premium-доступ. Lifetime — постоянное место для тех, кто уже знает, что Glitch должен быть частью stack.",
    memberHubBlurb: "— место, где находятся ваши загрузки, обновления и шаги onboarding.",
    faqTitle: "FAQ",
    faqItems: [
      { question: "Почему Glitch называется торговым ассистентом?", answer: "Потому что он помогает на каждом этапе операционного процесса: риск-контроль, дисциплина репликации, анализ контекста и обзор результатов." },
      { question: "Glitch заменяет мою стратегию?", answer: "Нет. Стратегия остается вашей. Glitch повышает качество исполнения и риск-дисциплину вокруг нее." },
      { question: "Можно ли использовать Glitch с автоматическими системами?", answer: "Да. Glitch создан для работы рядом с ручными и автоматическими workflow, сохраняя гибкость и guardrails." },
      { question: "Что делает платный доступ ценным?", answer: "Масштаб, глубина и скорость: больше счетов, сильнее контекст и более серьезный feedback loop через Journal, Metrics и Insights." },
      { question: "Как работают месячный, годовой и lifetime доступ?", answer: "Месячный и годовой относятся к гибкому платному плану. Lifetime — это разовый платеж. Оба варианта открывают одну и ту же premium stack." },
      { question: "Вы обещаете payouts или прибыль?", answer: "Нет. Мы обещаем серьезные инструменты для риск-дисциплины и исполнения. Результаты зависят от трейдера и рынка." },
    ],
  },
  pricing: {
    metadataTitle: "Цены Glitch - Бесплатно, Месяц или Год, и Lifetime Доступ",
    metadataDescription: "Сравните цены Glitch для prop-трейдеров: начните бесплатно, перейдите на месячный или годовой premium, либо зафиксируйте lifetime доступ.",
    badge: "Цены",
    title: "Прямые цены для серьезных операторов.",
    lead: "Три плана. Понятные различия. Бесплатный дает guardrails. Monthly / Annual открывает полную premium stack с гибкой оплатой. Lifetime открывает ту же stack без регулярных списаний.",
    memberHubLead: "После checkout",
    memberHubBlurb: "находятся загрузки, onboarding, обновления и шаги активации.",
    upgradeTitle: "Что меняется после апгрейда",
    upgradeLead: "Переход с Free на платный уровень — не косметика. Именно здесь Glitch становится полноценным операционным слоем для серьезной многоаккаунтной торговли.",
    freeFoundationTitle: "Бесплатная база",
    freeBestForLabel: "Лучше всего для",
    freeBestForBody: "Трейдеров, которые проверяют workflow, строят дисциплину и защищают счета до масштабирования.",
    paidUnlocksTitle: "Каждый платный план открывает",
    paidBestForLabel: "Лучше всего для",
    paidBestForBody: "Трейдеров с большим числом счетов, более глубокой аналитикой и быстрыми циклами обзора.",
    paidNote: "Месячный, годовой и lifetime доступ открывают одну и ту же premium stack. Меняется только модель оплаты.",
    accessTitle: "Как работает доступ",
    upgradeFlowTitle: "Путь апгрейда",
    upgradeFlowSteps: [
      "Выберите Free, Monthly / Annual или Lifetime.",
      "Завершите checkout и откройте Member Hub.",
      "Скачайте последнюю сборку и активируйте лицензию.",
      "Запустите Glitch и настройте workflow.",
      "Пересмотрите правила, репликацию и ежедневный setup.",
    ],
    roadmapTitle: "Roadmap",
    roadmapItems: [
      "Marketplace для сторонних индикаторов",
      "Marketplace для сторонних стратегий",
      "Интегрированные предложения партнерских prop firms",
      "Расширенные автоматизации и workflow торгового ассистента",
    ],
    faqTitle: "FAQ",
    promoCodePolicy: "Правило промокодов: один код на заказ. Подробности атрибуции описаны на страницах Affiliate и Terms.",
    faqItems: [
      { question: "В чем разница между Monthly / Annual и Lifetime?", answer: "Премиальный продукт один и тот же. Monthly / Annual дает гибкость оплаты. Lifetime открывает ту же premium stack одним платежом без рекуррентных списаний." },
      { question: "Могу ли я начать бесплатно и перейти позже?", answer: "Да. Начните бесплатно, проверьте fit и обновляйтесь, когда workflow займет более важное место в операции." },
      { question: "Все платные планы включают полную premium stack?", answer: "Да. Месячный, годовой и lifetime планы открывают одну и ту же premium stack по соответствию, репликации, аналитике и insights." },
      { question: "Могу ли я использовать свои индикаторы и автоматизацию?", answer: "Да. Glitch спроектирован как риск- и execution-layer поверх вашего текущего setup." },
      { question: "Подходит ли Glitch для большого числа счетов?", answer: "Да. Платный доступ поддерживает до 10 masters или groups и до 100 followers на группу." },
      { question: "Где происходят загрузка и активация?", answer: "В Member Hub. Там находятся последняя сборка, onboarding, обновления и шаги активации." },
      { question: "Промокоды суммируются?", answer: "Нет. Один промокод на один заказ." },
      { question: "Marketplace уже в пути?", answer: "Да. В roadmap входят сторонние индикаторы, стратегии и предложения prop firms в едином marketplace." },
    ],
  },
  affiliate: {
    metadataTitle: "Партнерская Программа Glitch - Комиссии для Создателей и Партнеров",
    metadataDescription: "Присоединяйтесь к партнерской программе Glitch и изучите комиссии, promo-правила, атрибуцию и доступ к вашим ссылкам.",
    badge: "Партнерская программа Glitch",
    title: "Продвигайте серьезный продукт. Зарабатывайте серьезную комиссию.",
    lead: "Если вашей аудитории важны риск-дисциплина, соответствие правилам prop firms и долговечность счета, эта программа создана для нее.",
    commissionTitle: "Модель комиссии",
    commissionBullets: [
      "20% рекуррентной комиссии с активных подписок",
      "Атрибуция по промокоду или последнему валидному клику",
      "Нет комиссии на план Free / Lite",
    ],
    commissionNote: "Комиссия выплачивается, пока membership остается активным.",
    promoTiersTitle: "Промо-уровни",
    promoTiersBullets: [
      "Публичное promo: до 20% off",
      "Выбранные creators: до 50% off",
      "Эксклюзивные кампании: до 100% off",
    ],
    rulesTitle: "Правила программы",
    rulesBullets: [
      "Нельзя складывать promo",
      "Никаких self-referrals",
      "Уникальный код + UTM обязательны для чистой атрибуции",
      "Никакого фейкового трафика, cookie stuffing или вводящих в заблуждение заявлений",
    ],
    rulesNote: "Нарушения могут привести к немедленной дисквалификации, отмене payout и постоянному удалению.",
    dashboardCta: "Партнерская панель",
    productPageCta: "Страница продукта",
    dashboardNote: "Это открывает affiliate dashboard Whop, где одобренные офферы показывают вашу уникальную ссылку и материалы.",
    termsLead: "Участие в партнерской программе регулируется нашими",
    termsLink: "Условиями использования",
    termsTail: "Условия комиссии, сроки payout и атрибуция остаются на нашем усмотрении и могут меняться; см. Terms для ограничений ответственности и других юридических условий.",
  },
};

const zh: MarketingContent = {
  home: {
    featurePills: ["Glitch Score", "合规层", "复制控制", "实时分析", "绩效 + Insights"],
    faqItems: [
      { question: "Glitch 到底是什么？", answer: "Glitch 是面向 NinjaTrader 的风险优先交易助手。它把合规、复制、分析和绩效复盘集中到一个操作层。"},
      { question: "Glitch 是自动交易机器人吗？", answer: "不是。策略与执行仍由你控制。Glitch 的作用是在可避免的操作失误伤到账户之前，先把风险和流程守住。"},
      { question: "我还能继续用自己的指标和策略吗？", answer: "可以。你可以继续使用自己的指标、自动化策略和机器人。Glitch 是补强你的流程，不是替代它。"},
      { question: "Glitch 能适配不同的 prop firm 模式吗？", answer: "可以。Glitch 面向 cross-prop 工作流设计，提供预置规则框架与可配置的合规行为。"},
      { question: "Glitch 能支撑很多账户吗？", answer: "付费访问支持最多 10 个 master 或 group，每组最多 100 个 follower，适合严肃的多账户运营。"},
      { question: "什么是 Glitch Score？", answer: "Glitch Score 是 Glitch 的复合信号层。它把多个时间框架的上下文汇总起来，让你更快、更有结构地读盘。"},
      { question: "付费方案怎么运作？", answer: "你可以选择月付、年付，或一次性终身访问。完成 checkout 后，下载、更新和激活都在 Member Hub 中完成。"},
      { question: "以后会有 marketplace 吗？", answer: "会。路线图里包含第三方指标、策略以及合作 prop firm 的 marketplace。"},
      { question: "Glitch 保证盈利吗？", answer: "不保证。Glitch 只是提升流程质量和风险纪律，最终结果仍取决于你的策略与执行。"},
    ],
    activationSteps: [
      "选择免费、月付 / 年付或终身访问。",
      "打开 Member Hub 并按照 Start Here 操作。",
      "下载并安装最新版 Glitch。",
      "在 NinjaTrader 中打开 New > Glitch。",
      "在 Settings 中粘贴密钥并点击 Validate License。",
    ],
    dailySteps: [
      "检查风险状态、警告数量和账户姿态。",
      "在开盘前确认复制与合规状态。",
      "执行前先阅读 Glitch Score 与宏观上下文。",
      "收盘后记录结果并复盘指标。",
      "按周迭代流程。专业交易员复盘，业余交易员只会反应。",
    ],
    premiumCheckoutNote: "在 checkout 中可选择月付、年付或终身访问。",
  },
  offer: {
    metadataTitle: "Glitch 产品方案 - 面向 NinjaTrader 的风险优先交易助手",
    metadataDescription: "查看 Glitch 的产品方案：合规执行、复制控制、Glitch Score 分析，以及面向 prop 交易员的高级扩展能力。",
    badge: "Glitch 方案",
    title: "为把交易当作事业的人而打造",
    lead: "如果你已经厌倦了可避免的规则违规、复制混乱和缺乏上下文的执行，这就是你一直缺少的操作层。",
    sublead: "Glitch 正在定义一个新类别：为真正运营型 prop 交易员服务的风险优先交易助手，而不是玩具型工具。",
    alreadyJoinedLabel: "已经加入？",
    costTitle: "可避免错误的代价非常高",
    costParagraphs: [
      "一个断裂的流程就可能让 evaluation 归零、让 payout 账户暂停，或让信心受挫数周。只要操作层够强，这些事里有很大一部分本可避免。",
      "Glitch 的设计目标，是让正确动作更容易、错误动作更困难。这正是稳定交易运营的构建方式。",
    ],
    startTitle: "先免费开始。等业务价值明确后再升级。",
    freeEyebrow: "免费开始",
    freeTitle: "核心账户保护",
    freeBestForLabel: "最适合",
    freeBestForBody: "适合先验证流程、建立纪律，并在放大规模前保护账户的交易员。",
    proEyebrow: "Go Pro",
    proTitle: "规模、深度与精度",
    proBestForLabel: "最适合",
    proBestForBody: "适合管理更多账户、需要更深分析和更快复盘节奏的交易员。",
    freeHighlights: [
      "手动 + 自动复制",
      "合规与规则控制",
      "1 个 master + 2 个 follower",
      "风险控制指标",
      "Replicate + Flatten All",
      "核心助手层",
    ],
    paidHighlights: [
      "10 个 group + 每组 100 个 follower",
      "1m、5m、15m、60m 的 Glitch Score",
      "Journal、Metrics + Insights",
      "技术、宏观与情绪上下文",
      "Nasdaq + Mag7 增强数据",
      "支持自有指标与机器人",
    ],
    engineTitle: "Glitch 的核心能力",
    engineCards: [
      { title: "合规执行", body: "面向规则的逻辑，帮助账户在违规发生前保持在 firm 限制范围内。" },
      { title: "复制系统", body: "多账户同步、follower 扩展与控制界面，适合严肃的账户栈运营。" },
      { title: "Glitch Score", body: "跨多个时间框架的结构化方向上下文，减少噪音与情绪化入场。" },
      { title: "Journal + Insights 引擎", body: "把随机结果变成可度量流程改进的反馈闭环。" },
      { title: "宏观与情绪层", body: "把 Nasdaq、Mag7、宏观与新闻放在一个视图里，让上下文不再靠猜。" },
      { title: "开放式工作流", body: "继续使用你的指标、自动化与策略，Glitch 负责把操作层集中并加固。" },
    ],
    pricingTitle: "用一个清晰视图看定价",
    pricingLead: "免费版让你先起步。月付 / 年付提供灵活的高级访问。终身访问则适合已经确认 Glitch 应该长期留在 stack 里的交易员。",
    memberHubBlurb: "里提供你的下载、更新与 onboarding 步骤。",
    faqTitle: "常见问题",
    faqItems: [
      { question: "为什么把 Glitch 定义为交易助手？", answer: "因为它参与的是整个操作流程：风险控制、复制纪律、上下文分析以及绩效复盘。"},
      { question: "Glitch 会替代我的策略吗？", answer: "不会。策略仍然是你的。Glitch 负责提升围绕该策略的执行质量与风险纪律。"},
      { question: "Glitch 能和自动化系统一起用吗？", answer: "可以。Glitch 被设计为可与手动和自动化流程并行工作，同时保留 guardrails。"},
      { question: "付费访问的价值在哪里？", answer: "价值在于规模、深度与速度：更多账户、更强上下文，以及更严肃的 Journal、Metrics 和 Insights 反馈闭环。"},
      { question: "月付、年付和终身访问如何区分？", answer: "月付和年付属于灵活计费方案。终身访问是一次性付款。两者解锁的是同一套高级功能栈。"},
      { question: "你们承诺 payout 或利润吗？", answer: "不承诺。我们承诺的是严肃的风险与执行工具。结果仍取决于交易员和市场。"},
    ],
  },
  pricing: {
    metadataTitle: "Glitch 定价 - 免费、月付 / 年付与终身访问",
    metadataDescription: "比较 Glitch 面向 prop 交易员的定价：先免费开始，升级到月付或年付高级版，或直接锁定终身访问。",
    badge: "定价",
    title: "为严肃运营者准备的直接定价。",
    lead: "三个方案，权衡清晰。免费版提供 guardrails。月付 / 年付提供完整高级 stack 与灵活计费。终身访问则在同一高级 stack 下取消持续收费。",
    memberHubLead: "完成 checkout 后，",
    memberHubBlurb: "里提供下载、onboarding、更新与激活步骤。",
    upgradeTitle: "升级后有什么变化",
    upgradeLead: "从免费到付费的变化并不是表面升级，而是 Glitch 真正成为严肃多账户交易操作层的节点。",
    freeFoundationTitle: "免费基础",
    freeBestForLabel: "最适合",
    freeBestForBody: "适合先验证流程、建立纪律，并在扩张前保护账户的交易员。",
    paidUnlocksTitle: "每个付费方案都会解锁",
    paidBestForLabel: "最适合",
    paidBestForBody: "适合管理更多账户、做更深分析并保持更快复盘节奏的交易员。",
    paidNote: "月付、年付和终身访问解锁的是同一套高级功能栈，区别只在计费方式。",
    accessTitle: "访问方式",
    upgradeFlowTitle: "升级流程",
    upgradeFlowSteps: [
      "选择免费、月付 / 年付或终身访问。",
      "完成 checkout 后进入 Member Hub。",
      "下载最新版本并激活许可证。",
      "启动 Glitch 并配置你的 workflow。",
      "检查规则、复制与每日操作设置。",
    ],
    roadmapTitle: "路线图",
    roadmapItems: [
      "第三方指标 marketplace",
      "第三方策略 marketplace",
      "合作 prop firm 一体化方案",
      "更丰富的交易助手自动化与 workflow",
    ],
    faqTitle: "常见问题",
    promoCodePolicy: "优惠码规则：每笔订单只可使用一个优惠码。归因细节请查看 Affiliate 与 Terms 页面。",
    faqItems: [
      { question: "月付 / 年付与终身访问有什么区别？", answer: "高级产品本身相同。月付 / 年付提供计费灵活性；终身访问则以一次付款获得同样的高级功能栈。"},
      { question: "我能先免费开始再升级吗？", answer: "可以。先免费验证适配度，等 workflow 在你的运营中承担更大角色后再升级。"},
      { question: "所有付费方案都包含完整高级 stack 吗？", answer: "是的。月付、年付和终身访问都解锁相同的高级合规、复制、分析与 insights stack。"},
      { question: "我能继续使用自己的指标和自动化吗？", answer: "可以。Glitch 被设计为叠加在现有 setup 之上的风险与执行层。"},
      { question: "Glitch 适合大规模账户运营吗？", answer: "适合。付费访问支持最多 10 个 master 或 group，以及每组最多 100 个 follower。"},
      { question: "下载和激活在哪里完成？", answer: "都在 Member Hub 中完成。你会在那里找到最新 build、onboarding、更新与激活说明。"},
      { question: "优惠码可以叠加吗？", answer: "不可以。每笔订单仅可使用一个优惠码。"},
      { question: "Marketplace 是否在规划中？", answer: "是的。路线图中包括第三方指标、策略以及 prop firm 方案的统一 marketplace。"},
    ],
  },
  affiliate: {
    metadataTitle: "Glitch 联盟计划 - 面向创作者与合作伙伴的佣金方案",
    metadataDescription: "加入 Glitch 联盟计划，查看佣金、促销规则、归因方式以及你的专属链接入口。",
    badge: "Glitch 联盟计划",
    title: "推广严肃产品，赚取严肃佣金。",
    lead: "如果你的受众重视风险纪律、prop firm 合规与账户寿命，这个计划就是为他们准备的。",
    commissionTitle: "佣金模式",
    commissionBullets: [
      "活跃订阅可获得 20% 持续佣金",
      "按优惠码或最后一次有效点击归因",
      "Free / Lite 方案不产生佣金",
    ],
    commissionNote: "只要 membership 保持激活，佣金就会持续发放。",
    promoTiersTitle: "促销层级",
    promoTiersBullets: [
      "公开促销：最高 20% off",
      "精选创作者：最高 50% off",
      "专属活动：最高 100% off",
    ],
    rulesTitle: "项目规则",
    rulesBullets: [
      "促销不可叠加",
      "不允许自我推荐",
      "必须使用唯一代码 + UTM 才能实现清晰归因",
      "不得使用虚假流量、cookie stuffing 或误导性说法",
    ],
    rulesNote: "违反规则可能导致立即取消资格、回收 payout，并永久移除。",
    dashboardCta: "联盟后台",
    productPageCta: "产品页面",
    dashboardNote: "这会打开 Whop 的联盟后台，获批后的 offer 会在其中显示你的专属链接与素材。",
    termsLead: "参与联盟计划须遵守我们的",
    termsLink: "服务条款",
    termsTail: "佣金规则、payout 时间与归因方式由我们自行决定并可能调整；更多责任限制与法律条款请查看 Terms。",
  },
};

export function getMarketingContent(locale?: string): MarketingContent {
  if (locale === "pt") {
    return pt;
  }
  if (locale === "es") {
    return es;
  }
  if (locale === "fr") {
    return fr;
  }
  if (locale === "ru") {
    return ru;
  }
  if (locale === "zh") {
    return zh;
  }

  return en;
}
