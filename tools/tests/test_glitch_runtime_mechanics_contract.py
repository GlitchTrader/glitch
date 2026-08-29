from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ADDON = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn"
CONTROLLER = ADDON / "Services/Ai/GlitchAiAutoRuntimeController.cs"
PERFORMANCE = ADDON / "UI/MainWindow/GlitchMainWindow.Performance.partial.cs"
AI_TAB = ADDON / "UI/MainWindow/GlitchMainWindow.AiTab.partial.cs"
BRIDGE = ROOT / "ninjatrader/Glitch/Indicators/glitch/GlitchAnalyticsBridge.cs"
REPLAY = ADDON / "Services/Persistence/GlitchHistoricalSnapshotExporter.cs"
FUNDAMENTALS = (
    ADDON
    / "Services/FundamentalAnalysis/GlitchFundamentalAnalysisService.cs"
)


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def section(text: str, start: str, end: str) -> str:
    start_index = text.index(start)
    end_index = text.index(end, start_index + len(start))
    return text[start_index:end_index]


def test_ai_auto_owns_the_real_python_process_for_uv_environments():
    controller = source(CONTROLLER)

    assert "ResolveOwnedPythonExecutable(pythonPath, startInfo)" in controller
    assert 'Path.Combine(venvDirectory, "pyvenv.cfg")' in controller
    assert 'key.Equals("uv", StringComparison.OrdinalIgnoreCase)' in controller
    assert 'startInfo.EnvironmentVariables["VIRTUAL_ENV"]' in controller
    assert 'startInfo.EnvironmentVariables["PYTHONPATH"]' in controller
    assert "return basePythonPath;" in controller
    assert "process.Kill()" in controller


def test_journal_throttle_schedules_one_delayed_flush_without_dispatcher_spin():
    performance = source(PERFORMANCE)
    throttle = section(
        performance,
        "if (!force\n                && (nowUtc - _lastJournalFlushUtc)",
        "_lastJournalFlushUtc = nowUtc;",
    )

    assert "ScheduleJournalFlushAfterDelay(" in throttle
    assert "Dispatcher.BeginInvoke" not in throttle
    assert "await Task.Delay(delay);" in performance


def test_indicator_bridge_registers_once_across_state_transitions():
    bridge = source(BRIDGE)
    lifecycle = section(
        bridge,
        "else if (State == State.DataLoaded)",
        "protected override void OnBarUpdate()",
    )

    assert lifecycle.count(
        "GlitchBridgeBusCompat.RegisterBridge(_instrumentRoot, PublishToGlitchUi);"
    ) == 1
    assert "else if (State == State.Realtime)" in lifecycle
    assert "GlitchBridgeBusCompat.TouchBridge(" in lifecycle
    assert "GlitchBridgeBusCompat.UnregisterBridge(_instrumentRoot);" in lifecycle


def test_replay_bundle_streams_snapshots_without_holding_the_full_bundle_in_memory():
    replay = source(REPLAY)

    assert "using (var writer = new StreamWriter(" in replay
    assert "WriteReplaySnapshotArray(writer, pairs, useMarketPath: true);" in replay
    assert "WriteReplaySnapshotArray(writer, pairs, useMarketPath: false);" in replay
    assert "public string MarketPath { get; set; }" in replay
    assert "public string PortfolioPath { get; set; }" in replay
    assert "HasNonWhitespaceContent(entry.MarketPath)" in replay
    assert "HasNonWhitespaceContent(entry.PortfolioPath)" in replay
    assert "public string MarketJson { get; set; }" not in replay
    assert "public string PortfolioJson { get; set; }" not in replay


def test_ai_scope_reconciliation_does_not_render_before_the_tab_exists():
    ai_tab = source(AI_TAB)

    assert "policy?.ProfileAccountBindings?.Values?.Where(currentMasters.Contains)" in ai_tab
    assert "if (_aiScopeRowsHost != null)" in ai_tab
    assert "if (refresh && _aiFeedHost != null)" in ai_tab


def test_fred_dataset_release_dates_never_masquerade_as_verified_live_events():
    fundamentals = source(FUNDAMENTALS)
    official_news = section(
        fundamentals,
        "private List<string> BuildOfficialNewsLines(",
        "private static string BuildOfficialNewsText(",
    )
    poll_interval = section(
        fundamentals,
        "private static TimeSpan ResolveCalendarPollInterval(",
        "private static DateTime ResolveEventEndUtc(",
    )

    assert "List<EconomicEvent> verifiedEvents = sourceEvents" in official_news
    assert ".Where(IsVerifiedLiveEconomicEvent)" in official_news
    assert "relevantUpcoming = verifiedEvents" in official_news
    assert ".Where(IsVerifiedLiveEconomicEvent)" in poll_interval
    assert '!string.Equals(item.Source, "FRED"' in poll_interval
