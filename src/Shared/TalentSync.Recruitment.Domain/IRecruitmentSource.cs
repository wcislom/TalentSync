namespace TalentSync.Recruitment.Domain;

/// <summary>
/// Port to the system that owns recruitment data. Returns the current state, which is the truth;
/// the event that triggered the read is only a signal.
/// </summary>
public interface IRecruitmentSource
{
    /// <summary>The candidate including the CV text.</summary>
    Task<FetchResult<Candidate>> GetCandidateAsync(string candidateId, CancellationToken cancellationToken);

    /// <summary>The application with its stage already mapped to <see cref="ApplicationStatus"/>.</summary>
    Task<FetchResult<JobApplication>> GetJobApplicationAsync(string jobApplicationId, CancellationToken cancellationToken);
}
