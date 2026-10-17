using System.Text.Json.Serialization;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>Source-generated JSON for the ServiceHost files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(ServiceLock))]
[JsonSerializable(typeof(ServiceReadyReport))]
[JsonSerializable(typeof(ServiceSupervisorState))]
internal sealed partial class ServiceHostJsonContext : JsonSerializerContext;
