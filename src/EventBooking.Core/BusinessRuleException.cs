namespace EventBooking.Core;

/// <summary>
/// Thrown when an action would break a business rule, e.g. publishing an event with no images.
/// The web layer catches it and shows the message to the user.
/// </summary>
public class BusinessRuleException(string message) : Exception(message);
