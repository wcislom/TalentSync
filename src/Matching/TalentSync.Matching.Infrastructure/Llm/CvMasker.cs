using System.Text.RegularExpressions;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>Replaces email addresses and phone numbers before the CV leaves for the model (invariant 15).</summary>
/// <remarks>
/// Errs towards masking too much: a long run of digits that is not a phone number is masked too, which costs the model
/// a little context but never leaks a number. Dots are not separators, so dates such as <c>01.2015-12.2020</c> stay.
/// </remarks>
internal static partial class CvMasker
{
    public const string EmailMask = "[email]";

    public const string PhoneMask = "[phone]";

    // Fewer digits than the shortest phone number in national format: years and year ranges ("2015-2020") stay.
    private const int MinPhoneDigits = 9;

    public static string Mask(string cvText)
    {
        var withoutEmails = Email().Replace(cvText, EmailMask);
        return PhoneCandidate().Replace(withoutEmails, match => match.Value.Count(char.IsAsciiDigit) >= MinPhoneDigits ? PhoneMask : match.Value);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\+?\(?\d[\d ()-]*\d")]
    private static partial Regex PhoneCandidate();
}
