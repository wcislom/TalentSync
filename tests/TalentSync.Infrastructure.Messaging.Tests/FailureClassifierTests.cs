using System.Net;
using Azure;
using Azure.Messaging.EventHubs;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TalentSync.Infrastructure.Messaging.Handling;

namespace TalentSync.Infrastructure.Messaging.Tests;

// SqlException has no public constructor, so the SQL error numbers are covered only by the acceptance tests.
public class FailureClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void TransientHttpStatus_IsTransient(HttpStatusCode status)
    {
        Assert.True(FailureClassifier.IsTransient(new HttpRequestException(null, null, status)));
    }

    // Adapters return Rejected for these; one reaching the skeleton as an exception is a bug, not an outage.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public void OtherHttpStatus_IsNotTransient(HttpStatusCode status)
    {
        Assert.False(FailureClassifier.IsTransient(new HttpRequestException(null, null, status)));
    }

    [Fact]
    public void ConnectionFailure_IsTransient()
    {
        Assert.True(FailureClassifier.IsTransient(new HttpRequestException("connection refused")));
    }

    [Fact]
    public void HttpClientTimeout_IsTransient()
    {
        Assert.True(FailureClassifier.IsTransient(new TaskCanceledException("timeout", new TimeoutException())));
    }

    [Fact]
    public void PollyAttemptTimeout_IsTransient()
    {
        Assert.True(FailureClassifier.IsTransient(new TimeoutRejectedException()));
    }

    [Fact]
    public void OpenCircuit_IsTransient()
    {
        Assert.True(FailureClassifier.IsTransient(new BrokenCircuitException()));
    }

    [Theory]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(404, false)]
    public void BlobStorageFailure_IsTransientOnlyForServerSideStatus(int status, bool transient)
    {
        Assert.Equal(transient, FailureClassifier.IsTransient(new RequestFailedException(status, "checkpoint failed")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EventHubsFailure_FollowsTheSdkClassification(bool isTransient)
    {
        Assert.Equal(isTransient, FailureClassifier.IsTransient(new EventHubsException(isTransient, "ats-events")));
    }

    [Fact]
    public void Bug_IsNotTransient()
    {
        Assert.False(FailureClassifier.IsTransient(new InvalidOperationException()));
    }

    [Fact]
    public void CancellationWithoutTimeout_IsNotTransient()
    {
        Assert.False(FailureClassifier.IsTransient(new TaskCanceledException()));
    }
}
