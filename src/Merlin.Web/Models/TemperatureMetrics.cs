namespace Merlin.Web.Models;

public sealed record TemperatureMetrics(IReadOnlyList<TemperatureSensor> Sensors);

public sealed record TemperatureSensor(
    string Label,
    double CelsiusCurrent,
    double? CelsiusCritical);
