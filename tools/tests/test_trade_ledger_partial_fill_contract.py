from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"
INSIGHTS = ADDON / "Services/Insights/GlitchTradeInsightsService.cs"
LEDGER = ADDON / "Services/Insights/GlitchTradeLedgerService.cs"
SUMMARY = ADDON / "UI/MainWindow/GlitchMainWindow.SummaryTab.partial.cs"
PERFORMANCE = ADDON / "UI/MainWindow/GlitchMainWindow.Performance.partial.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(text: str, start: str, end: str) -> str:
    start_index = text.index(start)
    end_index = text.index(end, start_index + len(start))
    return text[start_index:end_index]


def test_execution_batch_reaches_ledger_before_ui_journal_trimming():
    body = method_body(source(PERFORMANCE), "private void FlushPendingJournalEntries", "internal void RequestAnalyticsRefresh")
    merge_index = body.index("MergeTradeLedgerJournalBatch(batch, nowUtc)")
    trim_index = body.index("const int maxJournalEntries = 800")
    assert merge_index < trim_index
    assert "RefreshTradeLedgerFromJournal(nowUtc)" not in body


def test_summary_seeds_once_then_uses_incremental_ledger_truth():
    summary = source(SUMMARY)
    seed = method_body(summary, "private void RefreshTradeLedgerFromJournal", "private void MergeTradeLedgerJournalBatch")
    merge = method_body(summary, "private void MergeTradeLedgerJournalBatch", "private static GlitchTradeInsightsService.TradeJournalEvent")
    refresh = method_body(summary, "private void RefreshSummaryInsightsCore", "private IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> ApplySummaryScope")
    assert "_tradeExecutionAggregationInitialized" in seed
    assert "RebuildExecutionAggregationAndGetAll" in seed
    assert "MergeExecutionEventsAndGetAll(newEvents, contextEvents, nowUtc)" in merge
    assert "_tradeLedgerService.MergeAndGetAll(null, nowUtc)" in refresh
    assert "BuildSnapshot(journalEvents" not in refresh


def test_accumulator_counts_every_fragment_and_deduplicates_only_native_ids():
    insights = source(INSIGHTS)
    accumulator = method_body(insights, "internal sealed class ExecutionAccumulator", "private sealed class ExecutionEvent")
    close = method_body(insights, "private static TradeRoundTrip BuildClosedTrade", "private static void AccumulateExecutionCommission")
    assert "_seenExecutionIds.Add(BuildExecutionIdentityKey(evt))" in accumulator
    assert "BuildNoIdExecutionSignature" not in insights
    assert "BuildEntryOwnershipKey(evt)" in insights
    assert "state.Lots[0]" in insights
    assert "state.Lots.RemoveAt(0)" in insights
    assert "lot.EntryContracts += fillQuantity" in insights
    assert "lot.EntryNotional += fillQuantity * evt.Price" in insights
    assert "Contracts = entryContracts" in close
    assert "EntryPrice = entryPrice" in close
    assert "EntryOrderIdentity = state.EntryOrderIdentity" in close


def test_corrected_aggregate_replaces_partial_without_merging_reused_signal():
    ledger = source(LEDGER)
    merge = method_body(ledger, "private void MergeTradesUnsafe", "private IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> CompleteMergeUnsafe")
    lifecycle = method_body(ledger, "internal static bool AreSameTradeLifecycle", "internal static bool IsPreferredAggregate")
    preferred = method_body(ledger, "internal static bool IsPreferredAggregate", "private static string BuildExactDuplicateSignature")
    assert "AreSameTradeLifecycle(pair.Value, trade)" in merge
    assert "_ledgerById.Remove(lifecycleMatch.Value.Key)" in merge
    assert "TotalSeconds) > 5" in lifecycle
    assert "return laterEntry <= earlierExit" in lifecycle
    assert "incomingContracts > existingContracts" in preferred


def test_stable_identity_is_appended_without_breaking_legacy_rows():
    insights = source(INSIGHTS)
    ledger = source(LEDGER)
    assert 'if (key == "OID")' in insights
    assert '"OID",\n                    CleanToken(rawEntryOrderIdentity)' in insights
    assert "if (!string.IsNullOrWhiteSpace(rawOrderIdentity))" in insights
    assert "entry_order_identity" in ledger
    assert "parts.Length >= 21 ? parts[20] : string.Empty" in ledger
    assert "CleanToken(trade.EntryOrderIdentity)" in ledger
