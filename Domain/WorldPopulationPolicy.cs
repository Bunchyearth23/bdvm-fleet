using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum WorldPopulationSource
{
    PurchasedDelivery,
    LeasedDelivery,
    StarterDelivery,
    RecoveryRequired,
    ExternalTraffic,
    NaturalLocomotive,
    ContractProvidedVehicle,
    UnsupportedTutorial,
    Unknown
}

public enum WorldPopulationDecisionKind { Allow, Deny }

[DataContract]
public sealed class WorldPopulationRule
{
    [DataMember(Name = "source", Order = 1)] public WorldPopulationSource Source { get; set; }
    [DataMember(Name = "allow", Order = 2)] public bool Allow { get; set; }
    [DataMember(Name = "maximumPhysicalCount", Order = 3)] public int MaximumPhysicalCount { get; set; } = -1;
    [DataMember(Name = "detail", Order = 4)] public string Detail { get; set; } = "";
}

[DataContract]
public sealed class WorldPopulationPolicy
{
    public const int CurrentVersion = 1;
    [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; } = CurrentVersion;
    [DataMember(Name = "strict", Order = 2)] public bool Strict { get; set; } = true;
    [DataMember(Name = "rules", Order = 3)] public List<WorldPopulationRule> Rules { get; set; } = new List<WorldPopulationRule>();

    public static WorldPopulationPolicy StrictDefaults()
    {
        return new WorldPopulationPolicy
        {
            Rules = new List<WorldPopulationRule>
            {
                Rule(WorldPopulationSource.PurchasedDelivery, true, -1, "host-authoritative purchased delivery"),
                Rule(WorldPopulationSource.LeasedDelivery, true, -1, "host-authoritative leased delivery"),
                Rule(WorldPopulationSource.StarterDelivery, true, -1, "one-shot persistent starter entitlement"),
                Rule(WorldPopulationSource.RecoveryRequired, true, 1, "non-economic recovery reserve only"),
                Rule(WorldPopulationSource.ExternalTraffic, true, -1, "external traffic remains outside BDVM ownership"),
                Rule(WorldPopulationSource.NaturalLocomotive, false, 0, "natural economic locomotives are disabled"),
                Rule(WorldPopulationSource.ContractProvidedVehicle, false, 0, "contracts never provide rolling stock"),
                Rule(WorldPopulationSource.UnsupportedTutorial, false, 0, "strict BDVM requires a non-tutorial career"),
                Rule(WorldPopulationSource.Unknown, false, 0, "unknown spawn sources fail closed")
            }
        };
    }

    private static WorldPopulationRule Rule(WorldPopulationSource source, bool allow, int maximum, string detail) =>
        new WorldPopulationRule { Source = source, Allow = allow, MaximumPhysicalCount = maximum, Detail = detail };
}

public sealed class WorldPopulationRequest
{
    public string CorrelationId { get; set; } = "";
    public WorldPopulationSource Source { get; set; }
    public string Origin { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string LocationId { get; set; } = "";
    public int ExistingPhysicalCount { get; set; }
}

public sealed class WorldPopulationDecision
{
    public WorldPopulationDecisionKind Decision { get; set; }
    public WorldPopulationSource Source { get; set; }
    public string ResultCode { get; set; } = "";
    public string Detail { get; set; } = "";
}

public static class WorldPopulationPolicyEngine
{
    public static WorldPopulationDecision Evaluate(WorldPopulationPolicy policy, WorldPopulationRequest request)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        if (request == null || string.IsNullOrWhiteSpace(request.CorrelationId) || string.IsNullOrWhiteSpace(request.Origin)) throw new ArgumentException("A bounded population request is required.", nameof(request));
        Validate(policy);
        if (!policy.Strict) return Decision(request.Source, true, "population-policy-observe-only", "Strict population control is disabled.");
        var rule = policy.Rules.Single(x => x.Source == request.Source);
        var belowMaximum = rule.MaximumPhysicalCount < 0 || request.ExistingPhysicalCount < rule.MaximumPhysicalCount;
        return Decision(request.Source, rule.Allow && belowMaximum,
            rule.Allow && belowMaximum ? "population-source-allowed" : rule.Allow ? "population-maximum-reached" : "population-source-denied", rule.Detail);
    }

    public static void Validate(WorldPopulationPolicy policy)
    {
        if (policy.SchemaVersion != WorldPopulationPolicy.CurrentVersion) throw new InvalidOperationException("Unsupported world population policy version.");
        if (policy.Rules == null || policy.Rules.GroupBy(x => x.Source).Any(x => x.Count() != 1) || Enum.GetValues(typeof(WorldPopulationSource)).Cast<WorldPopulationSource>().Any(source => policy.Rules.All(x => x.Source != source)))
            throw new InvalidOperationException("World population policy must define every source exactly once.");
        if (policy.Rules.Any(x => x.MaximumPhysicalCount < -1 || string.IsNullOrWhiteSpace(x.Detail))) throw new InvalidOperationException("World population policy contains an invalid bound or detail.");
    }

    private static WorldPopulationDecision Decision(WorldPopulationSource source, bool allow, string code, string detail) =>
        new WorldPopulationDecision { Source = source, Decision = allow ? WorldPopulationDecisionKind.Allow : WorldPopulationDecisionKind.Deny, ResultCode = code, Detail = detail };
}
