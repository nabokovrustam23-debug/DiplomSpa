namespace BarbershopCrm.Domain.Scheduling;

public readonly record struct TimeRange(TimeOnly Start, TimeOnly End)
{
    public int DurationMinutes => (int)(End - Start).TotalMinutes;

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}

public readonly record struct FreeSlot(int MasterId, DateTime StartDateTime);

public sealed class MasterScheduleInput
{
    public int MasterId { get; init; }
    public string MasterName { get; init; } = string.Empty;
    public IReadOnlyList<TimeRange> WorkRanges { get; init; } = Array.Empty<TimeRange>();
    public IReadOnlyList<TimeRange> BlockedRanges { get; init; } = Array.Empty<TimeRange>();
    public bool IsDayOff { get; init; }
}

/// <summary>
/// Pure calculator. No DB, no HTTP — only TimeOnly/DateTime arithmetic.
/// Given a master's work intervals and blocked intervals (lunch + bookings),
/// produces appointment slots of a fixed service duration on a granular step.
/// </summary>
public static class SlotCalculator
{
    /// <summary>
    /// Compute slot start times for one master on a given date.
    /// </summary>
    /// <param name="date">Day to compute slots for.</param>
    /// <param name="serviceDurationMinutes">Required service duration.</param>
    /// <param name="stepMinutes">Slot step (e.g. 15 minutes).</param>
    /// <param name="branchOpen">Branch opening time.</param>
    /// <param name="branchClose">Branch closing time.</param>
    /// <param name="master">Master schedule data for this date.</param>
    /// <returns>Slot start times that completely fit a free interval.</returns>
    public static IEnumerable<DateTime> ComputeSlots(
        DateOnly date,
        int serviceDurationMinutes,
        int stepMinutes,
        TimeOnly branchOpen,
        TimeOnly branchClose,
        MasterScheduleInput master)
    {
        if (serviceDurationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(serviceDurationMinutes));
        if (stepMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepMinutes));
        if (branchClose <= branchOpen)
            yield break;
        if (master.IsDayOff || master.WorkRanges.Count == 0)
            yield break;

        var dayStart = new DateTime(date.Year, date.Month, date.Day, branchOpen.Hour, branchOpen.Minute, 0, DateTimeKind.Unspecified);

        foreach (var work in master.WorkRanges)
        {
            // clamp work range to branch open/close
            var winStart = work.Start < branchOpen ? branchOpen : work.Start;
            var winEnd = work.End > branchClose ? branchClose : work.End;
            if (winEnd <= winStart) continue;

            // build free sub-intervals by subtracting blocked ranges
            var blocks = master.BlockedRanges
                .Where(b => b.Start < winEnd && b.End > winStart)
                .OrderBy(b => b.Start)
                .ToList();

            var cursor = winStart;
            foreach (var block in blocks)
            {
                var bStart = block.Start < winStart ? winStart : block.Start;
                var bEnd = block.End > winEnd ? winEnd : block.End;

                if (cursor < bStart)
                {
                    foreach (var s in EnumerateSlots(date, cursor, bStart, serviceDurationMinutes, stepMinutes))
                        yield return s;
                }
                if (bEnd > cursor) cursor = bEnd;
            }
            if (cursor < winEnd)
            {
                foreach (var s in EnumerateSlots(date, cursor, winEnd, serviceDurationMinutes, stepMinutes))
                    yield return s;
            }
        }
    }

    private static IEnumerable<DateTime> EnumerateSlots(
        DateOnly date, TimeOnly from, TimeOnly until, int durationMinutes, int stepMinutes)
    {
        // Quantize start to the nearest step from `from` (round up).
        var startMinutes = from.Hour * 60 + from.Minute;
        var rem = startMinutes % stepMinutes;
        if (rem != 0) startMinutes += stepMinutes - rem;

        var windowEndMinutes = until.Hour * 60 + until.Minute;
        var lastValidStart = windowEndMinutes - durationMinutes;

        for (var m = startMinutes; m <= lastValidStart; m += stepMinutes)
        {
            var hh = m / 60;
            var mm = m % 60;
            yield return new DateTime(date.Year, date.Month, date.Day, hh, mm, 0, DateTimeKind.Unspecified);
        }
    }
}
