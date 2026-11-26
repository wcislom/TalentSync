using System.Text.RegularExpressions;

namespace TalentSync.Matching.Infrastructure.Llm;

/// <summary>Replaces email addresses and phone numbers before the CV leaves for the model (invariant 15).</summary>
/// <remarks>
/// Errs towards masking too much: a long run of digits that is not a phone number is masked too, which costs the model
/// a little context but never leaks a number (an IP address such as <c>192.168.100.200</c> is masked too).
/// Dots are separators only between groups of 2–3 digits (<c>600.123.456</c>), so dates with a 4-digit year such as
/// <c>01.2015-12.2020</c> or <c>15.03.1990</c> stay.
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
        var withoutDottedPhones = DottedPhoneCandidate().Replace(withoutEmails, MaskIfLongEnough);
        return PhoneCandidate().Replace(withoutDottedPhones, MaskIfLongEnough);
    }

    private static string MaskIfLongEnough(Match match) =>
        match.Value.Count(char.IsAsciiDigit) >= MinPhoneDigits ? PhoneMask : match.Value;

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // The lookarounds keep a dotted run whole: a run with a 4-digit group (a year) never matches in part.
    [GeneratedRegex(@"(?<![\d.])(?:\+\d{1,3} ?|\(\d{2,3}\) ?)?\d{2,3}(?:\.\d{2,3})+(?!\.?\d)")]
    private static partial Regex DottedPhoneCandidate();

    [GeneratedRegex(@"\+?\(?\d[\d ()-]*\d")]
    private static partial Regex PhoneCandidate();
}
