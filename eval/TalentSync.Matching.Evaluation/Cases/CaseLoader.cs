using System.Text.Json;
using TalentSync.Matching.Domain;

namespace TalentSync.Matching.Evaluation.Cases;

/// <summary>Reads <c>*.json</c> case files, one case per file, named after the case id.</summary>
public static class CaseLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<EvaluationCase> Load(string directory) =>
        GoldenSetHash.CaseFiles(directory).Select(LoadFile).ToList();

    private static EvaluationCase LoadFile(string path)
    {
        var fileName = Path.GetFileName(path);
        CaseFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CaseFileDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{fileName}: not valid JSON ({exception.Message}).", exception);
        }

        return Validate(dto, fileName);
    }

    private static EvaluationCase Validate(CaseFileDto? dto, string fileName)
    {
        InvalidDataException Invalid(string message) => new($"{fileName}: {message}");

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Purpose)
            || string.IsNullOrWhiteSpace(dto.Cv) || dto.Expected is null)
        {
            throw Invalid("id, purpose, cv and expected are required.");
        }

        if (fileName != dto.Id + ".json")
        {
            throw Invalid($"the file must be named after the case id ('{dto.Id}.json').");
        }

        var expected = dto.Expected;
        var required = expected.Required ?? [];
        if (required.Any(entry => entry is null || entry.Count == 0))
        {
            throw Invalid("every required entry needs at least one spelling.");
        }

        IEnumerable<string?> spellings = [.. required.SelectMany(entry => entry!), .. expected.Acceptable ?? [], .. expected.Forbidden ?? []];
        if (spellings.Any(s => Skill.IsBlank(s) || Skill.IsTooLong(s)))
        {
            throw Invalid($"skill names must be non-blank and at most {Skill.MaxLength} characters.");
        }

        // Exact names only, as the domain parses the model's answer.
        if (!Enum.TryParse<Seniority>(expected.Seniority, ignoreCase: false, out var seniority) || Enum.GetName(seniority) != expected.Seniority)
        {
            throw Invalid($"seniority must be one of {string.Join(", ", Enum.GetNames<Seniority>())}.");
        }

        if (expected.SeniorityTolerance < 0)
        {
            throw Invalid("seniorityTolerance must not be negative.");
        }

        return new EvaluationCase(
            dto.Id,
            dto.Purpose,
            dto.Cv,
            new ExpectedEnrichment(
                required.Select(entry => (IReadOnlyList<string>)entry!.Select(s => s!).ToList()).ToList(),
                (expected.Acceptable ?? []).Select(s => s!).ToList(),
                (expected.Forbidden ?? []).Select(s => s!).ToList(),
                seniority,
                expected.SeniorityTolerance));
    }

    private sealed record CaseFileDto(string? Id, string? Purpose, string? Cv, ExpectedDto? Expected);

    private sealed record ExpectedDto(
        List<List<string?>?>? Required, List<string?>? Acceptable, List<string?>? Forbidden, string? Seniority, int SeniorityTolerance);
}
