namespace TalentSync.Matching.Domain;

public enum EnrichmentRejection
{
    BlankSkill,
    SkillTooLong,
    NoSkills,
    UnknownSeniority,
    ConfidenceOutOfRange,
}
