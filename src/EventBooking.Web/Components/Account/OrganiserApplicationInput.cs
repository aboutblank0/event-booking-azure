using System.ComponentModel.DataAnnotations;
using EventBooking.Core;

namespace EventBooking.Web.Components.Account;

/// <summary>
/// The organiser application form fields. Core checks the same limits again on submit.
/// </summary>
public class OrganiserApplicationInput
{
    [Required]
    [StringLength(OrganiserApplication.OrganisationNameMaxLength)]
    [Display(Name = "Organisation name")]
    public string OrganisationName { get; set; } = "";

    [Required]
    [StringLength(OrganiserApplication.ContactPhoneMaxLength)]
    [Display(Name = "Contact phone")]
    public string ContactPhone { get; set; } = "";

    [StringLength(OrganiserApplication.WebsiteMaxLength)]
    [Display(Name = "Website (optional)")]
    public string? Website { get; set; }

    [StringLength(OrganiserApplication.DescriptionMaxLength)]
    [Display(Name = "Why you're applying (optional)")]
    public string? Description { get; set; }
}
