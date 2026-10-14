using EventBooking.Core;

namespace EventBooking.Tests;

/// <summary>
/// Tests for the rules in docs/roles-design.md ("Organiser applications").
/// Rule numbers are in the test names' comments.
/// </summary>
public class OrganiserApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A valid application from a normal user. Tests override only what they care about.</summary>
    private static OrganiserApplication Submit(
        string organisationName = "Lisbon Tech Club",
        string contactPhone = "+351 912 345 678",
        string? website = "https://example.org",
        string? description = "We run monthly meetups.",
        string[]? userRoles = null,
        OrganiserApplication[]? previous = null,
        DateTimeOffset? now = null) =>
        OrganiserApplication.Submit("user-1", organisationName, contactPhone, website, description,
            userRoles ?? [], previous ?? [], now ?? Now);

    private static OrganiserApplication RejectedOn(DateTimeOffset reviewedAt)
    {
        var application = Submit(now: reviewedAt.AddDays(-1));
        application.Reject("admin-1", null, reviewedAt);
        return application;
    }

    // ---------- Submitting ----------

    [Fact]
    public void Submit_ValidApplication_IsPendingWithAllValuesSet()
    {
        var application = Submit();

        Assert.Equal(ApplicationStatus.Pending, application.Status);
        Assert.Equal("user-1", application.UserId);
        Assert.Equal("Lisbon Tech Club", application.OrganisationName);
        Assert.Equal("+351 912 345 678", application.ContactPhone);
        Assert.Equal("https://example.org", application.Website);
        Assert.Equal("We run monthly meetups.", application.Description);
        Assert.Equal(Now, application.SubmittedAt);
        Assert.Null(application.ReviewedById);
        Assert.Null(application.ReviewedAt);
        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void Submit_TrimsText_AndStoresBlankOptionalTextAsNull()
    {
        var application = Submit(organisationName: "  Lisbon Tech Club  ", website: "   ", description: "");

        Assert.Equal("Lisbon Tech Club", application.OrganisationName);
        Assert.Null(application.Website);
        Assert.Null(application.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_RejectsMissingRequiredText(string blank)
    {
        Assert.Throws<BusinessRuleException>(() => Submit(organisationName: blank));
        Assert.Throws<BusinessRuleException>(() => Submit(contactPhone: blank));
    }

    [Fact]
    public void Submit_RejectsMissingApplicant()
    {
        Assert.Throws<BusinessRuleException>(() =>
            OrganiserApplication.Submit("", "Club", "123", null, null, [], [], Now));
    }

    [Fact]
    public void Submit_AcceptsTextAtMaxLength_RejectsOneOver()
    {
        Submit(organisationName: new string('a', OrganiserApplication.OrganisationNameMaxLength),
            contactPhone: new string('1', OrganiserApplication.ContactPhoneMaxLength),
            website: new string('a', OrganiserApplication.WebsiteMaxLength),
            description: new string('a', OrganiserApplication.DescriptionMaxLength));

        Assert.Throws<BusinessRuleException>(() => Submit(organisationName: new string('a', OrganiserApplication.OrganisationNameMaxLength + 1)));
        Assert.Throws<BusinessRuleException>(() => Submit(contactPhone: new string('1', OrganiserApplication.ContactPhoneMaxLength + 1)));
        Assert.Throws<BusinessRuleException>(() => Submit(website: new string('a', OrganiserApplication.WebsiteMaxLength + 1)));
        Assert.Throws<BusinessRuleException>(() => Submit(description: new string('a', OrganiserApplication.DescriptionMaxLength + 1)));
    }

    [Fact]
    public void ReasonCannotApply_IsNullForANormalUser_AndExplainsOtherwise()
    {
        Assert.Null(OrganiserApplication.ReasonCannotApply([], [], Now));
        Assert.Equal("You are already an organiser.", OrganiserApplication.ReasonCannotApply([Roles.Organiser], [], Now));
        Assert.Equal("You can apply again from 14 November 2026.",
            OrganiserApplication.ReasonCannotApply([], [RejectedOn(Now)], Now.AddDays(1)));
    }

    [Fact]
    public void Submit_RejectsWhenAnotherApplicationIsPending() // rule 1
    {
        var pending = Submit(now: Now.AddDays(-3));

        Assert.Throws<BusinessRuleException>(() => Submit(previous: [pending]));
    }

    [Theory] // rule 2
    [InlineData(Roles.Organiser)]
    [InlineData(Roles.Admin)]
    public void Submit_RejectsOrganisersAndAdmins(string role)
    {
        Assert.Throws<BusinessRuleException>(() => Submit(userRoles: [role]));
    }

    [Fact]
    public void Submit_WithinOneMonthOfRejection_IsRejected() // rule 3
    {
        var rejected = RejectedOn(Now);

        Assert.Throws<BusinessRuleException>(() => Submit(previous: [rejected], now: Now.AddMonths(1).AddMinutes(-1)));
    }

    [Fact]
    public void Submit_OneMonthAfterRejection_IsAllowed() // rule 3
    {
        var rejected = RejectedOn(Now);

        var application = Submit(previous: [rejected], now: Now.AddMonths(1));

        Assert.Equal(ApplicationStatus.Pending, application.Status);
    }

    [Fact]
    public void CanApplyAgainFrom_IsOneMonthAfterRejection_AndOnlySetForRejections() // rule 3
    {
        var pending = Submit();
        var approved = Submit();
        approved.Approve("admin-1", Now);

        Assert.Equal(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), RejectedOn(Now).CanApplyAgainFrom);
        Assert.Null(pending.CanApplyAgainFrom);
        Assert.Null(approved.CanApplyAgainFrom);
    }

    [Fact]
    public void Submit_UsesTheLatestRejection() // rule 3
    {
        var older = RejectedOn(Now.AddMonths(-3));
        var latest = RejectedOn(Now.AddDays(-10));

        Assert.Throws<BusinessRuleException>(() => Submit(previous: [latest, older]));
    }

    [Fact]
    public void Submit_AfterApprovalAndRoleRemoved_IsAllowedStraightAway()
    {
        // From "Removing organisers": a user who lost the role can apply again at once.
        var approved = Submit(now: Now.AddDays(-2));
        approved.Approve("admin-1", Now.AddDays(-1));

        var application = Submit(previous: [approved]);

        Assert.Equal(ApplicationStatus.Pending, application.Status);
    }

    // ---------- Reviewing (rules 4 and 6) ----------

    [Fact]
    public void Approve_Pending_RecordsTheReview()
    {
        var application = Submit();

        application.Approve("admin-1", Now.AddHours(1));

        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal("admin-1", application.ReviewedById);
        Assert.Equal(Now.AddHours(1), application.ReviewedAt);
        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void Reject_WithReason_StoresTrimmedReason()
    {
        var application = Submit();

        application.Reject("admin-1", "  Please add a website.  ", Now.AddHours(1));

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
        Assert.Equal("admin-1", application.ReviewedById);
        Assert.Equal(Now.AddHours(1), application.ReviewedAt);
        Assert.Equal("Please add a website.", application.RejectionReason);
    }

    [Theory] // rule 6
    [InlineData(null)]
    [InlineData("   ")]
    public void Reject_WithoutReason_IsAllowed(string? reason)
    {
        var application = Submit();

        application.Reject("admin-1", reason, Now);

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void Reject_RejectsReasonOverMaxLength()
    {
        var application = Submit();

        Assert.Throws<BusinessRuleException>(() =>
            application.Reject("admin-1", new string('a', OrganiserApplication.RejectionReasonMaxLength + 1), Now));
        Assert.Equal(ApplicationStatus.Pending, application.Status);
    }

    [Fact]
    public void Review_RejectsMissingAdmin()
    {
        var application = Submit();

        Assert.Throws<BusinessRuleException>(() => application.Approve("", Now));
        Assert.Throws<BusinessRuleException>(() => application.Reject(" ", null, Now));
    }

    [Fact]
    public void Review_OnlyPendingApplicationsCanBeReviewed() // rule 4
    {
        var approved = Submit();
        approved.Approve("admin-1", Now);
        var rejected = Submit();
        rejected.Reject("admin-1", null, Now);

        Assert.Throws<BusinessRuleException>(() => approved.Approve("admin-2", Now));
        Assert.Throws<BusinessRuleException>(() => approved.Reject("admin-2", null, Now));
        Assert.Throws<BusinessRuleException>(() => rejected.Approve("admin-2", Now));
        Assert.Throws<BusinessRuleException>(() => rejected.Reject("admin-2", null, Now));
    }
}
