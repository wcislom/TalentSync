using System.Diagnostics.Metrics;

namespace TalentSync.Infrastructure.Messaging.Tests;

/// <summary>Creates real meters, so the telemetry code runs; nothing listens to them.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() => _meters.ForEach(meter => meter.Dispose());
}
