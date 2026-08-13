import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
BRIDGE = ROOT / "ninjatrader" / "Glitch" / "Indicators" / "glitch" / "GlitchAnalyticsBridge.cs"


class MarketQuoteContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = BRIDGE.read_text(encoding="utf-8-sig")

    def test_bid_and_ask_updates_are_timestamped(self) -> None:
        self.assertIn("_lastBidUpdateUtc = nowUtc;", self.source)
        self.assertIn("_lastAskUpdateUtc = nowUtc;", self.source)

    def test_descriptive_liquidity_exposes_quote_and_spread_context(self) -> None:
        for field in (
            'best_bid',
            'best_ask',
            'spread_points',
            'spread_ticks',
            'last_quote_age_seconds',
            'last_depth_age_seconds',
        ):
            with self.subTest(field=field):
                self.assertIn(field, self.source)
        self.assertIn("spreadPoints.Value / tickSize.Value", self.source)
        self.assertIn("quoteUpdateUtc.HasValue", self.source)


if __name__ == "__main__":
    unittest.main()
