using System.Globalization;

namespace EventBooking.Core;

/// <summary>
/// A user's request to become an organiser. Kept after a decision, as a history.
/// Setters are private, so every change goes through a method that checks the rules.
/// </summary>
public class OrganiserApplication
{
    public const int OrganisationNameMaxLength = 200;
    public const int ContactPhoneMaxLength = 30;
    public const int WebsiteMaxLength = 200;
    public const int DescriptionMaxLength = 500;
    public const int RejectionReasonMaxLength = 500;

    // For EF Core when loading from the database.
    private OrganiserApplication() { }

    public int Id { get; private set; }
    public string UserId { get; private set; } = "";
    public string OrganisationName { get; private set; } = "";
    public string ContactPhone { get; private set; } = "";
    public string? Website { get; private set; }
    public string? Description { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public string? ReviewedById { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    /// <summary>Concurrency token, so a second admin's review can't overwrite the first.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>One month after a rejection; null unless Rejected.</summary>
    public DateTimeOffset? CanApplyAgainFrom =>
        Status == ApplicationStatus.Rejected ? ReviewedAt?.AddMonths(1) : null;

    /// <summary>Why the user can't apply right now, or null if they can.</summary>
    public static string? ReasonCannotApply(
        IEnumerable<string> userRoles,
        IEnumerable<OrganiserApplication> previousApplications,
        DateTimeOffset now)
    {
        if (userRoles.Contains(Roles.Organiser))
            return "You are already an organiser.";
        if (userRoles.Contains(Roles.Admin))
            return "Admins can't apply to become organisers.";

        var previous = previousApplications.ToList();
        if (previous.Any(a => a.Status == ApplicationStatus.Pending))
            return "You already have an application waiting for review.";

        var canApplyAgainFrom = previous.Max(a => a.CanApplyAgainFrom);
        if (canApplyAgainFrom is { } from && now < from)
            // InvariantCulture: English month names whatever the server's language.
            return $"You can apply again from {from.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}.";

        return null;
    }

    /// <summary>Creates a Pending application, if <see cref="ReasonCannotApply"/> allows it.</summary>
    public static OrganiserApplication Submit(
        string userId,
        string organisationName,
        string contactPhone,
        string? website,
        string? description,
        IEnumerable<string> userRoles,
        IEnumerable<OrganiserApplication> previousApplications,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new BusinessRuleException("An application must have an applicant.");

        var reason = ReasonCannotApply(userRoles, previousApplications, now);
        if (reason is not null)
            throw new BusinessRuleException(reason);

        return new OrganiserApplication
        {
            UserId = userId,
            OrganisationName = TextRules.Required(organisationName, "Organisation name", OrganisationNameMaxLength),
            ContactPhone = TextRules.Required(contactPhone, "Contact phone", ContactPhoneMaxLength),
            Website = TextRules.Optional(website, nameof(Website), WebsiteMaxLength),
            Description = TextRules.Optional(description, nameof(Description), DescriptionMaxLength),
            Status = ApplicationStatus.Pending,
            SubmittedAt = now,
        };
    }

    /// <summary>Marks the application approved. The caller gives the user the Organiser role.</summary>
    public void Approve(string adminId, DateTimeOffset now)
    {
        RecordReview(adminId, now);
        Status = ApplicationStatus.Approved;
    }

    /// <summary>Marks the application rejected, with an optional reason.</summary>
    public void Reject(string adminId, string? reason, DateTimeOffset now)
    {
        var trimmedReason = TextRules.Optional(reason, "Rejection reason", RejectionReasonMaxLength);
        RecordReview(adminId, now);
        Status = ApplicationStatus.Rejected;
        RejectionReason = trimmedReason;
    }

    private void RecordReview(string adminId, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Pending)
            throw new BusinessRuleException("This application has already been reviewed.");
        if (string.IsNullOrWhiteSpace(adminId))
            throw new BusinessRuleException("A review must record which admin made it.");

        ReviewedById = adminId;
        ReviewedAt = now;
    }
}
