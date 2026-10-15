using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Evaluation.Cases;

/// <summary>One golden-set case: a fictional CV and what a good answer contains (ADR 009).</summary>
public sealed record EvaluationCase(string Id, string Purpose, string Cv, ExpectedEnrichment Expected);

/// <param name="Required">Each entry is one skill, given as its accepted spellings.</param>
/// <param name="Acceptable">Correct if returned, not required.</param>
/// <param name="Forbidden">Plausible for the CV but not in it: returning one is a hallucination.</param>
/// <param name="SeniorityTolerance">0 = only the exact level counts; 1 = a neighbouring level counts too.</param>
public sealed record ExpectedEnrichment(
    IReadOnlyList<IReadOnlyList<string>> Required,
    IReadOnlyList<string> Acceptable,
    IReadOnlyList<string> Forbidden,
    Seniority Seniority,
    int SeniorityTolerance);
