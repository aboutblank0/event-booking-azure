namespace EventBooking.Core;

/// <summary>
/// An event that users can book. The rules are described in docs/event-design.md.
/// All properties have private setters: every change goes through a method, so the rules
/// are always checked, and there is one place to hook in notifications later.
/// </summary>
public class Event
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 5000;
    public const int VenueNameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int MaxImages = 5;

    // EF Core fills this field directly when loading images; outside code only sees the
    // read-only Images property, so images can't be added without going through AddImage.
    private readonly List<EventImage> _images = [];

    // EF Core needs a parameterless constructor to create objects when loading from the
    // database. It can be private, which forces the rest of the app to use Create().
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

    /// <summary>Creates a new Draft event (rule 11).</summary>
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

    /// <summary>Dates can only change while the event is a Draft (rule 19).</summary>
    public void ChangeSchedule(DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId, DateTimeOffset now)
    {
        EnsureEditable();
        if (Status != EventStatus.Draft)
            throw new BusinessRuleException("The date and time can't be changed once the event is published.");

        SetSchedule(startsAt, endsAt, timeZoneId, now);
    }

    /// <summary>The venue can change at any point while the event is editable (rule 21).</summary>
    public void ChangeVenue(string venueName, string address, double latitude, double longitude)
    {
        EnsureEditable();
        SetVenue(venueName, address, latitude, longitude);
    }

    /// <summary>
    /// Bookings live outside this class, so the caller passes in how many are confirmed (rule 20).
    /// </summary>
    public void ChangeCapacity(int capacity, int confirmedBookings)
    {
        EnsureEditable();
        SetCapacity(capacity, confirmedBookings);
    }

    /// <summary>Adds an image at the end of the display order (rules 9 and 10).</summary>
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

        // A published event must keep at least one image (rule 8).
        if (Status == EventStatus.Published && _images.Count == 1)
            throw new BusinessRuleException("A published event must have at least one image.");

        _images.Remove(image);
    }

    /// <summary>Makes the event visible to other users (rules 2, 4, 8 and 12).</summary>
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

    /// <summary>Only published events can be cancelled; drafts are deleted instead (rule 12).</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
            throw new BusinessRuleException("Only a published event can be cancelled.");

        Status = EventStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>Only drafts can be deleted; nobody can have booked them (rules 12 and 14).</summary>
    public void Delete()
    {
        if (Status != EventStatus.Draft)
            throw new BusinessRuleException("Only a draft event can be deleted. Cancel it instead.");

        Status = EventStatus.Deleted;
    }

    // Cancelled and Deleted are final (rule 13).
    private void EnsureEditable()
    {
        if (Status is EventStatus.Cancelled or EventStatus.Deleted)
            throw new BusinessRuleException("A cancelled or deleted event can't be changed.");
    }

    private void SetDetails(string name, string description)
    {
        Name = RequireText(name, nameof(Name), NameMaxLength);
        Description = RequireText(description, nameof(Description), DescriptionMaxLength);
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
        // Written this way (rather than "< -90 || > 90") so NaN is rejected too:
        // every comparison with NaN is false.
        if (latitude is not (>= -90 and <= 90))
            throw new BusinessRuleException("Latitude must be between -90 and 90.");
        if (longitude is not (>= -180 and <= 180))
            throw new BusinessRuleException("Longitude must be between -180 and 180.");

        VenueName = RequireText(venueName, nameof(VenueName), VenueNameMaxLength);
        Address = RequireText(address, nameof(Address), AddressMaxLength);
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

    private static string RequireText(string value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BusinessRuleException($"{fieldName} is required.");

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new BusinessRuleException($"{fieldName} can be at most {maxLength} characters.");

        return trimmed;
    }

    /// <summary>
    /// .NET also accepts Windows time zone names like "GMT Standard Time". HasIanaId is only
    /// true for IANA names like "Europe/Lisbon", which is what the design requires (rule 7).
    /// </summary>
    private static bool IsIanaTimeZone(string timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
        && zone.HasIanaId;
}
