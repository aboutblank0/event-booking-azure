using EventBooking.Core;

namespace EventBooking.Tests;

/// <summary>Tests for the rules in docs/event-design.md. Rule numbers are in the test names' comments.</summary>
public class EventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextWeek = Now.AddDays(7);

    /// <summary>A valid draft event. Tests override only the argument they care about.</summary>
    private static Event CreateEvent(
        string name = "Tech Meetup",
        string description = "Talks and pizza.",
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        string timeZoneId = "Europe/Lisbon",
        string venueName = "Lisbon Congress Centre",
        string address = "Praça das Indústrias, Lisbon",
        double latitude = 38.70,
        double longitude = -9.20,
        int capacity = 100)
    {
        var start = startsAt ?? NextWeek;
        return Event.Create("organiser-1", name, description, start, endsAt ?? start.AddHours(3),
            timeZoneId, venueName, address, latitude, longitude, capacity, Now);
    }

    private static Event CreatePublishedEvent()
    {
        var ev = CreateEvent();
        ev.AddImage("cover.jpg");
        ev.Publish(Now);
        return ev;
    }

    private static Event CreateCancelledEvent()
    {
        var ev = CreatePublishedEvent();
        ev.Cancel(Now);
        return ev;
    }

    // ---------- Creating (rules 1, 3-7, 11) ----------

    [Fact]
    public void Create_ValidEvent_IsDraftWithAllValuesSet()
    {
        var ev = CreateEvent();

        Assert.Equal(EventStatus.Draft, ev.Status);
        Assert.Equal("organiser-1", ev.OrganiserId);
        Assert.Equal("Tech Meetup", ev.Name);
        Assert.Equal(NextWeek, ev.StartsAt);
        Assert.Equal(100, ev.Capacity);
        Assert.Equal(Now, ev.CreatedAt);
        Assert.Null(ev.CancelledAt);
        Assert.Empty(ev.Images);
    }

    [Fact]
    public void Create_TrimsText()
    {
        var ev = CreateEvent(name: "  Tech Meetup  ");

        Assert.Equal("Tech Meetup", ev.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsMissingRequiredText(string blank)
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent(name: blank));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(description: blank));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(venueName: blank));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(address: blank));
    }

    [Fact]
    public void Create_RejectsMissingOrganiser()
    {
        Assert.Throws<BusinessRuleException>(() => Event.Create("", "Name", "Description",
            NextWeek, NextWeek.AddHours(1), "Europe/Lisbon", "Venue", "Address", 0, 0, 10, Now));
    }

    [Fact]
    public void Create_AcceptsTextAtMaxLength_RejectsOneOver()
    {
        CreateEvent(name: new string('a', Event.NameMaxLength));

        Assert.Throws<BusinessRuleException>(() => CreateEvent(name: new string('a', Event.NameMaxLength + 1)));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(description: new string('a', Event.DescriptionMaxLength + 1)));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(venueName: new string('a', Event.VenueNameMaxLength + 1)));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(address: new string('a', Event.AddressMaxLength + 1)));
    }

    [Fact]
    public void Create_RejectsEndAtOrBeforeStart() // rule 3
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent(startsAt: NextWeek, endsAt: NextWeek));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(startsAt: NextWeek, endsAt: NextWeek.AddMinutes(-1)));
    }

    [Fact]
    public void Create_RejectsStartInThePast() // rule 4
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent(startsAt: Now.AddMinutes(-1)));
    }

    [Fact]
    public void Create_RejectsZeroOrNegativeCapacity() // rule 5
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent(capacity: 0));
        Assert.Throws<BusinessRuleException>(() => CreateEvent(capacity: -1));
    }

    [Theory] // rule 6
    [InlineData(-90.0, -180.0)]
    [InlineData(90.0, 180.0)]
    public void Create_AcceptsCoordinatesAtTheLimits(double latitude, double longitude)
    {
        CreateEvent(latitude: latitude, longitude: longitude);
    }

    [Theory] // rule 6
    [InlineData(-90.1, 0.0)]
    [InlineData(90.1, 0.0)]
    [InlineData(0.0, -180.1)]
    [InlineData(0.0, 180.1)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    public void Create_RejectsInvalidCoordinates(double latitude, double longitude)
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent(latitude: latitude, longitude: longitude));
    }

    [Theory] // rule 7
    [InlineData("")]
    [InlineData("Not/AZone")]
    [InlineData("GMT Standard Time")] // a Windows name, not IANA
    public void Create_RejectsInvalidTimeZone(string timeZoneId)
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent(timeZoneId: timeZoneId));
    }

    // ---------- Images (rules 8-10) ----------

    [Fact]
    public void AddImage_GivesEachImageAHigherSortOrder() // rule 10
    {
        var ev = CreateEvent();

        ev.AddImage("a.jpg");
        ev.AddImage("b.jpg");
        ev.RemoveImage("a.jpg");
        ev.AddImage("c.jpg");

        var sortOrders = ev.Images.Select(i => i.SortOrder).ToList();
        Assert.Equal(sortOrders.Count, sortOrders.Distinct().Count());
        Assert.True(ev.Images.Single(i => i.BlobName == "c.jpg").SortOrder >
                    ev.Images.Single(i => i.BlobName == "b.jpg").SortOrder);
    }

    [Fact]
    public void AddImage_AllowsFiveImages_RejectsSixth() // rule 9
    {
        var ev = CreateEvent();
        for (var i = 1; i <= Event.MaxImages; i++)
            ev.AddImage($"{i}.jpg");

        Assert.Equal(5, ev.Images.Count);
        Assert.Throws<BusinessRuleException>(() => ev.AddImage("6.jpg"));
    }

    [Fact]
    public void AddImage_RejectsDuplicateOrBlankBlobName()
    {
        var ev = CreateEvent();
        ev.AddImage("a.jpg");

        Assert.Throws<BusinessRuleException>(() => ev.AddImage("a.jpg"));
        Assert.Throws<BusinessRuleException>(() => ev.AddImage(" "));
    }

    [Fact]
    public void RemoveImage_RejectsUnknownImage()
    {
        var ev = CreateEvent();

        Assert.Throws<BusinessRuleException>(() => ev.RemoveImage("missing.jpg"));
    }

    [Fact]
    public void RemoveImage_DraftCanRemoveItsLastImage()
    {
        var ev = CreateEvent();
        ev.AddImage("a.jpg");

        ev.RemoveImage("a.jpg");

        Assert.Empty(ev.Images);
    }

    [Fact]
    public void RemoveImage_PublishedEventMustKeepOneImage() // rule 8
    {
        var ev = CreatePublishedEvent();

        Assert.Throws<BusinessRuleException>(() => ev.RemoveImage("cover.jpg"));
    }

    // ---------- Publishing (rules 2, 4, 8, 12) ----------

    [Fact]
    public void Publish_DraftWithImage_BecomesPublished()
    {
        var ev = CreatePublishedEvent();

        Assert.Equal(EventStatus.Published, ev.Status);
    }

    [Fact]
    public void Publish_RejectsEventWithoutImages() // rule 8
    {
        var ev = CreateEvent();

        Assert.Throws<BusinessRuleException>(() => ev.Publish(Now));
    }

    [Fact]
    public void Publish_AllowsStartExactlyOneYearAway_RejectsLater() // rule 2
    {
        var oneYear = CreateEvent(startsAt: Now.AddYears(1));
        oneYear.AddImage("a.jpg");
        oneYear.Publish(Now);

        var tooFar = CreateEvent(startsAt: Now.AddYears(1).AddMinutes(1));
        tooFar.AddImage("a.jpg");
        Assert.Throws<BusinessRuleException>(() => tooFar.Publish(Now));

        // Creating a draft that far ahead is fine; it can be published once within a year.
        tooFar.Publish(Now.AddDays(1));
    }

    [Fact]
    public void Publish_RejectsDraftWhoseStartHasPassed() // rule 4
    {
        var ev = CreateEvent(startsAt: NextWeek);
        ev.AddImage("a.jpg");

        Assert.Throws<BusinessRuleException>(() => ev.Publish(NextWeek.AddDays(1)));
    }

    [Fact]
    public void Publish_RejectsEventThatIsNotDraft() // rule 12
    {
        Assert.Throws<BusinessRuleException>(() => CreatePublishedEvent().Publish(Now));
        Assert.Throws<BusinessRuleException>(() => CreateCancelledEvent().Publish(Now));
    }

    // ---------- Cancelling and deleting (rules 12-14) ----------

    [Fact]
    public void Cancel_PublishedEvent_SetsStatusAndCancelledAt()
    {
        var ev = CreatePublishedEvent();

        ev.Cancel(Now.AddDays(1));

        Assert.Equal(EventStatus.Cancelled, ev.Status);
        Assert.Equal(Now.AddDays(1), ev.CancelledAt);
    }

    [Fact]
    public void Cancel_RejectsDraftAndAlreadyCancelled()
    {
        Assert.Throws<BusinessRuleException>(() => CreateEvent().Cancel(Now));
        Assert.Throws<BusinessRuleException>(() => CreateCancelledEvent().Cancel(Now));
    }

    [Fact]
    public void Delete_Draft_BecomesDeleted()
    {
        var ev = CreateEvent();

        ev.Delete();

        Assert.Equal(EventStatus.Deleted, ev.Status);
    }

    [Fact]
    public void Delete_RejectsPublishedEvent() // rule 14
    {
        Assert.Throws<BusinessRuleException>(() => CreatePublishedEvent().Delete());
    }

    [Fact]
    public void CancelledAndDeletedEvents_CannotBeChanged() // rule 13
    {
        var deleted = CreateEvent();
        deleted.Delete();

        foreach (var ev in new[] { CreateCancelledEvent(), deleted })
        {
            Assert.Throws<BusinessRuleException>(() => ev.UpdateDetails("New name", "New description"));
            Assert.Throws<BusinessRuleException>(() => ev.ChangeVenue("New venue", "New address", 0, 0));
            Assert.Throws<BusinessRuleException>(() => ev.ChangeCapacity(50, 0));
            Assert.Throws<BusinessRuleException>(() => ev.ChangeSchedule(NextWeek, NextWeek.AddHours(1), "Europe/Lisbon", Now));
            Assert.Throws<BusinessRuleException>(() => ev.AddImage("new.jpg"));
            Assert.Throws<BusinessRuleException>(() => ev.Delete());
        }
    }

    // ---------- Permissions (rule 17) ----------

    [Fact]
    public void IsOrganisedBy_OnlyTrueForTheOrganiser()
    {
        var ev = CreateEvent();

        Assert.True(ev.IsOrganisedBy("organiser-1"));
        Assert.False(ev.IsOrganisedBy("someone-else"));
    }

    // ---------- Editing (rules 19-21) ----------

    [Fact]
    public void ChangeSchedule_AllowedWhileDraft()
    {
        var ev = CreateEvent();

        ev.ChangeSchedule(NextWeek.AddDays(1), NextWeek.AddDays(2), "Europe/London", Now);

        Assert.Equal(NextWeek.AddDays(1), ev.StartsAt);
        Assert.Equal("Europe/London", ev.TimeZoneId);
    }

    [Fact]
    public void ChangeSchedule_StillChecksDateRules()
    {
        var ev = CreateEvent();

        Assert.Throws<BusinessRuleException>(() => ev.ChangeSchedule(Now.AddDays(-1), NextWeek, "Europe/Lisbon", Now));
        Assert.Throws<BusinessRuleException>(() => ev.ChangeSchedule(NextWeek, NextWeek, "Europe/Lisbon", Now));
    }

    [Fact]
    public void ChangeSchedule_RejectedOncePublished() // rule 19
    {
        var ev = CreatePublishedEvent();

        Assert.Throws<BusinessRuleException>(() =>
            ev.ChangeSchedule(NextWeek.AddDays(1), NextWeek.AddDays(2), "Europe/Lisbon", Now));
    }

    [Fact]
    public void ChangeCapacity_CannotGoBelowConfirmedBookings() // rule 20
    {
        var ev = CreatePublishedEvent();

        ev.ChangeCapacity(40, confirmedBookings: 40);
        Assert.Equal(40, ev.Capacity);

        Assert.Throws<BusinessRuleException>(() => ev.ChangeCapacity(39, confirmedBookings: 40));
    }

    [Fact]
    public void ChangeVenue_AllowedOncePublished() // rule 21
    {
        var ev = CreatePublishedEvent();

        ev.ChangeVenue("New Venue", "New Address", 41.15, -8.61);

        Assert.Equal("New Venue", ev.VenueName);
        Assert.Equal(41.15, ev.Latitude);
    }

    [Fact]
    public void UpdateDetails_AllowedOncePublished()
    {
        var ev = CreatePublishedEvent();

        ev.UpdateDetails("New name", "New description");

        Assert.Equal("New name", ev.Name);
    }
}
