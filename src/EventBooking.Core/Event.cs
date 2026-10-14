namespace EventBooking.Core;

/// <summary>
/// An event that users can book. Setters are private, so every change goes through a method
/// that checks the rules.
/// </summary>
public class Event
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 5000;
    public const int VenueNameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int MaxImages = 5;

    // EF Core fills this field directly; outside code must use AddImage.
    private readonly List<EventImage> _images = [];

    // For EF Core when loading from the database. Private, so the app must use Create().
    private Event() { }

    public int Id { get; private set; }
    public string OrganiserId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public EventStatus Status { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string TimeZoneId { get; private set; } = "";
    public string VenueName { get; private set; } = "";
    public string Address { get; private set; } = "";
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public int Capacity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Concurrency token. SQL Server changes it on every update.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Not sorted; order by <see cref="EventImage.SortOrder"/> when displaying.</summary>
    public IReadOnlyCollection<EventImage> Images => _images;

    /// <summary>Creates a new Draft event.</summary>
    public static Event Create(
        string organiserId,
        string name,
        string description,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string timeZoneId,
        string venueName,
        string address,
        double latitude,
        double longitude,
        int capacity,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(organiserId))
            throw new BusinessRuleException("An event must have an organiser.");

        var ev = new Event
        {
            OrganiserId = organiserId,
            Status = EventStatus.Draft,
            CreatedAt = now,
        };
        ev.SetDetails(name, description);
        ev.SetSchedule(startsAt, endsAt, timeZoneId, now);
        ev.SetVenue(venueName, address, latitude, longitude);
        ev.SetCapacity(capacity, confirmedBookings: 0);
        return ev;
    }

    public bool IsOrganisedBy(string userId) => OrganiserId == userId;

    public void UpdateDetails(string name, string description)
    {
        EnsureEditable();
        SetDetails(name, description);
    }

    /// <summary>Dates can only change while the event is a Draft.</summary>
    public void ChangeSchedule(DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId, DateTimeOffset now)
    {
        EnsureEditable();
        if (Status != EventStatus.Draft)
            throw new BusinessRuleException("The date and time can't be changed once the event is published.");

        SetSchedule(startsAt, endsAt, timeZoneId, now);
    }

    public void ChangeVenue(string venueName, string address, double latitude, double longitude)
    {
        EnsureEditable();
        SetVenue(venueName, address, latitude, longitude);
    }

    /// <summary>Capacity can't go below the number of confirmed bookings.</summary>
    public void ChangeCapacity(int capacity, int confirmedBookings)
    {
        EnsureEditable();
        SetCapacity(capacity, confirmedBookings);
    }

    /// <summary>Adds an image at the end of the display order.</summary>
    public void AddImage(string blobName)
    {
        EnsureEditable();

        if (string.IsNullOrWhiteSpace(blobName))
            throw new BusinessRuleException("An image must have a blob name.");
        if (_images.Count >= MaxImages)
            throw new BusinessRuleException($"An event can have at most {MaxImages} images.");
        if (_images.Any(i => i.BlobName == blobName))
            throw new BusinessRuleException("This image has already been added to the event.");

        // Always one higher than the current highest, so SortOrder values stay unique.
        var sortOrder = _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1;
        _images.Add(new EventImage(blobName, sortOrder));
    }

    public void RemoveImage(string blobName)
    {
        EnsureEditable();

        var image = _images.FirstOrDefault(i => i.BlobName == blobName)
            ?? throw new BusinessRuleException("This image is not part of the event.");

        if (Status == EventStatus.Published && _images.Count == 1)
            throw new BusinessRuleException("A published event must have at least one image.");

        _images.Remove(image);
    }

    /// <summary>Makes the event visible to other users.</summary>
    public void Publish(DateTimeOffset now)
    {
        if (Status != EventStatus.Draft)
            throw new BusinessRuleException("Only a draft event can be published.");
        if (_images.Count == 0)
            throw new BusinessRuleException("An event must have at least one image before it is published.");
        if (StartsAt <= now)
            throw new BusinessRuleException("An event that has already started can't be published.");
        if (StartsAt > now.AddYears(1))
            throw new BusinessRuleException("An event can't be published more than a year before it starts.");

        Status = EventStatus.Published;
    }

    /// <summary>Only published events can be cancelled; drafts are deleted instead.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
            throw new BusinessRuleException("Only a published event can be cancelled.");

        Status = EventStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>Only drafts can be deleted, since nobody can have booked them.</summary>
    public void Delete()
    {
        if (Status != EventStatus.Draft)
            throw new BusinessRuleException("Only a draft event can be deleted. Cancel it instead.");

        Status = EventStatus.Deleted;
    }

    private void EnsureEditable()
    {
        if (Status is EventStatus.Cancelled or EventStatus.Deleted)
            throw new BusinessRuleException("A cancelled or deleted event can't be changed.");
    }

    private void SetDetails(string name, string description)
    {
        Name = TextRules.Required(name, nameof(Name), NameMaxLength);
        Description = TextRules.Required(description, nameof(Description), DescriptionMaxLength);
    }

    private void SetSchedule(DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId, DateTimeOffset now)
    {
        if (startsAt <= now)
            throw new BusinessRuleException("An event can't start in the past.");
        if (endsAt <= startsAt)
            throw new BusinessRuleException("An event must end after it starts.");
        if (!IsIanaTimeZone(timeZoneId))
            throw new BusinessRuleException($"'{timeZoneId}' is not a valid time zone.");

        StartsAt = startsAt;
        EndsAt = endsAt;
        TimeZoneId = timeZoneId;
    }

    private void SetVenue(string venueName, string address, double latitude, double longitude)
    {
        // Written this way (not "< -90 || > 90") so NaN is rejected too.
        if (latitude is not (>= -90 and <= 90))
            throw new BusinessRuleException("Latitude must be between -90 and 90.");
        if (longitude is not (>= -180 and <= 180))
            throw new BusinessRuleException("Longitude must be between -180 and 180.");

        VenueName = TextRules.Required(venueName, nameof(VenueName), VenueNameMaxLength);
        Address = TextRules.Required(address, nameof(Address), AddressMaxLength);
        Latitude = latitude;
        Longitude = longitude;
    }

    private void SetCapacity(int capacity, int confirmedBookings)
    {
        if (capacity <= 0)
            throw new BusinessRuleException("Capacity must be greater than 0.");
        if (capacity < confirmedBookings)
            throw new BusinessRuleException(
                $"Capacity can't be lower than the {confirmedBookings} bookings already confirmed.");

        Capacity = capacity;
    }

    // Only IANA names like "Europe/Lisbon"; .NET also accepts Windows names like "GMT Standard Time".
    private static bool IsIanaTimeZone(string timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
        && zone.HasIanaId;
}
