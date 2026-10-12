using System.Net;
using Azure;
using Azure.Messaging.EventHubs;
using Microsoft.Data.SqlClient;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace TalentSync.Infrastructure.Messaging.Handling;

/// <summary>
/// Decides whether a failure is about the environment (wait and retry, never park) or unclassified
/// (a few attempts, then park). Permanent failures never arrive here: they are <see cref="HandlerResult"/> values.
/// </summary>
internal static class FailureClassifier
{
    // The common ones only. A missing number means the failure is parked after the unclassified retry limit
    // instead of being waited out.
    private static readonly HashSet<int> TransientSqlErrors =
    [
        -2,    // timeout
        53,    // server unreachable
        1205,  // deadlock victim
        10054, // connection reset
        10061, // connection refused
    ];

    public static bool IsTransient(Exception exception)
    {
        // EF and HttpClient wrap the cause (DbUpdateException → SqlException, TaskCanceledException → TimeoutException).
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsTransientItself(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientItself(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => IsTransientStatus(status),
        TimeoutException => true,
        TimeoutRejectedException => true,
        BrokenCircuitException => true,
        SqlException sql => TransientSqlErrors.Contains(sql.Number),

        // A failed checkpoint (Blob storage) or a lost connection to the hub.
        RequestFailedException { Status: >= 500 or 408 or 429 } => true,
        EventHubsException { IsTransient: true } => true,
        _ => false,
    };

    // 401 and 403 mean a wrong key or a missing header: every event would fail the same way, so parking would only
    // fill the parking lot. The partition waits until the configuration is fixed.
    private static bool IsTransientStatus(HttpStatusCode status) =>
        (int)status >= 500
        || status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden;
}
