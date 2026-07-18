using System;
using System.Globalization;

namespace Glitch.Services
{
    internal sealed class GlitchAiTradingWindowStatus
    {
        public bool IsValid { get; set; }
        public bool IsSessionOpen { get; set; }
        public bool IsEntryAllowed { get; set; }
        public DateTime? MustFlatUtc { get; set; }
        public double SecondsUntilMustFlat { get; set; }
        public string Failure { get; set; }
    }

    internal static class GlitchAiTradingWindow
    {
        private static readonly TimeSpan EntryCloseLead = TimeSpan.FromMinutes(1);

        public static GlitchAiTradingWindowStatus Evaluate(
            DateTime nowUtc,
            string tradingStartTime,
            string tradingEndTime)
        {
            TimeSpan start;
            TimeSpan end;
            if (!TimeSpan.TryParse(tradingStartTime, CultureInfo.InvariantCulture, out start)
                || !TimeSpan.TryParse(tradingEndTime, CultureInfo.InvariantCulture, out end))
                return Invalid("trading_window_times_invalid");

            try
            {
                TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                DateTime utc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
                DateTime easternNow = TimeZoneInfo.ConvertTimeFromUtc(utc, eastern);
                bool open = IsSessionOpen(easternNow, start, end);
                DateTime? closeEastern = open ? ResolveCurrentSessionClose(easternNow, end) : (DateTime?)null;
                DateTime? closeUtc = closeEastern.HasValue
                    ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(closeEastern.Value, DateTimeKind.Unspecified), eastern)
                    : (DateTime?)null;
                double secondsUntilClose = closeUtc.HasValue
                    ? Math.Max(0, (closeUtc.Value - utc).TotalSeconds)
                    : 0;

                return new GlitchAiTradingWindowStatus
                {
                    IsValid = true,
                    IsSessionOpen = open,
                    IsEntryAllowed = open && secondsUntilClose > EntryCloseLead.TotalSeconds,
                    MustFlatUtc = closeUtc,
                    SecondsUntilMustFlat = secondsUntilClose
                };
            }
            catch (Exception ex)
            {
                return Invalid("trading_window_timezone_" + ex.GetType().Name);
            }
        }

        private static bool IsSessionOpen(DateTime easternNow, TimeSpan start, TimeSpan end)
        {
            TimeSpan time = easternNow.TimeOfDay;
            switch (easternNow.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    return false;
                case DayOfWeek.Sunday:
                    return time >= start;
                case DayOfWeek.Friday:
                    return time < end;
                default:
                    return time < end || time >= start;
            }
        }

        private static DateTime ResolveCurrentSessionClose(DateTime easternNow, TimeSpan end)
        {
            DateTime closeDate = easternNow.TimeOfDay < end
                ? easternNow.Date
                : easternNow.Date.AddDays(1);
            return closeDate.Add(end);
        }

        private static GlitchAiTradingWindowStatus Invalid(string failure)
        {
            return new GlitchAiTradingWindowStatus
            {
                IsValid = false,
                Failure = failure
            };
        }
    }
}
