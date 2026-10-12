using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace FakeTeamtailor.State;

// The dictionary key is the resource id; records do not repeat it.
public sealed record Stage(string Name);

public sealed record Job(string Title);

public sealed record Candidate(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string ResumeText,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobApplication(
    string CandidateId,
    string JobId,
    string StageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RejectedAt);

/// <summary>
/// The whole state of the fake ATS, seeded with invented people (example.com addresses, fictional phone numbers).
/// Lost on restart. Real Teamtailor stages belong to a job; here every job shares the same five stages.
/// </summary>
public sealed class Store
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public ConcurrentDictionary<string, Stage> Stages { get; } = new()
    {
        ["stage-1"] = new("New"),
        ["stage-2"] = new("Screening"),
        ["stage-3"] = new("Interview"),
        ["stage-4"] = new("Qualified"),
        ["stage-5"] = new("Rejected"),
    };

    public ConcurrentDictionary<string, Job> Jobs { get; } = new()
    {
        ["job-1"] = new("Senior .NET Backend Developer"),
        ["job-2"] = new("Frontend Developer (React)"),
        ["job-3"] = new("Data Engineer"),
        ["job-4"] = new("DevOps Engineer"),
    };

    // Some CVs repeat the email or phone in the text on purpose: TalentSync must mask them before the LLM call.
    public ConcurrentDictionary<string, Candidate> Candidates { get; } = new()
    {
        ["candidate-1"] = Person("Alice", "Example", "alice.example@example.com", "+48 500 100 101", """
            Backend developer, 9 years of experience.
            Skills: C#, .NET 8, ASP.NET Core, EF Core, SQL Server, Azure Service Bus, Docker.
            2019-2026: tech lead in a team of 6, migrated a monolith to event-driven services on Azure.
            2016-2019: .NET developer, payment integrations, REST APIs.
            Contact: alice.example@example.com, +48 500 100 101.
            """),
        ["candidate-2"] = Person("Bob", "Sample", "bob.sample@example.com", "+48 500 100 102", """
            Frontend developer, 3 years of experience.
            Skills: TypeScript, React, Next.js, CSS, Jest, Playwright.
            2023-2026: built a design system and a customer portal for an insurance client.
            """),
        ["candidate-3"] = Person("Carol", "Placeholder", "carol.placeholder@example.com", null, """
            Data engineer, 6 years of experience.
            Skills: Python, SQL, Apache Spark, Databricks, Airflow, Azure Data Factory, dbt.
            Designed batch and streaming pipelines for a retail analytics platform.
            Reach me at carol.placeholder@example.com.
            """),
        ["candidate-4"] = Person("Dave", "Testperson", "dave.testperson@example.com", "+48 500 100 104", """
            DevOps engineer, 12 years of experience, previously Linux administrator.
            Skills: Kubernetes, Terraform, Azure, AWS, GitHub Actions, Prometheus, Grafana, Bash.
            Led platform teams, introduced GitOps and on-call practices.
            Phone: +48 500 100 104.
            """),
        ["candidate-5"] = Person("Eve", "Mockowska", "eve.mockowska@example.com", "+48 500 100 105", """
            Junior developer, 1 year of commercial experience.
            Skills: C#, ASP.NET Core basics, SQL, Git, some React.
            Bootcamp graduate, internship in a small software house.
            """),
        ["candidate-6"] = Person("Frank", "Dummy", "frank.dummy@example.com", "+48 500 100 106", """
            Full-stack developer, 5 years of experience.
            Skills: C#, .NET, React, TypeScript, PostgreSQL, Docker, Azure Functions.
            Worked on B2B SaaS products, from database design to UI.
            """),
    };

    // candidate-6 has two applications, so qualifying both shows one profile per candidate (D3).
    public ConcurrentDictionary<string, JobApplication> JobApplications { get; } = new()
    {
        ["job-application-1"] = Application("candidate-1", "job-1", "stage-3"),
        ["job-application-2"] = Application("candidate-2", "job-2", "stage-2"),
        ["job-application-3"] = Application("candidate-3", "job-3", "stage-3"),
        ["job-application-4"] = Application("candidate-4", "job-4", "stage-1"),
        ["job-application-5"] = Application("candidate-5", "job-1", "stage-2"),
        ["job-application-6"] = Application("candidate-6", "job-1", "stage-3"),
        ["job-application-7"] = Application("candidate-6", "job-2", "stage-3"),
    };

    // Owners of removed applications: a worker still fetches a deleted application (and gets 404), and /calls
    // and anomalies must count that call under its candidate, not under none.
    private readonly ConcurrentDictionary<string, string> removedJobApplicationOwners = new();

    private int lastCandidateNumber = 6;
    private int lastJobApplicationNumber = 7;

    public string NewCandidateId() => NewId("candidate", ref lastCandidateNumber, Candidates);

    public string NewJobApplicationId() => NewId("job-application", ref lastJobApplicationNumber, JobApplications);

    public string? FindStageId(string name) =>
        Stages.FirstOrDefault(stage => string.Equals(stage.Value.Name, name, StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>The candidate a <c>/v1</c> request is about; a job application path holds the application id.</summary>
    public string? CandidateIdOf(HttpRequest request)
    {
        var id = request.RouteValues.TryGetValue("id", out var value) ? value as string : null;
        if (!request.Path.StartsWithSegments("/v1/job-applications"))
        {
            return id;
        }

        if (id is null)
        {
            return null;
        }

        return JobApplications.TryGetValue(id, out var application)
            ? application.CandidateId
            : removedJobApplicationOwners.GetValueOrDefault(id);
    }

    public bool TryRemoveJobApplication(string id, [NotNullWhen(true)] out JobApplication? application)
    {
        if (!JobApplications.TryRemove(id, out application))
        {
            return false;
        }

        removedJobApplicationOwners[id] = application.CandidateId;
        return true;
    }

    // Skips ids a test already took by passing its own candidateId.
    private static string NewId<T>(string prefix, ref int lastNumber, ConcurrentDictionary<string, T> existing)
    {
        string id;
        do
        {
            id = $"{prefix}-{Interlocked.Increment(ref lastNumber)}";
        }
        while (existing.ContainsKey(id));

        return id;
    }

    private static Candidate Person(string firstName, string lastName, string email, string? phone, string cv) =>
        new(firstName, lastName, email, phone, cv, SeededAt, SeededAt);

    private static JobApplication Application(string candidateId, string jobId, string stageId) =>
        new(candidateId, jobId, stageId, SeededAt, SeededAt, RejectedAt: null);
}
