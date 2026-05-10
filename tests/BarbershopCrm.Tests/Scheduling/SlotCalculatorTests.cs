using BarbershopCrm.Domain.Scheduling;
using FluentAssertions;

namespace BarbershopCrm.Tests.Scheduling;

public class SlotCalculatorTests
{
    private static readonly DateOnly Day = new(2026, 5, 10);
    private static readonly TimeOnly Open = new(9, 0);
    private static readonly TimeOnly Close = new(21, 0);

    private static MasterScheduleInput Master(int id, string name,
        TimeRange[] work, TimeRange[]? blocked = null, bool dayOff = false) =>
        new()
        {
            MasterId = id,
            MasterName = name,
            WorkRanges = work,
            BlockedRanges = blocked ?? Array.Empty<TimeRange>(),
            IsDayOff = dayOff,
        };

    [Fact]
    public void NoWorkRanges_ReturnsNoSlots()
    {
        var m = Master(1, "A", Array.Empty<TimeRange>());
        var slots = SlotCalculator.ComputeSlots(Day, 30, 15, Open, Close, m).ToList();
        slots.Should().BeEmpty();
    }

    [Fact]
    public void DayOff_ReturnsNoSlots()
    {
        var m = Master(1, "A", new[] { new TimeRange(new(10, 0), new(20, 0)) }, dayOff: true);
        var slots = SlotCalculator.ComputeSlots(Day, 30, 15, Open, Close, m).ToList();
        slots.Should().BeEmpty();
    }

    [Fact]
    public void OneShift_NoBlocks_ProducesSlotsOnStep()
    {
        var m = Master(1, "A", new[] { new TimeRange(new(10, 0), new(11, 0)) });
        // 60-min window, 30-min service, 15-min step => 10:00, 10:15, 10:30
        // last valid start where 30 min still fits before 11:00 is 10:30.
        var slots = SlotCalculator.ComputeSlots(Day, 30, 15, Open, Close, m).ToList();
        slots.Should().Equal(
            new DateTime(2026, 5, 10, 10, 0, 0),
            new DateTime(2026, 5, 10, 10, 15, 0),
            new DateTime(2026, 5, 10, 10, 30, 0)
        );
    }

    [Fact]
    public void LunchBreak_IsRespected()
    {
        var m = Master(1, "A",
            new[] { new TimeRange(new(10, 0), new(14, 0)) },
            new[] { new TimeRange(new(12, 0), new(13, 0)) }); // lunch 12-13
        // 30 min service, 15 step. Pre-lunch: 10:00..12:00 → 10:00, 10:15, 10:30, 10:45, 11:00, 11:15, 11:30 (last fits ends 12:00)
        // Post-lunch: 13:00..14:00 → 13:00, 13:15, 13:30
        var slots = SlotCalculator.ComputeSlots(Day, 30, 15, Open, Close, m).Select(s => s.ToString("HH:mm")).ToList();
        slots.Should().BeEquivalentTo(new[]
        {
            "10:00","10:15","10:30","10:45","11:00","11:15","11:30",
            "13:00","13:15","13:30"
        }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void ExistingBookings_SubtractedFromAvailability()
    {
        var m = Master(1, "A",
            new[] { new TimeRange(new(10, 0), new(13, 0)) },
            new[] { new TimeRange(new(11, 0), new(11, 30)) }); // booking 11:00-11:30
        // 30 min service, 15 step.
        // Free intervals: 10:00-11:00 (60 min), 11:30-13:00 (90 min)
        // Pre-booking starts: 10:00, 10:15, 10:30 (last that fits 30 min ≤ 11:00)
        // Post-booking starts: 11:30, 11:45, 12:00, 12:15, 12:30
        var slots = SlotCalculator.ComputeSlots(Day, 30, 15, Open, Close, m).Select(s => s.ToString("HH:mm")).ToList();
        slots.Should().Equal(new[]
        {
            "10:00","10:15","10:30",
            "11:30","11:45","12:00","12:15","12:30"
        });
    }

    [Fact]
    public void BranchClose_LimitsLastSlot()
    {
        var m = Master(1, "A", new[] { new TimeRange(new(10, 0), new(22, 0)) }); // master works till 22:00
        var slots = SlotCalculator.ComputeSlots(Day, 60, 30, Open, Close, m).ToList();
        // close=21:00, last valid 60-min slot starts at 20:00
        slots.Last().Should().Be(new DateTime(2026, 5, 10, 20, 0, 0));
    }

    [Fact]
    public void ServiceDuration_LongerThanWindow_ReturnsEmpty()
    {
        var m = Master(1, "A", new[] { new TimeRange(new(10, 0), new(10, 30)) });
        var slots = SlotCalculator.ComputeSlots(Day, 60, 15, Open, Close, m).ToList();
        slots.Should().BeEmpty();
    }

    [Fact]
    public void StartTime_QuantizedToStep()
    {
        // Master starts at 10:07 — first valid slot snaps to 10:15.
        var m = Master(1, "A", new[] { new TimeRange(new(10, 7), new(11, 0)) });
        var slots = SlotCalculator.ComputeSlots(Day, 30, 15, Open, Close, m).Select(s => s.ToString("HH:mm")).ToList();
        slots.Should().Equal("10:15", "10:30");
    }

    [Fact]
    public void MultipleWorkRanges_HandledIndependently()
    {
        var m = Master(1, "A", new[]
        {
            new TimeRange(new(9, 0), new(11, 0)),
            new TimeRange(new(15, 0), new(17, 0)),
        });
        var slots = SlotCalculator.ComputeSlots(Day, 60, 60, Open, Close, m).Select(s => s.ToString("HH:mm")).ToList();
        slots.Should().Equal("09:00", "10:00", "15:00", "16:00");
    }

    [Fact]
    public void OverlappingBlock_ClampedToWindow()
    {
        var m = Master(1, "A",
            new[] { new TimeRange(new(10, 0), new(13, 0)) },
            new[] { new TimeRange(new(8, 0), new(11, 0)) }); // block extends before window
        var slots = SlotCalculator.ComputeSlots(Day, 30, 30, Open, Close, m).Select(s => s.ToString("HH:mm")).ToList();
        // Available 11:00..13:00, 30-min service, 30 step → 11:00, 11:30, 12:00, 12:30
        slots.Should().Equal("11:00", "11:30", "12:00", "12:30");
    }

    [Fact]
    public void WorkRangeOutsideBranchHours_Clamped()
    {
        var m = Master(1, "A", new[] { new TimeRange(new(7, 0), new(23, 0)) });
        var slots = SlotCalculator.ComputeSlots(Day, 60, 60, Open, Close, m).ToList();
        slots.First().Hour.Should().Be(9);
        slots.Last().Hour.Should().Be(20); // close 21:00, last 60-min slot starts at 20:00
    }
}
