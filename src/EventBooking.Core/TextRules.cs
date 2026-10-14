namespace EventBooking.Core;

/// <summary>Checks for text fields, shared by the entities in Core.</summary>
internal static class TextRules
{
    /// <summary>Returns the trimmed text, or throws if it is blank or too long.</summary>
    public static string Required(string value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BusinessRuleException($"{fieldName} is required.");

        return CheckLength(value.Trim(), fieldName, maxLength);
    }

    /// <summary>Returns the trimmed text, or null if it is blank. Throws if it is too long.</summary>
    public static string? Optional(string? value, string fieldName, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : CheckLength(value.Trim(), fieldName, maxLength);

    private static string CheckLength(string trimmed, string fieldName, int maxLength) =>
        trimmed.Length <= maxLength
            ? trimmed
            : throw new BusinessRuleException($"{fieldName} can be at most {maxLength} characters.");
}
