namespace TalentSync.Sync.Infrastructure.Crm;

/// <summary>What the CRM client reads from the <c>Crm</c> configuration section.</summary>
internal sealed class CrmOptions
{
    public const string SectionName = "Crm";

    public Uri? BaseUrl { get; set; }

    public bool IsValid() => BaseUrl is { IsAbsoluteUri: true };
}
