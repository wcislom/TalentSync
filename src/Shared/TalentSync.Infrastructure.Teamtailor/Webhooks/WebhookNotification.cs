namespace TalentSync.Infrastructure.Teamtailor.Webhooks;

/// <summary>
/// What a webhook says happened, ids only. Attributes are never carried over: they may hold PII
/// and consumers fetch the current state from the API anyway (notify-then-fetch).
/// </summary>
/// <param name="EventName">Source event name, e.g. <c>job_application.update</c>.</param>
/// <param name="ResourceType">JSON:API type from <c>payload.data.type</c>, e.g. <c>job-applications</c>.</param>
/// <param name="ResourceId">JSON:API id from <c>payload.data.id</c>.</param>
/// <param name="CandidateId">The candidate the event concerns; the partition key.</param>
public sealed record WebhookNotification(string EventName, string ResourceType, string ResourceId, string CandidateId);
