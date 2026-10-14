using TalentSync.Matching.Infrastructure.Llm;

namespace TalentSync.Matching.Infrastructure.Tests;

public sealed class CvMaskerTests
{
    [Theory]
    [InlineData("Contact: grace.sampleton@example.com.", "Contact: [email].")]
    [InlineData("Mail a.b+cv@sub.example.co.uk today", "Mail [email] today")]
    public void Mask_ReplacesEmailAddresses(string cv, string expected) => Assert.Equal(expected, CvMasker.Mask(cv));

    [Theory]
    [InlineData("Phone +48 600 700 800, evenings", "Phone [phone], evenings")]
    [InlineData("Phone 600-700-800", "Phone [phone]")]
    [InlineData("Phone (22) 123 45 67", "Phone [phone]")]
    [InlineData("Phone 600700800", "Phone [phone]")]
    public void Mask_ReplacesPhoneNumbers(string cv, string expected) => Assert.Equal(expected, CvMasker.Mask(cv));

    [Theory]
    [InlineData("Backend developer 2015-2020, .NET 8, C# 12")]
    [InlineData("Lead from 01.2015-12.2020 at a bank")]
    [InlineData("4 years of experience, team of 12")]
    public void Mask_KeepsYearsVersionsAndSmallNumbers(string cv) => Assert.Equal(cv, CvMasker.Mask(cv));
}
