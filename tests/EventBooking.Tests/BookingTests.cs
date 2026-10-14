using EventBooking.Core;

namespace EventBooking.Tests;

public class BookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextWeek = Now.AddDays(7);
    private const string UserId = "user-1";

    private static Event CreateEvent(int capacity = 10)
    {
        var ev = Event.Create("organiser-1", "Tech Meetup", "Talks and pizza.", NextWeek, NextWeek.AddHours(3),
            "Europe/Lisbon", "Lisbon Congress Centre", "Praça das Indústrias, Lisbon", 38.70, -9.20, capacity, Now);
        ev.AddImage("cover.jpg");
        return ev;
    }

    private static Event CreatePublishedEvent(int capacity = 10)
    {
        var ev = CreateEvent(capacity);
        ev.Publish(Now);
        return ev;
    }

    private static Booking Book(Event ev, int confirmedBookings = 0, DateTimeOffset? now = null) =>
        Booking.Create(ev, UserId, confirmedBookings, hasConfirmedBooking: false, now ?? Now);

    // ---------- Booking ----------

    [Fact]
    public void Create_IsConfirmedStraightAway()
    {
        var booking = Book(CreatePublishedEvent());

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(UserId, booking.UserId);
        Assert.Equal(Now, booking.BookedAt);
        Assert.Null(booking.CancelledAt);
    }

    [Fact]
    public void Create_RejectsADraft()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Book(CreateEvent()));
        Assert.Contains("isn't open for booking", ex.Message);
    }

    [Fact]
    public void Create_RejectsACancelledEvent()
    {
        var ev = CreatePublishedEvent();
        ev.Cancel(Now);

        var ex = Assert.Throws<BusinessRuleException>(() => Book(ev));
        Assert.Contains("cancelled", ex.Message);
    }

    [Fact]
    public void Create_RejectsAnEventThatHasStarted()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Book(CreatePublishedEvent(), now: NextWeek));
        Assert.Contains("already started", ex.Message);
    }

    [Fact]
    public void Create_RejectsTheEventsOrganiser()
    {
        var ev = CreatePublishedEvent();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            Booking.Create(ev, ev.OrganiserId, 0, hasConfirmedBooking: false, Now));
        Assert.Contains("your own event", ex.Message);
    }

    [Fact]
    public void Create_RejectsASecondConfirmedBooking()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Booking.Create(CreatePublishedEvent(), UserId, 1, hasConfirmedBooking: true, Now));
        Assert.Contains("already booked", ex.Message);
    }

    [Fact]
    public void Create_RejectsAFullEvent_AndSuggestsComingBack()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Book(CreatePublishedEvent(capacity: 2), confirmedBookings: 2));
        Assert.Contains("currently full", ex.Message);
        Assert.Contains("check back later", ex.Message);
    }

    [Fact]
    public void Create_AllowsTheLastPlace()
    {
        var booking = Book(CreatePublishedEvent(capacity: 2), confirmedBookings: 1);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void Create_RejectsAMissingUser()
    {
        Assert.Throws<BusinessRuleException>(() =>
            Booking.Create(CreatePublishedEvent(), " ", 0, hasConfirmedBooking: false, Now));
    }

    [Fact]
    public void ReasonCannotBook_IsNullWhenBookingIsAllowed()
    {
        Assert.Null(Booking.ReasonCannotBook(CreatePublishedEvent(), UserId, 0, hasConfirmedBooking: false, Now));
    }

    [Theory]
    [InlineData(10, 3, 7)]
    [InlineData(10, 10, 0)]
    [InlineData(5, 8, 0)]
    public void PlacesLeft_IsCapacityMinusConfirmedBookings_NeverNegative(int capacity, int confirmed, int expected)
    {
        Assert.Equal(expected, CreatePublishedEvent(capacity).PlacesLeft(confirmed));
    }

    // ---------- Cancelling ----------

    [Fact]
    public void Cancel_MarksTheBookingCancelled()
    {
        var ev = CreatePublishedEvent();
        var booking = Book(ev);

        booking.Cancel(ev, Now.AddDays(1));

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(Now.AddDays(1), booking.CancelledAt);
    }

    [Fact]
    public void Cancel_RejectsABookingThatIsAlreadyCancelled()
    {
        var ev = CreatePublishedEvent();
        var booking = Book(ev);
        booking.Cancel(ev, Now);

        Assert.Throws<BusinessRuleException>(() => booking.Cancel(ev, Now));
    }

    [Fact]
    public void Cancel_RejectsOnceTheEventHasStarted()
    {
        var ev = CreatePublishedEvent();
        var booking = Book(ev);

        var ex = Assert.Throws<BusinessRuleException>(() => booking.Cancel(ev, NextWeek));
        Assert.Contains("started", ex.Message);
    }

    [Fact]
    public void Cancel_RejectsWhenTheEventWasCancelled()
    {
        var ev = CreatePublishedEvent();
        var booking = Book(ev);
        ev.Cancel(Now);

        Assert.Throws<BusinessRuleException>(() => booking.Cancel(ev, Now));
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }
}
