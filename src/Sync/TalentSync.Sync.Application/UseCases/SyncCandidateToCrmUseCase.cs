using TalentSync.Recruitment.Domain;
using TalentSync.Sync.Application.Ports;

namespace TalentSync.Sync.Application.UseCases;

/// <summary>
/// Makes the CRM contact match the candidate's current state in the recruitment source (D1).
/// </summary>
/// <remarks>
/// Takes only the candidate id, not the event type: the event is a signal and the fetched state decides
/// (notify-then-fetch, ADR 003). A <c>candidate.create</c> that arrives after the candidate was deleted therefore
/// marks the contact as deleted instead of recreating it, and a duplicate or reordered event repeats an idempotent
/// effect (invariant 2).
/// </remarks>
public sealed class SyncCandidateToCrmUseCase(IRecruitmentSource recruitmentSource, ICrm crm)
{
    public async Task<SyncCandidateToCrmResult> ExecuteAsync(string candidateId, CancellationToken cancellationToken)
    {
        var fetched = await recruitmentSource.GetCandidateAsync(candidateId, cancellationToken);

        var crmResult = fetched switch
        {
            FetchResult<Candidate>.Found found => await crm.UpsertContactAsync(found.Value, cancellationToken),
            FetchResult<Candidate>.Deleted => await crm.DeleteContactAsync(candidateId, cancellationToken),
            FetchResult<Candidate>.Rejected rejected => CrmResult.Rejected(rejected.Reason),
            _ => throw new InvalidOperationException("Unknown fetch result."),
        };

        return crmResult switch
        {
            CrmResult.DoneResult => SyncCandidateToCrmResult.Done,
            CrmResult.RejectedResult rejected => SyncCandidateToCrmResult.Rejected(rejected.Reason),
            _ => throw new InvalidOperationException("Unknown CRM result."),
        };
    }
}
