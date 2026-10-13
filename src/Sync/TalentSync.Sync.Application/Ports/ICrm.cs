using TalentSync.Recruitment.Domain;

namespace TalentSync.Sync.Application.Ports;

/// <summary>
/// Port to the CRM. Both effects are keyed by the candidate id, so repeating one creates no second contact
/// (invariant 2).
/// </summary>
public interface ICrm
{
    /// <summary>Creates or updates the candidate's contact.</summary>
    Task<CrmResult> UpsertContactAsync(Candidate candidate, CancellationToken cancellationToken);

    /// <summary>Marks the contact as deleted; the CRM keeps the record (D1). A missing contact counts as deleted.</summary>
    Task<CrmResult> DeleteContactAsync(string candidateId, CancellationToken cancellationToken);
}
