namespace EventBooking.Core;

/// <summary>Thrown when an action breaks a business rule. The message is shown to the user.</summary>
public class BusinessRuleException(string message) : Exception(message);
