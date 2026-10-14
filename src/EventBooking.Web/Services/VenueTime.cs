using System.Globalization;
using EventBooking.Core;

namespace EventBooking.Web.Services;

/// <summary>Converts between exact moments and the clock time at an event's venue.</summary>
public static class VenueTime
{
    /// <summary>IANA time zone names such as "Europe/Lisbon", sorted for a dropdown.</summary>
    public static IReadOnlyList<string> TimeZoneIds { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Where(zone => zone.HasIanaId)
        .Select(zone => zone.Id)
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>The exact moment when the venue's clocks show <paramref name="local"/>.</summary>
    public static DateTimeOffset ToMoment(DateTime local, string timeZoneId)
    {
        var zone = FindZone(timeZoneId);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
            throw new BusinessRuleException(
                $"{local:HH:mm} doesn't exist on {Format(local, "d MMM yyyy")} in {timeZoneId} because the clocks go forward.");

        // When the clocks go back, a time happens twice; this picks the second (standard time).
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>What the venue's clocks show at <paramref name="moment"/>.</summary>
    public static DateTime ToLocal(DateTimeOffset moment, string timeZoneId) =>
        TimeZoneInfo.ConvertTime(moment, FindZone(timeZoneId)).DateTime;

    /// <summary>
    /// E.g. "Sat 14 Nov 2026, 19:00 – 22:00", or with both dates when the event runs past midnight.
    /// </summary>
    public static string FormatRange(DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId)
    {
        var start = ToLocal(startsAt, timeZoneId);
        var end = ToLocal(endsAt, timeZoneId);
        const string date = "ddd d MMM yyyy";
        return start.Date == end.Date
            ? $"{Format(start, date)}, {Format(start, "HH:mm")} – {Format(end, "HH:mm")}"
            : $"{Format(start, date)}, {Format(start, "HH:mm")} – {Format(end, date)}, {Format(end, "HH:mm")}";
    }

    // Invariant culture, so day and month names don't depend on the server's language settings.
    private static string Format(DateTime value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static TimeZoneInfo FindZone(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : throw new BusinessRuleException($"'{timeZoneId}' is not a valid time zone.");
}
