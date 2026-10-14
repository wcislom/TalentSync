namespace TalentSync.Matching.Domain;

public enum EnrichmentRejection
{
    BlankSkill,
    SkillTooLong,
    NoSkills,
    TooManySkills,
    UnknownSeniority,
    ConfidenceOutOfRange,
}
