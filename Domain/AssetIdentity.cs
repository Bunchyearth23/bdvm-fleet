using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BDVM.Domain;

public static class AssetPersistenceSchema
{
    public const string Name = "bdvm.asset-registry";
    public const int Version = 1;
}

public enum PersistentLinkState
{
    CandidateUnverified,
    Resolved,
    TemporarilyAbsent,
    Missing,
    Ambiguous,
    Invalid
}

public sealed class AssetDefinition
{
    public string DefinitionId { get; set; } = "";
    public string Origin { get; set; } = "";
    public List<string> RequiredComponents { get; set; } = new List<string>();
}

public sealed class PersistentVehicleLink
{
    public const string CarGuidKind = "derail-valley:TrainCar.CarGUID";

    public string Kind { get; set; } = CarGuidKind;
    public string? Value { get; set; }
    public string? ExpectedDefinitionId { get; set; }
    public PersistentLinkState State { get; set; } = PersistentLinkState.CandidateUnverified;
    public string Detail { get; set; } = "CarGUID is the observed persistence candidate; runtime stability is not yet validated.";
}

public sealed class FleetAsset
{
    public string AssetId { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public PersistentVehicleLink GameLink { get; set; } = new PersistentVehicleLink();

    public static FleetAsset Create(string definitionId, string carGuid)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("A definition ID is required.", nameof(definitionId));
        if (!Guid.TryParse(carGuid, out var parsed) || parsed == Guid.Empty)
            throw new ArgumentException("CarGUID must be a non-empty GUID.", nameof(carGuid));
        return new FleetAsset
        {
            AssetId = Guid.NewGuid().ToString("N"),
            DefinitionId = definitionId,
            GameLink = new PersistentVehicleLink
            {
                Value = parsed.ToString("D"),
                ExpectedDefinitionId = definitionId
            }
        };
    }
}

public sealed class AssetBundle
{
    public string BundleId { get; set; } = "";
    public List<string> ComponentAssetIds { get; set; } = new List<string>();
}

public sealed class AssetRegistrySnapshot
{
    public string Schema { get; set; } = AssetPersistenceSchema.Name;
    public int SchemaVersion { get; set; } = AssetPersistenceSchema.Version;
    public List<AssetDefinition> Definitions { get; set; } = new List<AssetDefinition>();
    public List<FleetAsset> Assets { get; set; } = new List<FleetAsset>();
    public List<AssetBundle> Bundles { get; set; } = new List<AssetBundle>();
}

public sealed class VisibleVehicleIdentity
{
    public string? CarGuid { get; set; }
    public string? DefinitionId { get; set; }
}

public sealed class AssetValidationResult
{
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0;
}

public static class AssetRegistry
{
    public static AssetValidationResult Validate(AssetRegistrySnapshot? snapshot)
    {
        var errors = new List<string>();
        if (snapshot == null) return Result("Snapshot is missing.");
        if (!string.Equals(snapshot.Schema, AssetPersistenceSchema.Name, StringComparison.Ordinal)) errors.Add("Unsupported asset registry schema.");
        if (snapshot.SchemaVersion != AssetPersistenceSchema.Version) errors.Add("Unsupported asset registry schema version.");

        var definitions = snapshot.Definitions ?? new List<AssetDefinition>();
        var assets = snapshot.Assets ?? new List<FleetAsset>();
        var bundles = snapshot.Bundles ?? new List<AssetBundle>();
        var definitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId)) errors.Add("Definition ID is missing.");
            else if (!definitionIds.Add(definition.DefinitionId)) errors.Add("Duplicate definition ID: " + definition.DefinitionId);
        }

        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        var linkedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (asset == null) { errors.Add("Asset record is missing."); continue; }
            var assetId = asset.AssetId;
            var definitionId = asset.DefinitionId;
            if (!IsAssetId(assetId)) errors.Add("Invalid AssetId: " + (assetId ?? "<null>"));
            else if (!assetIds.Add(assetId!)) errors.Add("Duplicate AssetId: " + assetId);
            if (string.IsNullOrWhiteSpace(definitionId) || !definitionIds.Contains(definitionId!))
                errors.Add("Asset references an unknown definition: " + (definitionId ?? "<null>"));
            if (asset.GameLink == null || !string.Equals(asset.GameLink.Kind, PersistentVehicleLink.CarGuidKind, StringComparison.Ordinal))
                errors.Add("Asset has an unsupported persistent link kind: " + asset.AssetId);
            else if (!TryNormalizeGuid(asset.GameLink.Value, out var normalized))
                errors.Add("Asset has an invalid CarGUID link: " + asset.AssetId);
            else if (!linkedGuids.Add(normalized))
                errors.Add("Duplicate persistent CarGUID link: " + normalized);
        }

        var bundleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bundle in bundles)
        {
            if (bundle == null || !IsAssetId(bundle.BundleId) || !bundleIds.Add(bundle.BundleId)) errors.Add("Invalid or duplicate bundle ID.");
            if (bundle?.ComponentAssetIds == null || bundle.ComponentAssetIds.Count < 2) errors.Add("A bundle requires at least two component assets.");
            else foreach (var componentId in bundle.ComponentAssetIds)
                if (!assetIds.Contains(componentId)) errors.Add("Bundle references an unknown asset: " + componentId);
        }
        return new AssetValidationResult { Errors = errors };
    }

    public static void Reconcile(AssetRegistrySnapshot snapshot, IReadOnlyList<VisibleVehicleIdentity>? visibleVehicles)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var vehicles = visibleVehicles ?? Array.Empty<VisibleVehicleIdentity>();
        var validObservations = vehicles.Where(v => v != null && TryNormalizeGuid(v.CarGuid, out _)).ToArray();

        foreach (var asset in snapshot.Assets ?? new List<FleetAsset>())
        {
            var link = asset?.GameLink;
            if (link == null || string.IsNullOrWhiteSpace(link.Value))
            {
                if (asset != null) asset.GameLink = Missing("Persistent CarGUID link is absent.", link);
                continue;
            }
            if (!string.Equals(link.Kind, PersistentVehicleLink.CarGuidKind, StringComparison.Ordinal) ||
                !TryNormalizeGuid(link.Value, out var expectedGuid))
            {
                if (asset != null) asset.GameLink = Invalid("Persistent CarGUID link is missing or invalid.", link);
                continue;
            }

            var duplicateOwners = (snapshot.Assets ?? new List<FleetAsset>()).Count(candidate =>
                candidate?.GameLink != null && TryNormalizeGuid(candidate.GameLink.Value, out var other) &&
                string.Equals(other, expectedGuid, StringComparison.OrdinalIgnoreCase));
            var matches = validObservations.Where(v => TryNormalizeGuid(v.CarGuid, out var observed) &&
                string.Equals(observed, expectedGuid, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (duplicateOwners > 1 || matches.Length > 1)
            {
                Set(link, PersistentLinkState.Ambiguous, duplicateOwners > 1
                    ? "More than one economic asset claims the same CarGUID."
                    : "More than one visible vehicle exposes the same CarGUID.");
            }
            else if (matches.Length == 0)
                Set(link, PersistentLinkState.TemporarilyAbsent, "No exact CarGUID match is currently visible; the asset identity is retained.");
            else if (!string.Equals(asset!.DefinitionId, matches[0].DefinitionId, StringComparison.Ordinal))
                Set(link, PersistentLinkState.Ambiguous, "Exact CarGUID match has a conflicting definition and requires reconciliation.");
            else
                Set(link, PersistentLinkState.Resolved, "Exactly one visible vehicle matches CarGUID and definition.");
        }
    }

    private static bool IsAssetId(string? value) => value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;
    private static bool TryNormalizeGuid(string? value, out string normalized)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty) { normalized = id.ToString("D"); return true; }
        normalized = ""; return false;
    }
    private static AssetValidationResult Result(string error) => new AssetValidationResult { Errors = new[] { error } };
    private static PersistentVehicleLink Invalid(string detail, PersistentVehicleLink? source) => new PersistentVehicleLink
    {
        Kind = source?.Kind ?? PersistentVehicleLink.CarGuidKind,
        Value = source?.Value,
        ExpectedDefinitionId = source?.ExpectedDefinitionId,
        State = PersistentLinkState.Invalid,
        Detail = detail
    };
    private static PersistentVehicleLink Missing(string detail, PersistentVehicleLink? source) => new PersistentVehicleLink
    {
        Kind = source?.Kind ?? PersistentVehicleLink.CarGuidKind,
        Value = source?.Value,
        ExpectedDefinitionId = source?.ExpectedDefinitionId,
        State = PersistentLinkState.Missing,
        Detail = detail
    };
    private static void Set(PersistentVehicleLink link, PersistentLinkState state, string detail) { link.State = state; link.Detail = detail; }
}

public static class AssetRegistryJson
{
    private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(
        typeof(AssetRegistrySnapshot),
        new[] { typeof(AssetDefinition[]), typeof(FleetAsset[]), typeof(AssetBundle[]), typeof(string[]) });

    public static string Serialize(AssetRegistrySnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        using (var stream = new MemoryStream())
        {
            Serializer.WriteObject(stream, snapshot);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    public static AssetRegistrySnapshot Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Asset registry JSON is required.", nameof(json));
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var snapshot = Serializer.ReadObject(stream) as AssetRegistrySnapshot;
            if (snapshot == null) throw new InvalidDataException("Asset registry JSON did not contain a snapshot.");
            var validation = AssetRegistry.Validate(snapshot);
            if (!validation.IsValid) throw new InvalidDataException(string.Join("; ", validation.Errors));
            return snapshot;
        }
    }
}
