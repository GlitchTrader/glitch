type LegalSection = {
  title: string;
  tone?: "warning" | "default";
  paragraphs?: string[];
  bullets?: string[];
};

type LegalPage = {
  metadataTitle: string;
  metadataDescription: string;
  title: string;
  intro: string;
  sections: LegalSection[];
};

type LegalContent = {
  shared: {
    lastUpdatedLabel: string;
    backToHome: string;
    terms: string;
    privacy: string;
    riskDisclosure: string;
  };
  terms: LegalPage;
  privacy: LegalPage;
  riskDisclosure: LegalPage;
};

const en: LegalContent = {
  shared: {
    lastUpdatedLabel: "Last updated",
    backToHome: "Back to Home",
    terms: "Terms of Service",
    privacy: "Privacy Policy",
    riskDisclosure: "Risk Disclosure",
  },
  terms: {
    metadataTitle: "Terms of Service - Glitch",
    metadataDescription:
      "Review Glitch terms of service, billing rules, acceptable use, disclaimers, and liability limits.",
    title: "Terms of Service",
    intro:
      "These terms govern your use of Glitch software, downloads, websites, APIs, and related services. By using Glitch, you agree to these terms.",
    sections: [
      {
        title: "Software and services are provided as is",
        paragraphs: [
          "Glitch is provided on an “as is” and “as available” basis, without express or implied warranties. We do not promise uninterrupted service, error-free operation, or fitness for a specific trading purpose.",
        ],
      },
      {
        title: "Trading and technology risk stay with the user",
        paragraphs: [
          "Trading involves substantial risk of loss. Software bugs, delays, outages, data issues, broker problems, and third-party failures can all affect outcomes. You remain responsible for your own decisions, account handling, and risk management.",
        ],
      },
      {
        title: "Billing and plan terms",
        bullets: [
          "Plans may include free, subscription, annual, and lifetime access.",
          "Billing cadence, renewal terms, and any trial terms are shown at checkout.",
          "Promo rules are limited to one code per order unless we state otherwise.",
        ],
      },
      {
        title: "Acceptable use",
        bullets: [
          "Do not abuse licensing, activation, checkout, or distribution mechanisms.",
          "Do not use Glitch for fraud, deceptive promotion, or policy evasion.",
          "Do not reverse engineer or misuse the product beyond what applicable law clearly permits.",
        ],
      },
      {
        title: "Prop firm responsibility",
        paragraphs: [
          "Glitch is designed to support professional prop workflows, but you are solely responsible for checking the current rules of your broker, prop firm, or funded program. We do not guarantee compliance with any firm's current or future rules.",
        ],
      },
      {
        title: "Liability, changes, and disputes",
        paragraphs: [
          "To the fullest extent allowed by law, our liability is limited to the amount you paid for the Services in the twelve months before a claim. We may update these terms and the Services over time. Continued use after an update constitutes acceptance of the revised terms.",
        ],
      },
    ],
  },
  privacy: {
    metadataTitle: "Privacy Policy - Glitch",
    metadataDescription:
      "Read how Glitch collects, uses, stores, and protects data across the website, checkout, member hub, and product services.",
    title: "Privacy Policy",
    intro:
      "We collect only the information needed to operate Glitch, provide access, support users, reduce abuse, and improve product quality.",
    sections: [
      {
        title: "Data we may collect",
        bullets: [
          "Account and contact details needed for access or support.",
          "Subscription and billing status from approved payment providers.",
          "Basic website analytics, diagnostics, and product usage signals.",
          "Support conversations and troubleshooting material you choose to send us.",
        ],
      },
      {
        title: "How we use data",
        bullets: [
          "Provision and maintain access to the Services.",
          "Process billing, entitlements, fraud prevention, and abuse controls.",
          "Improve product stability, support quality, and operational communications.",
          "Comply with legal obligations and enforce our terms.",
        ],
      },
      {
        title: "Third parties and payments",
        paragraphs: [
          "Payments are handled by approved providers such as Whop or Stripe. We do not intentionally store full payment-card details on our own systems. We may use hosting, analytics, and support providers where needed to operate the Services.",
        ],
      },
      {
        title: "Retention and rights",
        paragraphs: [
          "We retain data only for as long as needed to operate the Services, resolve disputes, prevent abuse, and satisfy legal obligations. Depending on your jurisdiction, you may have rights to request access, correction, deletion, or restriction of certain processing.",
        ],
      },
      {
        title: "Policy updates",
        paragraphs: [
          "We may revise this policy over time. The date at the top of the page reflects the latest revision.",
        ],
      },
    ],
  },
  riskDisclosure: {
    metadataTitle: "Risk Disclosure - Glitch",
    metadataDescription:
      "Important trading risk disclosure for futures, options, NinjaTrader workflows, and prop firm trading environments.",
    title: "Risk Disclosure",
    intro:
      "Futures, options, and prop firm trading involve substantial risk. Glitch is an operating and risk-assistance tool, not a promise of profits or protection from loss.",
    sections: [
      {
        title: "Trading is risky and you remain responsible",
        tone: "warning",
        paragraphs: [
          "Glitch does not guarantee profits, payouts, funding, or compliance outcomes. You remain solely responsible for strategy, execution, account handling, and risk discipline.",
        ],
      },
      {
        title: "Market risk",
        bullets: [
          "Leverage can magnify both gains and losses.",
          "Markets can move quickly and fill worse than expected.",
          "Only risk capital should be used for trading.",
          "Past performance does not guarantee future results.",
        ],
      },
      {
        title: "Technology risk",
        bullets: [
          "Software, APIs, data feeds, and connectivity can fail, lag, or disconnect.",
          "Automations, controls, and alerts may not trigger as intended in every condition.",
          "We are not responsible for missed trades, execution failures, or losses tied to technical issues.",
        ],
      },
      {
        title: "Prop firm and broker rules",
        paragraphs: [
          "If you trade with a prop firm or funded account, you must verify that your use of Glitch fits that firm's current rules. Rules change and may differ across firms, accounts, and providers.",
        ],
      },
      {
        title: "No advice or performance promise",
        paragraphs: [
          "Nothing in Glitch or on this website is financial, legal, tax, or investment advice. Any examples, simulations, or scenarios are illustrative only and do not represent guaranteed performance.",
        ],
      },
    ],
  },
};

const pt: LegalContent = {
  shared: {
    lastUpdatedLabel: "Última atualização",
    backToHome: "Voltar para o início",
    terms: "Termos de Serviço",
    privacy: "Política de Privacidade",
    riskDisclosure: "Aviso de Risco",
  },
  terms: {
    metadataTitle: "Termos de Serviço - Glitch",
    metadataDescription:
      "Consulte os termos do Glitch, regras de cobrança, uso aceitável, avisos e limites de responsabilidade.",
    title: "Termos de Serviço",
    intro:
      "Estes termos regem o uso do software, dos downloads, do site, das APIs e dos serviços relacionados do Glitch. Ao usar o Glitch, você concorda com estes termos.",
    sections: [
      {
        title: "Software e serviços são fornecidos no estado em que se encontram",
        paragraphs: [
          "O Glitch é fornecido “como está” e “conforme disponível”, sem garantias expressas ou implícitas. Não prometemos operação ininterrupta, ausência de erros ou adequação a um objetivo específico de trading.",
        ],
      },
      {
        title: "O risco de trading e de tecnologia permanece com o usuário",
        paragraphs: [
          "Trading envolve risco substancial de perda. Bugs, atrasos, indisponibilidade, falhas de dados, problemas de broker e falhas de terceiros podem afetar o resultado. Você continua responsável por suas decisões, pelo uso das contas e pela gestão de risco.",
        ],
      },
      {
        title: "Cobrança e planos",
        bullets: [
          "Os planos podem incluir acesso grátis, assinatura, anual e vitalício.",
          "Periodicidade, renovação e eventuais testes aparecem no checkout.",
          "Regras de promo são limitadas a um código por pedido, salvo aviso expresso em contrário.",
        ],
      },
      {
        title: "Uso aceitável",
        bullets: [
          "Não abuse de licenciamento, ativação, checkout ou distribuição.",
          "Não use o Glitch para fraude, promoção enganosa ou evasão de políticas.",
          "Não faça engenharia reversa nem uso indevido além do que a lei permitir de forma clara.",
        ],
      },
      {
        title: "Responsabilidade sobre regras de prop firm",
        paragraphs: [
          "O Glitch foi feito para apoiar fluxos profissionais de prop trading, mas você é o único responsável por verificar as regras atuais da sua corretora, prop firm ou programa financiado. Não garantimos conformidade com regras presentes ou futuras de qualquer firma.",
        ],
      },
      {
        title: "Responsabilidade, mudanças e disputas",
        paragraphs: [
          "Na máxima medida permitida por lei, nossa responsabilidade fica limitada ao valor pago por você pelos Serviços nos doze meses anteriores a qualquer reclamação. Podemos atualizar estes termos e os Serviços ao longo do tempo. O uso contínuo após uma atualização representa aceitação da versão revisada.",
        ],
      },
    ],
  },
  privacy: {
    metadataTitle: "Política de Privacidade - Glitch",
    metadataDescription:
      "Saiba como o Glitch coleta, usa, armazena e protege dados no site, no checkout, no member hub e nos serviços do produto.",
    title: "Política de Privacidade",
    intro:
      "Coletamos apenas as informações necessárias para operar o Glitch, liberar acesso, oferecer suporte, reduzir abuso e melhorar a qualidade do produto.",
    sections: [
      {
        title: "Dados que podemos coletar",
        bullets: [
          "Dados de conta e contato necessários para acesso ou suporte.",
          "Status de assinatura e cobrança vindos de provedores aprovados.",
          "Sinais básicos de analytics, diagnóstico e uso do produto.",
          "Conversas de suporte e material de troubleshooting enviado por você.",
        ],
      },
      {
        title: "Como usamos os dados",
        bullets: [
          "Liberar e manter acesso aos Serviços.",
          "Processar cobrança, entitlement, prevenção a fraude e controles contra abuso.",
          "Melhorar estabilidade, qualidade de suporte e comunicação operacional.",
          "Cumprir obrigações legais e aplicar nossos termos.",
        ],
      },
      {
        title: "Terceiros e pagamentos",
        paragraphs: [
          "Pagamentos são processados por provedores aprovados como Whop ou Stripe. Não buscamos armazenar dados completos de cartão em nossos próprios sistemas. Podemos usar provedores de hospedagem, analytics e suporte quando necessário para operar os Serviços.",
        ],
      },
      {
        title: "Retenção e direitos",
        paragraphs: [
          "Retemos dados apenas pelo tempo necessário para operar os Serviços, resolver disputas, prevenir abuso e cumprir obrigações legais. Dependendo da sua jurisdição, você pode ter direitos de acesso, correção, exclusão ou restrição de determinados tratamentos.",
        ],
      },
      {
        title: "Atualizações desta política",
        paragraphs: [
          "Podemos revisar esta política ao longo do tempo. A data no topo da página indica a revisão mais recente.",
        ],
      },
    ],
  },
  riskDisclosure: {
    metadataTitle: "Aviso de Risco - Glitch",
    metadataDescription:
      "Aviso importante sobre riscos de trading para futuros, opções, fluxos NinjaTrader e ambientes de prop firm.",
    title: "Aviso de Risco",
    intro:
      "Trading em futuros, opções e prop firms envolve risco substancial. O Glitch é uma ferramenta operacional e de assistência ao risco, não uma promessa de lucro nem de proteção contra perdas.",
    sections: [
      {
        title: "Trading é arriscado e a responsabilidade é sua",
        tone: "warning",
        paragraphs: [
          "O Glitch não garante lucro, payout, funding ou conformidade. Você continua sendo o único responsável por estratégia, execução, uso das contas e disciplina de risco.",
        ],
      },
      {
        title: "Risco de mercado",
        bullets: [
          "Alavancagem pode ampliar tanto ganhos quanto perdas.",
          "Mercados podem se mover rápido e executar pior do que o esperado.",
          "Somente capital de risco deve ser usado para trading.",
          "Desempenho passado não garante resultado futuro.",
        ],
      },
      {
        title: "Risco tecnológico",
        bullets: [
          "Software, APIs, feeds e conectividade podem falhar, atrasar ou cair.",
          "Automações, controles e alertas podem não disparar como esperado em todas as condições.",
          "Não somos responsáveis por perdas, falhas de execução ou trades perdidos ligados a problemas técnicos.",
        ],
      },
      {
        title: "Regras de prop firm e corretora",
        paragraphs: [
          "Se você usa o Glitch com prop firm ou conta financiada, cabe a você verificar se o uso está alinhado às regras atuais daquela firma. As regras mudam e podem variar entre firmas, contas e provedores.",
        ],
      },
      {
        title: "Sem aconselhamento ou promessa de performance",
        paragraphs: [
          "Nada no Glitch ou neste site constitui aconselhamento financeiro, jurídico, tributário ou de investimento. Exemplos, simulações e cenários são apenas ilustrativos e não representam performance garantida.",
        ],
      },
    ],
  },
};

const es: LegalContent = {
  shared: {
    lastUpdatedLabel: "Última actualización",
    backToHome: "Volver al inicio",
    terms: "Términos del Servicio",
    privacy: "Política de Privacidad",
    riskDisclosure: "Aviso de Riesgo",
  },
  terms: {
    metadataTitle: "Términos del Servicio - Glitch",
    metadataDescription:
      "Consulta los términos de Glitch, reglas de cobro, uso aceptable, avisos y límites de responsabilidad.",
    title: "Términos del Servicio",
    intro:
      "Estos términos regulan el uso del software, las descargas, el sitio web, las APIs y los servicios relacionados de Glitch. Al usar Glitch, aceptas estos términos.",
    sections: [
      {
        title: "El software y los servicios se ofrecen tal como están",
        paragraphs: [
          "Glitch se ofrece “tal como está” y “según disponibilidad”, sin garantías expresas ni implícitas. No prometemos servicio ininterrumpido, funcionamiento sin errores ni idoneidad para un objetivo de trading concreto.",
        ],
      },
      {
        title: "El riesgo de trading y de tecnología sigue siendo del usuario",
        paragraphs: [
          "El trading implica un riesgo sustancial de pérdida. Bugs, retrasos, caídas, problemas de datos, fallos del bróker o de terceros pueden afectar los resultados. Tú sigues siendo responsable de tus decisiones, de tus cuentas y de tu gestión del riesgo.",
        ],
      },
      {
        title: "Facturación y planes",
        bullets: [
          "Los planes pueden incluir acceso gratuito, suscripción, anual y vitalicio.",
          "La frecuencia de cobro, la renovación y cualquier prueba se muestran en el checkout.",
          "Las promociones se limitan a un código por pedido salvo indicación expresa en contrario.",
        ],
      },
      {
        title: "Uso aceptable",
        bullets: [
          "No abuses del licenciamiento, la activación, el checkout ni los mecanismos de distribución.",
          "No uses Glitch para fraude, promoción engañosa ni evasión de políticas.",
          "No hagas ingeniería inversa ni un uso indebido más allá de lo que la ley permita de forma clara.",
        ],
      },
      {
        title: "Responsabilidad sobre reglas de prop firm",
        paragraphs: [
          "Glitch está diseñado para apoyar flujos profesionales de prop trading, pero eres el único responsable de verificar las reglas actuales de tu bróker, prop firm o programa fondeado. No garantizamos conformidad con las reglas presentes o futuras de ninguna firma.",
        ],
      },
      {
        title: "Responsabilidad, cambios y disputas",
        paragraphs: [
          "En la máxima medida permitida por la ley, nuestra responsabilidad queda limitada al importe que nos hayas pagado por los Servicios durante los doce meses anteriores a cualquier reclamación. Podemos actualizar estos términos y los Servicios con el tiempo. El uso continuado tras una actualización implica aceptación de la versión revisada.",
        ],
      },
    ],
  },
  privacy: {
    metadataTitle: "Política de Privacidad - Glitch",
    metadataDescription:
      "Consulta cómo Glitch recopila, usa, almacena y protege datos en el sitio web, checkout, member hub y servicios del producto.",
    title: "Política de Privacidad",
    intro:
      "Recopilamos solo la información necesaria para operar Glitch, dar acceso, ofrecer soporte, reducir abusos y mejorar la calidad del producto.",
    sections: [
      {
        title: "Datos que podemos recopilar",
        bullets: [
          "Datos de cuenta y contacto necesarios para acceso o soporte.",
          "Estado de suscripción y facturación desde proveedores aprobados.",
          "Señales básicas de analítica, diagnóstico y uso del producto.",
          "Conversaciones de soporte y material de troubleshooting que decidas enviarnos.",
        ],
      },
      {
        title: "Cómo usamos los datos",
        bullets: [
          "Dar y mantener acceso a los Servicios.",
          "Procesar cobros, entitlements, prevención de fraude y controles antiabuso.",
          "Mejorar estabilidad, calidad del soporte y comunicación operativa.",
          "Cumplir obligaciones legales y hacer valer nuestros términos.",
        ],
      },
      {
        title: "Terceros y pagos",
        paragraphs: [
          "Los pagos son procesados por proveedores aprobados como Whop o Stripe. No buscamos almacenar datos completos de tarjetas en nuestros propios sistemas. Podemos usar proveedores de hosting, analítica y soporte cuando sea necesario para operar los Servicios.",
        ],
      },
      {
        title: "Retención y derechos",
        paragraphs: [
          "Conservamos los datos solo el tiempo necesario para operar los Servicios, resolver disputas, prevenir abusos y cumplir obligaciones legales. Según tu jurisdicción, puedes tener derechos de acceso, corrección, eliminación o limitación de ciertos tratamientos.",
        ],
      },
      {
        title: "Actualizaciones de esta política",
        paragraphs: [
          "Podemos revisar esta política con el tiempo. La fecha en la parte superior de la página indica la revisión más reciente.",
        ],
      },
    ],
  },
  riskDisclosure: {
    metadataTitle: "Aviso de Riesgo - Glitch",
    metadataDescription:
      "Aviso importante sobre riesgos de trading para futuros, opciones, flujos de NinjaTrader y entornos de prop firm.",
    title: "Aviso de Riesgo",
    intro:
      "Operar futuros, opciones y cuentas de prop firm implica un riesgo sustancial. Glitch es una herramienta operativa y de asistencia al riesgo, no una promesa de beneficios ni de protección frente a pérdidas.",
    sections: [
      {
        title: "El trading es arriesgado y la responsabilidad es tuya",
        tone: "warning",
        paragraphs: [
          "Glitch no garantiza beneficios, payouts, funding ni resultados de cumplimiento. Tú sigues siendo el único responsable de estrategia, ejecución, manejo de cuentas y disciplina de riesgo.",
        ],
      },
      {
        title: "Riesgo de mercado",
        bullets: [
          "El apalancamiento puede ampliar tanto ganancias como pérdidas.",
          "Los mercados pueden moverse rápido y ejecutar peor de lo esperado.",
          "Solo debe usarse capital de riesgo para operar.",
          "El rendimiento pasado no garantiza resultados futuros.",
        ],
      },
      {
        title: "Riesgo tecnológico",
        bullets: [
          "Software, APIs, feeds y conectividad pueden fallar, retrasarse o caerse.",
          "Automatizaciones, controles y alertas pueden no activarse como se espera en todas las condiciones.",
          "No somos responsables de pérdidas, fallos de ejecución o trades perdidos relacionados con problemas técnicos.",
        ],
      },
      {
        title: "Reglas de prop firm y bróker",
        paragraphs: [
          "Si usas Glitch con una prop firm o una cuenta fondeada, te corresponde verificar que el uso encaje con las reglas actuales de esa firma. Las reglas cambian y pueden variar entre firmas, cuentas y proveedores.",
        ],
      },
      {
        title: "Sin asesoramiento ni promesa de rendimiento",
        paragraphs: [
          "Nada en Glitch ni en este sitio constituye asesoramiento financiero, legal, fiscal o de inversión. Los ejemplos, simulaciones y escenarios son solo ilustrativos y no representan un rendimiento garantizado.",
        ],
      },
    ],
  },
};

const fr: LegalContent = {
  shared: {
    lastUpdatedLabel: "Dernière mise à jour",
    backToHome: "Retour à l’accueil",
    terms: "Conditions d’utilisation",
    privacy: "Politique de confidentialité",
    riskDisclosure: "Avertissement sur les risques",
  },
  terms: {
    metadataTitle: "Conditions d’utilisation - Glitch",
    metadataDescription:
      "Consultez les conditions Glitch, les règles de facturation, l’usage acceptable, les avertissements et les limites de responsabilité.",
    title: "Conditions d’utilisation",
    intro:
      "Ces conditions régissent l’utilisation du logiciel, des téléchargements, du site, des API et des services associés de Glitch. En utilisant Glitch, vous acceptez ces conditions.",
    sections: [
      {
        title: "Le logiciel et les services sont fournis en l’état",
        paragraphs: [
          "Glitch est fourni « en l’état » et « selon disponibilité », sans garantie expresse ou implicite. Nous ne promettons ni service ininterrompu, ni fonctionnement sans erreur, ni adéquation à un objectif de trading particulier.",
        ],
      },
      {
        title: "Le risque de trading et de technologie reste à votre charge",
        paragraphs: [
          "Le trading comporte un risque élevé de perte. Bugs, retards, pannes, problèmes de données, défaillances du courtier ou de tiers peuvent affecter les résultats. Vous restez responsable de vos décisions, de vos comptes et de votre gestion du risque.",
        ],
      },
      {
        title: "Facturation et plans",
        bullets: [
          "Les plans peuvent inclure un accès gratuit, par abonnement, annuel et à vie.",
          "Le rythme de facturation, le renouvellement et toute période d’essai apparaissent au checkout.",
          "Les promotions sont limitées à un code par commande sauf indication contraire explicite.",
        ],
      },
      {
        title: "Usage acceptable",
        bullets: [
          "N’abusez pas des mécanismes de licence, d’activation, de checkout ou de distribution.",
          "N’utilisez pas Glitch pour la fraude, la promotion trompeuse ou le contournement de politiques.",
          "N’effectuez pas d’ingénierie inverse ni d’usage abusif au-delà de ce que la loi autorise clairement.",
        ],
      },
      {
        title: "Responsabilité sur les règles des prop firms",
        paragraphs: [
          "Glitch est conçu pour soutenir des workflows prop professionnels, mais vous êtes seul responsable de vérifier les règles actuelles de votre courtier, de votre prop firm ou de votre programme financé. Nous ne garantissons pas la conformité avec les règles présentes ou futures d’une société donnée.",
        ],
      },
      {
        title: "Responsabilité, changements et litiges",
        paragraphs: [
          "Dans toute la mesure permise par la loi, notre responsabilité est limitée au montant que vous avez payé pour les Services durant les douze mois précédant une réclamation. Nous pouvons mettre à jour ces conditions et les Services au fil du temps. La poursuite de l’utilisation après une mise à jour vaut acceptation de la version révisée.",
        ],
      },
    ],
  },
  privacy: {
    metadataTitle: "Politique de confidentialité - Glitch",
    metadataDescription:
      "Découvrez comment Glitch collecte, utilise, stocke et protège les données sur le site, le checkout, le member hub et les services produit.",
    title: "Politique de confidentialité",
    intro:
      "Nous ne collectons que les informations nécessaires au fonctionnement de Glitch, à l’accès au service, au support, à la réduction des abus et à l’amélioration du produit.",
    sections: [
      {
        title: "Données susceptibles d’être collectées",
        bullets: [
          "Coordonnées et informations de compte nécessaires pour l’accès ou le support.",
          "Statut d’abonnement et de facturation provenant de prestataires approuvés.",
          "Signaux de base d’analytics, de diagnostic et d’usage produit.",
          "Échanges de support et éléments de dépannage que vous choisissez de nous envoyer.",
        ],
      },
      {
        title: "Utilisation des données",
        bullets: [
          "Fournir et maintenir l’accès aux Services.",
          "Gérer la facturation, les droits d’accès, la prévention de la fraude et les contrôles anti-abus.",
          "Améliorer la stabilité produit, la qualité du support et la communication opérationnelle.",
          "Respecter les obligations légales et faire appliquer nos conditions.",
        ],
      },
      {
        title: "Tiers et paiements",
        paragraphs: [
          "Les paiements sont traités par des prestataires approuvés tels que Whop ou Stripe. Nous ne cherchons pas à stocker les données complètes de carte bancaire sur nos propres systèmes. Nous pouvons utiliser des prestataires d’hébergement, d’analytics et de support lorsque cela est nécessaire à l’exploitation des Services.",
        ],
      },
      {
        title: "Conservation et droits",
        paragraphs: [
          "Nous conservons les données uniquement pendant la durée nécessaire à l’exploitation des Services, à la résolution des litiges, à la prévention des abus et au respect des obligations légales. Selon votre juridiction, vous pouvez disposer de droits d’accès, de rectification, d’effacement ou de limitation de certains traitements.",
        ],
      },
      {
        title: "Mises à jour de cette politique",
        paragraphs: [
          "Nous pouvons réviser cette politique au fil du temps. La date en haut de page indique la version la plus récente.",
        ],
      },
    ],
  },
  riskDisclosure: {
    metadataTitle: "Avertissement sur les risques - Glitch",
    metadataDescription:
      "Avertissement important sur les risques de trading pour les futures, options, workflows NinjaTrader et environnements de prop firm.",
    title: "Avertissement sur les risques",
    intro:
      "Le trading sur futures, options et comptes de prop firm comporte un risque important. Glitch est un outil opérationnel et d’assistance au risque, pas une promesse de profit ni de protection contre les pertes.",
    sections: [
      {
        title: "Le trading est risqué et vous restez responsable",
        tone: "warning",
        paragraphs: [
          "Glitch ne garantit ni profit, ni payout, ni funding, ni résultat de conformité. Vous restez seul responsable de la stratégie, de l’exécution, de la gestion des comptes et de la discipline de risque.",
        ],
      },
      {
        title: "Risque de marché",
        bullets: [
          "L’effet de levier peut amplifier les gains comme les pertes.",
          "Les marchés peuvent bouger vite et vous faire exécuter à un prix pire que prévu.",
          "Seul le capital à risque doit être utilisé pour trader.",
          "Les performances passées ne garantissent pas les résultats futurs.",
        ],
      },
      {
        title: "Risque technologique",
        bullets: [
          "Le logiciel, les API, les flux de données et la connectivité peuvent tomber en panne, ralentir ou se déconnecter.",
          "Les automatisations, contrôles et alertes peuvent ne pas se déclencher comme prévu dans toutes les conditions.",
          "Nous ne sommes pas responsables des pertes, échecs d’exécution ou opportunités manquées liés à des problèmes techniques.",
        ],
      },
      {
        title: "Règles des prop firms et des courtiers",
        paragraphs: [
          "Si vous utilisez Glitch avec une prop firm ou un compte financé, il vous appartient de vérifier que l’usage correspond aux règles actuelles de cette société. Les règles évoluent et peuvent varier selon les sociétés, les comptes et les prestataires.",
        ],
      },
      {
        title: "Aucun conseil ni promesse de performance",
        paragraphs: [
          "Rien dans Glitch ni sur ce site ne constitue un conseil financier, juridique, fiscal ou d’investissement. Les exemples, simulations et scénarios sont purement illustratifs et ne représentent aucune performance garantie.",
        ],
      },
    ],
  },
};

const ru: LegalContent = {
  shared: {
    lastUpdatedLabel: "Последнее обновление",
    backToHome: "Назад на главную",
    terms: "Условия использования",
    privacy: "Политика конфиденциальности",
    riskDisclosure: "Раскрытие рисков",
  },
  terms: {
    metadataTitle: "Условия использования - Glitch",
    metadataDescription:
      "Ознакомьтесь с условиями Glitch, правилами оплаты, допустимым использованием, оговорками и ограничением ответственности.",
    title: "Условия использования",
    intro:
      "Эти условия регулируют использование программного обеспечения, загрузок, сайта, API и связанных сервисов Glitch. Используя Glitch, вы соглашаетесь с этими условиями.",
    sections: [
      {
        title: "ПО и сервисы предоставляются как есть",
        paragraphs: [
          "Glitch предоставляется по принципу «как есть» и «по мере доступности», без прямых или подразумеваемых гарантий. Мы не обещаем бесперебойную работу, отсутствие ошибок или пригодность для конкретной торговой задачи.",
        ],
      },
      {
        title: "Торговый и технологический риск остаются на пользователе",
        paragraphs: [
          "Торговля связана с существенным риском убытков. Баги, задержки, сбои, проблемы с данными, неполадки у брокера или сторонних сервисов могут повлиять на результат. Ответственность за решения, работу со счетами и управление риском несете вы.",
        ],
      },
      {
        title: "Оплата и тарифы",
        bullets: [
          "Тарифы могут включать бесплатный, подписочный, годовой и пожизненный доступ.",
          "Периодичность списаний, продление и условия пробного периода показываются при оформлении заказа.",
          "Промо-правила ограничены одним кодом на заказ, если мы явно не указали иное.",
        ],
      },
      {
        title: "Допустимое использование",
        bullets: [
          "Нельзя злоупотреблять лицензированием, активацией, оформлением заказа или механизмами распространения.",
          "Нельзя использовать Glitch для мошенничества, вводящего в заблуждение продвижения или обхода правил.",
          "Нельзя заниматься реверс-инжинирингом и иным злоупотреблением за пределами того, что прямо допускает закон.",
        ],
      },
      {
        title: "Ответственность за правила prop firm",
        paragraphs: [
          "Glitch создан для поддержки профессиональных prop-процессов, но вы единолично отвечаете за проверку актуальных правил вашего брокера, prop firm или funded-программы. Мы не гарантируем соответствие текущим или будущим правилам какой-либо компании.",
        ],
      },
      {
        title: "Ответственность, изменения и споры",
        paragraphs: [
          "В максимально допустимой законом степени наша ответственность ограничена суммой, которую вы фактически заплатили за Сервисы за двенадцать месяцев до претензии. Мы можем обновлять эти условия и сами Сервисы со временем. Продолжение использования после обновления означает принятие новой редакции.",
        ],
      },
    ],
  },
  privacy: {
    metadataTitle: "Политика конфиденциальности - Glitch",
    metadataDescription:
      "Узнайте, как Glitch собирает, использует, хранит и защищает данные на сайте, при оплате, в member hub и в продуктовых сервисах.",
    title: "Политика конфиденциальности",
    intro:
      "Мы собираем только те данные, которые нужны для работы Glitch, предоставления доступа, поддержки, снижения злоупотреблений и улучшения продукта.",
    sections: [
      {
        title: "Какие данные мы можем собирать",
        bullets: [
          "Данные учетной записи и контакты, необходимые для доступа или поддержки.",
          "Статус подписки и оплаты от одобренных платежных провайдеров.",
          "Базовые сигналы веб-аналитики, диагностики и использования продукта.",
          "Переписку с поддержкой и материалы для устранения неполадок, которые вы сами нам отправляете.",
        ],
      },
      {
        title: "Как мы используем данные",
        bullets: [
          "Предоставляем и поддерживаем доступ к Сервисам.",
          "Обрабатываем оплату, права доступа, защиту от мошенничества и антиабуз-контроль.",
          "Улучшаем стабильность продукта, качество поддержки и операционные уведомления.",
          "Соблюдаем юридические требования и применяем наши условия.",
        ],
      },
      {
        title: "Третьи стороны и платежи",
        paragraphs: [
          "Платежи обрабатываются одобренными провайдерами, такими как Whop или Stripe. Мы не стремимся хранить полные данные банковских карт на собственных системах. При необходимости для работы Сервисов мы можем использовать провайдеров хостинга, аналитики и поддержки.",
        ],
      },
      {
        title: "Хранение и права",
        paragraphs: [
          "Мы храним данные только столько, сколько нужно для работы Сервисов, разрешения споров, предотвращения злоупотреблений и выполнения требований закона. В зависимости от вашей юрисдикции у вас могут быть права на доступ, исправление, удаление или ограничение отдельных видов обработки.",
        ],
      },
      {
        title: "Обновления этой политики",
        paragraphs: [
          "Мы можем пересматривать эту политику со временем. Дата в верхней части страницы показывает последнюю редакцию.",
        ],
      },
    ],
  },
  riskDisclosure: {
    metadataTitle: "Раскрытие рисков - Glitch",
    metadataDescription:
      "Важное раскрытие торговых рисков для фьючерсов, опционов, NinjaTrader workflows и сред prop firm.",
    title: "Раскрытие рисков",
    intro:
      "Торговля фьючерсами, опционами и на счетах prop firm связана с существенным риском. Glitch — это операционный и risk-assistance инструмент, а не обещание прибыли или защиты от убытков.",
    sections: [
      {
        title: "Торговля рискованна, и ответственность остается на вас",
        tone: "warning",
        paragraphs: [
          "Glitch не гарантирует прибыль, выплаты, funding или результаты по соблюдению правил. Полная ответственность за стратегию, исполнение, работу со счетами и риск-дисциплину лежит на вас.",
        ],
      },
      {
        title: "Рыночный риск",
        bullets: [
          "Плечо может усиливать как прибыль, так и убытки.",
          "Рынок может двигаться быстро, а исполнение — быть хуже ожидаемого.",
          "Для торговли следует использовать только риск-капитал.",
          "Прошлые результаты не гарантируют будущих.",
        ],
      },
      {
        title: "Технологический риск",
        bullets: [
          "ПО, API, потоки данных и связь могут сбоить, тормозить или отключаться.",
          "Автоматизация, контроли и алерты могут не сработать должным образом в каждой ситуации.",
          "Мы не несем ответственности за убытки, ошибки исполнения или упущенные сделки, связанные с техническими проблемами.",
        ],
      },
      {
        title: "Правила prop firm и брокера",
        paragraphs: [
          "Если вы используете Glitch с prop firm или funded-счетом, вы сами обязаны убедиться, что такой способ использования соответствует текущим правилам конкретной компании. Правила меняются и могут отличаться между фирмами, счетами и провайдерами.",
        ],
      },
      {
        title: "Никаких советов и обещаний результата",
        paragraphs: [
          "Ничто в Glitch или на этом сайте не является финансовым, юридическим, налоговым или инвестиционным советом. Любые примеры, симуляции и сценарии носят исключительно иллюстративный характер и не являются гарантией результата.",
        ],
      },
    ],
  },
};

const zh: LegalContent = {
  shared: {
    lastUpdatedLabel: "最后更新",
    backToHome: "返回首页",
    terms: "服务条款",
    privacy: "隐私政策",
    riskDisclosure: "风险披露",
  },
  terms: {
    metadataTitle: "服务条款 - Glitch",
    metadataDescription:
      "查看 Glitch 的服务条款、计费规则、可接受使用规范、免责声明与责任限制。",
    title: "服务条款",
    intro:
      "本条款适用于您对 Glitch 软件、下载内容、网站、API 及相关服务的使用。使用 Glitch 即表示您同意这些条款。",
    sections: [
      {
        title: "软件与服务按现状提供",
        paragraphs: [
          "Glitch 按“现状”和“可用状态”提供，不附带任何明示或默示保证。我们不承诺服务不间断、无错误，也不承诺其适用于某一特定交易目的。",
        ],
      },
      {
        title: "交易风险与技术风险由用户承担",
        paragraphs: [
          "交易具有重大亏损风险。Bug、延迟、宕机、数据问题、经纪商问题或第三方故障都可能影响结果。您仍需对自己的决策、账户操作与风险管理负责。",
        ],
      },
      {
        title: "计费与方案",
        bullets: [
          "方案可能包括免费、订阅、年度和终身访问。",
          "计费周期、续费与任何试用条件会在结账时显示。",
          "除非我们另有明确说明，每笔订单仅限使用一个优惠码。",
        ],
      },
      {
        title: "可接受使用",
        bullets: [
          "不得滥用许可、激活、结账或分发机制。",
          "不得将 Glitch 用于欺诈、误导性推广或规避政策。",
          "不得在法律未明确允许的范围之外进行逆向工程或其他滥用行为。",
        ],
      },
      {
        title: "Prop firm 规则责任",
        paragraphs: [
          "Glitch 旨在支持专业的 prop 交易工作流，但您需自行核验经纪商、prop firm 或 funded 计划的最新规则。我们不保证任何机构当前或未来规则下的合规性。",
        ],
      },
      {
        title: "责任、变更与争议",
        paragraphs: [
          "在法律允许的最大范围内，我们的责任以上一项索赔前十二个月内您实际支付给我们的服务费用为限。我们可能会不时更新这些条款及服务。更新后继续使用即表示接受修订版本。",
        ],
      },
    ],
  },
  privacy: {
    metadataTitle: "隐私政策 - Glitch",
    metadataDescription:
      "了解 Glitch 如何在网站、结账、member hub 与产品服务中收集、使用、存储并保护数据。",
    title: "隐私政策",
    intro:
      "我们仅收集运营 Glitch、提供访问权限、支持用户、减少滥用并提升产品质量所必需的信息。",
    sections: [
      {
        title: "我们可能收集的数据",
        bullets: [
          "用于访问或支持的账户与联系方式。",
          "来自获批支付服务商的订阅与计费状态。",
          "基础网站分析、诊断与产品使用信号。",
          "您主动提交给我们的支持对话与排障材料。",
        ],
      },
      {
        title: "数据用途",
        bullets: [
          "提供并维护对服务的访问。",
          "处理计费、权益验证、反欺诈与反滥用控制。",
          "提升产品稳定性、支持质量与运营沟通。",
          "履行法律义务并执行我们的条款。",
        ],
      },
      {
        title: "第三方与支付",
        paragraphs: [
          "支付由 Whop、Stripe 等获批服务商处理。我们不会主动在自有系统中存储完整的银行卡信息。在运营服务所必需的情况下，我们可能使用托管、分析与支持类第三方服务商。",
        ],
      },
      {
        title: "保留期限与权利",
        paragraphs: [
          "我们仅在运营服务、解决争议、防止滥用和履行法律义务所需期间内保留数据。根据您所在司法辖区，您可能享有访问、更正、删除或限制某些处理活动的权利。",
        ],
      },
      {
        title: "政策更新",
        paragraphs: [
          "我们可能会不时修订本政策。页面顶部日期代表最新修订时间。",
        ],
      },
    ],
  },
  riskDisclosure: {
    metadataTitle: "风险披露 - Glitch",
    metadataDescription:
      "面向期货、期权、NinjaTrader 工作流与 prop firm 环境的重要交易风险披露。",
    title: "风险披露",
    intro:
      "期货、期权以及 prop firm 账户交易都具有重大风险。Glitch 是运营与风险辅助工具，并不承诺盈利，也不保证避免亏损。",
    sections: [
      {
        title: "交易有风险，责任仍由您承担",
        tone: "warning",
        paragraphs: [
          "Glitch 不保证盈利、payout、funding 或合规结果。策略、执行、账户处理与风险纪律的责任完全在您自己。",
        ],
      },
      {
        title: "市场风险",
        bullets: [
          "杠杆可能同时放大收益与亏损。",
          "市场可能快速波动，成交价格可能差于预期。",
          "交易只应使用可承受损失的风险资金。",
          "历史表现不代表未来结果。",
        ],
      },
      {
        title: "技术风险",
        bullets: [
          "软件、API、数据源与连接可能出现故障、延迟或中断。",
          "自动化、控制与提醒在所有条件下都可能无法按预期触发。",
          "对于因技术问题导致的亏损、执行失败或错失交易，我们不承担责任。",
        ],
      },
      {
        title: "Prop firm 与经纪商规则",
        paragraphs: [
          "如果您在 prop firm 或 funded 账户环境中使用 Glitch，您必须自行确认其使用方式符合该机构的现行规则。规则会变化，不同机构、账户与服务商之间也可能不同。",
        ],
      },
      {
        title: "不构成建议，也不承诺业绩",
        paragraphs: [
          "Glitch 或本网站上的任何内容均不构成金融、法律、税务或投资建议。任何示例、模拟或情景仅供说明，不代表任何保证性结果。",
        ],
      },
    ],
  },
};

export function getLegalContent(locale?: string): LegalContent {
  switch (locale) {
    case "pt":
      return pt;
    case "es":
      return es;
    case "fr":
      return fr;
    case "ru":
      return ru;
    case "zh":
      return zh;
    default:
      return en;
  }
}
