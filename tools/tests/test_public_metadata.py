"""Brand and social metadata contracts for every Glitch web surface."""

import hashlib
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
APP_NAMES = ("website", "doc", "download", "app", "api")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class PublicMetadataTests(unittest.TestCase):
    def test_all_apps_publish_the_same_brand_assets(self):
        website = ROOT / "apps/website"
        expected = {
            "favicon": digest(website / "src/app/favicon.ico"),
            "favicon_png": digest(website / "public/images/Glitch Favicon.png"),
            "app_icon": digest(website / "public/images/branding/Glitch Icon.png"),
            "social_image": digest(website / "public/images/Glitch Banner.png"),
        }

        for app_name in APP_NAMES:
            app = ROOT / "apps" / app_name
            with self.subTest(app=app_name):
                self.assertEqual(digest(app / "src/app/favicon.ico"), expected["favicon"])
                self.assertEqual(digest(app / "public/images/Glitch Favicon.png"), expected["favicon_png"])
                self.assertEqual(digest(app / "public/images/branding/Glitch Icon.png"), expected["app_icon"])
                self.assertEqual(digest(app / "public/images/Glitch Banner.png"), expected["social_image"])

    def test_all_apps_publish_manifest_and_social_metadata(self):
        metadata_sources = {
            "website": ("src/app/[locale]/layout.tsx", "src/lib/seo.ts"),
            "doc": ("src/app/layout.tsx", "src/lib/metadata.ts"),
            "download": ("src/app/layout.tsx", "src/lib/metadata.ts"),
            "app": ("src/app/layout.tsx",),
            "api": ("src/app/layout.tsx",),
        }

        for app_name, relative_sources in metadata_sources.items():
            app = ROOT / "apps" / app_name
            manifest = (app / "src/app/manifest.ts").read_text(encoding="utf-8")
            metadata = "\n".join((app / source).read_text(encoding="utf-8") for source in relative_sources)
            with self.subTest(app=app_name):
                self.assertIn('manifest: "/manifest.webmanifest"', metadata)
                self.assertIn("Glitch%20Favicon.png", metadata)
                self.assertIn("Glitch%20Banner.png", metadata)
                self.assertIn("openGraph", metadata)
                self.assertIn("twitter", metadata)
                self.assertIn("Glitch%20Icon.png", manifest)
                self.assertIn('sizes: "512x512"', manifest)

    def test_localized_docs_and_downloads_keep_complete_social_cards(self):
        docs_article = (ROOT / "apps/doc/src/app/[...segments]/page.tsx").read_text(encoding="utf-8")
        download_locale = (ROOT / "apps/download/src/app/[locale]/page.tsx").read_text(encoding="utf-8")
        self.assertIn("buildDocsPageMetadata", docs_article)
        self.assertIn("buildDownloadPageMetadata", download_locale)


if __name__ == "__main__":
    unittest.main()
