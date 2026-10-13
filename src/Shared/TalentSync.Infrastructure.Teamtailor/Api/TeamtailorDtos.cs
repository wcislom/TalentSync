namespace TalentSync.Infrastructure.Teamtailor.Api;

// The JSON:API shapes of the two endpoints the ACL reads, only the fields it maps. Unknown fields are ignored
// (tolerant reader) and nothing here leaves the adapter.

internal sealed record CandidateResponseDto(CandidateDto? Data);

internal sealed record CandidateDto(string? Id, CandidateAttributesDto? Attributes);

// ASSUMPTION (teamtailor rule): resume-text is our simulator's plain-text CV; the real API exposes a file.
internal sealed record CandidateAttributesDto(string? FirstName, string? LastName, string? Email, string? Phone, string? ResumeText);

internal sealed record JobApplicationResponseDto(JobApplicationDto? Data, IReadOnlyList<IncludedResourceDto>? Included);

internal sealed record JobApplicationDto(string? Id, JobApplicationRelationshipsDto? Relationships);

internal sealed record JobApplicationRelationshipsDto(RelationshipDto? Candidate, RelationshipDto? Stage);

internal sealed record RelationshipDto(ResourceIdentifierDto? Data);

internal sealed record ResourceIdentifierDto(string? Id, string? Type);

internal sealed record IncludedResourceDto(string? Id, string? Type, IncludedAttributesDto? Attributes);

internal sealed record IncludedAttributesDto(string? Name);
