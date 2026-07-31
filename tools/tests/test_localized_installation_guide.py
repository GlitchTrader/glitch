"""Public documentation localization and release contracts."""

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DOCS_ROOT = ROOT / "ninjatrader/Glitch/Docs"
DOCS_APP = ROOT / "apps/doc/src"
DOWNLOAD_APP = ROOT / "apps/download/src"

GUIDES = {
    "en": DOCS_ROOT / "installation-guide-troubleshooting.md",
    "pt": DOCS_ROOT / "installation-guide-troubleshooting.pt.md",
    "es": DOCS_ROOT / "installation-guide-troubleshooting.es.md",
    "zh": DOCS_ROOT / "installation-guide-troubleshooting.zh.md",
    "fr": DOCS_ROOT / "installation-guide-troubleshooting.fr.md",
    "ru": DOCS_ROOT / "installation-guide-troubleshooting.ru.md",
}

PUBLIC_DOCS = (
    "installation-guide-troubleshooting",
    "architecture",
    "addon",
    "indicator",
    "data-flow-and-bridge",
    "persistence",
    "api-reference",
)

LOCALE_SUFFIXES = {
    "en": "",
    "pt": ".pt",
    "es": ".es",
    "zh": ".zh",
    "fr": ".fr",
    "ru": ".ru",
}

ADDON_DOCS = {
    locale: DOCS_ROOT / f"addon{suffix}.md"
    for locale, suffix in LOCALE_SUFFIXES.items()
}

ARCHITECTURE_DOCS = {
    locale: DOCS_ROOT / f"architecture{suffix}.md"
    for locale, suffix in LOCALE_SUFFIXES.items()
}

LANGUAGE_HREFS = {
    "en": "/installation-guide-troubleshooting",
    "pt": "/pt/installation-guide-troubleshooting",
    "es": "/es/installation-guide-troubleshooting",
    "zh": "/zh/installation-guide-troubleshooting",
    "fr": "/fr/installation-guide-troubleshooting",
    "ru": "/ru/installation-guide-troubleshooting",
}

REQUIRED_TOKENS = (
    "v0.0.2.0",
    "v0.0.2.2",
    "https://download.glitchtrader.com/latest",
    "https://download.glitchtrader.com/latest/ai",
    "github.com/GlitchTrader/glitch-hermes-profile",
    "hermes profile export glitch",
    "hermes profile import",
    "hermes profile update glitch",
    "openai-codex --type oauth",
    "setup.ps1",
    "glitch-direct-operator",
    "glitch-learning-supervisor",
    "GlitchData",
    "GlitchAnalyticsBridge",
    "GlitchAiMarketIngest",
    "5/5 snapshots",
    "/trade",
    "/pause_trading",
    "/flatten_all",
    "/glitch_status",
    "AI Auto",
    "2x",
    "Flatten All",
)


class LocalizedInstallationGuideTests(unittest.TestCase):
    def test_every_public_document_has_all_six_locales(self):
        for slug in PUBLIC_DOCS:
            for locale, suffix in LOCALE_SUFFIXES.items():
                path = DOCS_ROOT / f"{slug}{suffix}.md"
                with self.subTest(slug=slug, locale=locale):
                    self.assertTrue(path.is_file(), f"missing localized document: {path}")
                    self.assertGreater(len(path.read_text(encoding="utf-8")), 300)

    def test_every_supported_locale_has_a_complete_guide(self):
        for locale, path in GUIDES.items():
            with self.subTest(locale=locale):
                self.assertTrue(path.is_file(), f"missing localized guide: {path}")
                text = path.read_text(encoding="utf-8")
                self.assertGreater(len(text), 8_000)
                for token in REQUIRED_TOKENS:
                    self.assertIn(token, text)

    def test_every_guide_links_to_every_language(self):
        for locale, path in GUIDES.items():
            text = path.read_text(encoding="utf-8")
            for linked_locale, href in LANGUAGE_HREFS.items():
                with self.subTest(locale=locale, linked_locale=linked_locale):
                    self.assertIn(f"]({href})", text)

    def test_docs_app_publishes_localized_routes_and_hreflang(self):
        locale_source = (DOCS_APP / "lib/docs-locales.ts").read_text(encoding="utf-8")
        article_route = (DOCS_APP / "app/[...segments]/page.tsx").read_text(encoding="utf-8")
        sitemap = (DOCS_APP / "app/sitemap.ts").read_text(encoding="utf-8")

        self.assertIn('["en", "pt", "es", "zh", "fr", "ru"]', locale_source)
        self.assertIn("getDocLanguages", article_route)
        self.assertIn("resolveArticle", article_route)
        self.assertIn("getDocsHref", sitemap)
        self.assertIn("getDocSummaries(locale)", sitemap)

    def test_public_language_switchers_match_the_flagged_website_control(self):
        for app in (DOCS_APP, DOWNLOAD_APP):
            header = (app / "components/site-header.tsx").read_text(encoding="utf-8")
            switcher = (app / "components/language-switcher.tsx").read_text(encoding="utf-8")
            with self.subTest(app=app.parent.name):
                self.assertNotIn("<select", header)
                self.assertIn("<LanguageSwitcher", header)
                self.assertIn('aria-haspopup="listbox"', switcher)
                self.assertIn("/images/flags/square/br.svg", switcher)
                self.assertIn("bg-glitch-teal", switcher)

    def test_canonical_guide_describes_both_editions_without_readiness_claims(self):
        text = GUIDES["en"].read_text(encoding="utf-8")
        self.assertIn("Standard", text)
        self.assertIn("AI Experimental", text)
        self.assertIn("does not promise profitability", text)
        self.assertIn("Do **not** install both packages", text)
        self.assertNotIn("This guide covers the standard Glitch setup flow", text)

    def test_public_docs_match_current_learning_and_authority_contracts(self):
        obsolete_cadence = {
            "en": "every 15 minutes",
            "pt": "a cada 15 minutos",
            "es": "cada 15 minutos",
            "zh": "每 15 分钟",
            "fr": "toutes les 15 minutes",
            "ru": "каждые 15 минут",
        }
        current_cadence = {
            "en": "every 30 minutes",
            "pt": "a cada 30 minutos",
            "es": "cada 30 minutos",
            "zh": "每 30 分钟",
            "fr": "toutes les 30 minutes",
            "ru": "каждые 30 минут",
        }
        for locale, path in GUIDES.items():
            text = path.read_text(encoding="utf-8")
            with self.subTest(locale=locale, contract="learning_cadence"):
                self.assertNotIn(obsolete_cadence[locale], text)
                self.assertIn(current_cadence[locale], text)

        for documents in (ADDON_DOCS, ARCHITECTURE_DOCS):
            for locale, path in documents.items():
                text = path.read_text(encoding="utf-8")
                with self.subTest(locale=locale, document=path.name, contract="explicit_sync"):
                    self.assertIn("**Sync**", text)


if __name__ == "__main__":
    unittest.main()
