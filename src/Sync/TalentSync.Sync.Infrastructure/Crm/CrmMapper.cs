using TalentSync.Recruitment.Domain;

namespace TalentSync.Sync.Infrastructure.Crm;

/// <summary>Translates a candidate into the CRM's contact. The CV never goes to the CRM.</summary>
internal static class CrmMapper
{
    public static ContactRequestDto ToContactRequest(Candidate candidate) =>
        new(candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone);
}
