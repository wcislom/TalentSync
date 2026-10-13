namespace TalentSync.Sync.Infrastructure.Crm;

// The body of PUT /contacts/{candidateId}. The CRM rejects a contact without first name, last name or email (400);
// that check stays in the CRM, which owns the rule.
internal sealed record ContactRequestDto(string? FirstName, string? LastName, string? Email, string? Phone);
