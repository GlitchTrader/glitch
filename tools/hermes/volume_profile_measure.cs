using System;
using System.Diagnostics;

public static class VolumeProfileMeasure
{
    public static int Main()
    {
        var clock = Stopwatch.StartNew();
        NinjaTrader.NinjaScript.Indicators.GlitchVolumeAtPrice.SelfCheck();
        clock.Stop();

        var profile = new NinjaTrader.NinjaScript.Indicators.GlitchVolumeAtPrice();
        var start = new DateTime(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 200; i++)
            profile.AddClassified(100 + (i % 40) * 0.25, 1, 1, 0.25, "NYC|2026093008", start, true);
        var serialize = Stopwatch.StartNew();
        string json = profile.BuildJson(0.25, "NYC|2026093008", 110, 100, 112, 98, 100);
        serialize.Stop();
        Console.WriteLine("self_check_ms=" + clock.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("profile_bytes=" + System.Text.Encoding.UTF8.GetByteCount(json).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("serialize_ms=" + serialize.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        var wide = new NinjaTrader.NinjaScript.Indicators.GlitchVolumeAtPrice();
        var wideStart = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i <= 800; i++)
            wide.AddClassified(i * 0.25, i == 0 ? 100 : 1, 0, 0.25, "NYC|2026093008", wideStart, false);
        var wideClock = Stopwatch.StartNew();
        string wideJson = wide.BuildJson(0.25, "NYC|2026093008", 200, 0.25, null, null, 1);
        wideClock.Stop();
        Console.WriteLine("capped_profile_bytes=" + System.Text.Encoding.UTF8.GetByteCount(wideJson).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("capped_serialize_ms=" + wideClock.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    }
}
