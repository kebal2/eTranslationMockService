using eTranslationMockService.Services;

using Xunit;

namespace eTranslationMockService.Tests;

/// <summary>
/// How long callbacks take at each status level, without waiting for them.
/// </summary>
public class DeliveryScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static TextCallbackRequest Request(string code) =>
        new(new Uri("http://localhost:9999/callback"), "translated", code, null, "EN", new[] { "HU" });

    [Theory]
    [InlineData(StatusLevel.Normal, 0, 0.5)]
    [InlineData(StatusLevel.HeavyLoad, 50, 100)]
    [InlineData(StatusLevel.CriticalLoad, 120, 300)]
    public void Each_level_delays_within_its_range(StatusLevel level, double minSeconds, double maxSeconds)
    {
        var random = new Random(42);

        for (var i = 0; i < 200; i++)
        {
            var delay = DeliverySchedule.Delay(level, random)!.Value;

            Assert.InRange(delay.TotalSeconds, minSeconds, maxSeconds);
        }
    }

    [Fact]
    public void A_blocked_service_has_no_delivery_time()
    {
        Assert.Null(DeliverySchedule.Delay(StatusLevel.Blocked, new Random(42)));
    }

    [Fact]
    public void A_callback_under_heavy_load_is_not_delivered_before_its_time()
    {
        var schedule = new DeliverySchedule(new Random(42));
        var delivery = schedule.Add(Request("1"), StatusLevel.HeavyLoad, Now);

        Assert.Empty(schedule.TakeDue(StatusLevel.HeavyLoad, Now.AddSeconds(49)));
        Assert.Equal(delivery, schedule.Find("1"));

        Assert.Single(schedule.TakeDue(StatusLevel.HeavyLoad, Now.AddSeconds(101)));
        Assert.Null(schedule.Find("1"));
    }

    [Fact]
    public void The_delay_is_the_one_of_the_level_the_callback_was_queued_at()
    {
        var schedule = new DeliverySchedule(new Random(42));
        schedule.Add(Request("1"), StatusLevel.CriticalLoad, Now);

        // Recovering does not speed up what is already in the queue.
        Assert.Empty(schedule.TakeDue(StatusLevel.Normal, Now.AddSeconds(100)));
        Assert.Single(schedule.TakeDue(StatusLevel.Normal, Now.AddMinutes(6)));
    }

    [Fact]
    public void A_blocked_service_holds_callbacks_however_long_it_takes()
    {
        var schedule = new DeliverySchedule(new Random(42));
        var delivery = schedule.Add(Request("1"), StatusLevel.Blocked, Now);

        Assert.Null(delivery.DueAt);
        Assert.Empty(schedule.TakeDue(StatusLevel.Blocked, Now.AddDays(1)));
        Assert.Equal(1, schedule.Count);
    }

    [Fact]
    public void Blocking_also_holds_callbacks_that_were_already_due()
    {
        var schedule = new DeliverySchedule(new Random(42));
        schedule.Add(Request("1"), StatusLevel.Normal, Now);

        Assert.Empty(schedule.TakeDue(StatusLevel.Blocked, Now.AddSeconds(10)));
        Assert.Single(schedule.TakeDue(StatusLevel.Normal, Now.AddSeconds(10)));
    }

    [Fact]
    public void Held_callbacks_are_scheduled_at_the_level_the_service_is_unblocked_to()
    {
        var schedule = new DeliverySchedule(new Random(42));
        schedule.Add(Request("1"), StatusLevel.Blocked, Now);

        var unblocked = Now.AddHours(1);

        Assert.Empty(schedule.TakeDue(StatusLevel.HeavyLoad, unblocked));

        var dueAt = schedule.Find("1")!.DueAt!.Value;

        Assert.InRange((dueAt - unblocked).TotalSeconds, 50, 100);
        Assert.Single(schedule.TakeDue(StatusLevel.HeavyLoad, dueAt));
    }
}
