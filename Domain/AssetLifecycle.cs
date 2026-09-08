using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum AssetLifecycleStatus { Active, TemporarilyAbsent, SuspendedMissingContent, Ambiguous, LocationStale }
public enum LifecycleProtectionStatus { NotRequired, Applied, Pending }

[DataContract]
public sealed class AssetLifecycleObservation
{
    [DataMember(Name = "persistentCarGuid", Order = 1)] public string PersistentCarGuid { get; set; } = "";
    [DataMember(Name = "definitionId", Order = 2)] public string DefinitionId { get; set; } = "";
    [DataMember(Name = "trackId", Order = 3)] public string? TrackId { get; set; }
    [DataMember(Name = "mapRevision", Order = 4)] public string? MapRevision { get; set; }
    [DataMember(Name = "visibleNumber", Order = 5)] public string? VisibleNumber { get; set; }
    [DataMember(Name = "liveryId", Order = 6)] public string? LiveryId { get; set; }
}

[DataContract]
public sealed class AssetLifecycleRecord
{
    [DataMember(Name = "assetId", Order = 1)] public string AssetId { get; set; } = "";
    [DataMember(Name = "status", Order = 2)] public AssetLifecycleStatus Status { get; set; }
    [DataMember(Name = "protectionStatus", Order = 3)] public LifecycleProtectionStatus ProtectionStatus { get; set; }
    [DataMember(Name = "operationalStateBeforeSuspension", Order = 4)] public FleetOperationalState OperationalStateBeforeSuspension { get; set; } = FleetOperationalState.Available;
    [DataMember(Name = "lastKnownMapRevision", Order = 5)] public string? LastKnownMapRevision { get; set; }
    [DataMember(Name = "lastKnownTrackId", Order = 6)] public string? LastKnownTrackId { get; set; }
    [DataMember(Name = "detail", Order = 7)] public string Detail { get; set; } = "";
    [DataMember(Name = "version", Order = 8)] public long Version { get; set; }
}

[DataContract]
public sealed class AssetLifecycleState
{
    [DataMember(Name = "records", Order = 1)] public List<AssetLifecycleRecord> Records { get; set; } = new List<AssetLifecycleRecord>();
    [DataMember(Name = "commands", Order = 2)] public List<MissionAssignmentCommand> Commands { get; set; } = new List<MissionAssignmentCommand>();
}

public interface IAssetLifecycleProtectionPort
{
    bool Available { get; }
    WorldOwnershipOutcome Protect(string operationId, IReadOnlyList<string> persistentCarGuids);
}

public sealed class DisabledAssetLifecycleProtectionPort : IAssetLifecycleProtectionPort
{
    public bool Available => false;
    public WorldOwnershipOutcome Protect(string operationId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.NotApplied;
}

public static class AssetLifecycleProtectionPolicy
{
    public static bool IsProtected(VehicleAcquisitionSnapshot state, string persistentCarGuid)
    {
        if (state == null || !Guid.TryParse(persistentCarGuid, out var expected) || expected == Guid.Empty) return false;
        var asset = state.Assets.Assets.SingleOrDefault(x => Guid.TryParse(x.GameLink?.Value, out var actual) && actual == expected);
        if (asset == null) return false;
        return state.Ownership.Any(x => x.AssetId == asset.AssetId && x.Owner.Kind != AssetOwnerKind.Merchant) || HasActiveContract(state, asset.AssetId);
    }

    public static IReadOnlyList<string> ProtectedPersistentCarGuids(VehicleAcquisitionSnapshot state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        return state.Assets.Assets.Select(x => x.GameLink?.Value).Where(x => !string.IsNullOrWhiteSpace(x) && IsProtected(state, x!))
            .Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasActiveContract(VehicleAcquisitionSnapshot state, string assetId) =>
        state.Leases.Any(x => x.AssetIds.Contains(assetId) && x.State != LeaseState.Returned && x.State != LeaseState.Purchased && x.State != LeaseState.Cancelled) ||
        state.OutboundLeases.Any(x => x.AssetIds.Contains(assetId) && x.State != OutboundLeaseState.Returned && x.State != OutboundLeaseState.Cancelled) ||
        state.Assignments.Any(x => x.AssetIds.Contains(assetId) && x.State != MissionAssignmentState.Completed && x.State != MissionAssignmentState.Cancelled);
}

public sealed class AssetLifecycleEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IAssetLifecycleProtectionPort protection;
    public AssetLifecycleEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IAssetLifecycleProtectionPort protection) { this.state = state ?? throw new ArgumentNullException(nameof(state)); this.authority = authority ?? throw new ArgumentNullException(nameof(authority)); this.protection = protection ?? throw new ArgumentNullException(nameof(protection)); VehicleAcquisitionPersistence.Validate(state); }

    public IReadOnlyList<AssetLifecycleRecord> Reconcile(string commandId, IReadOnlyList<AssetLifecycleObservation> observations,
        IReadOnlyCollection<string> loadedDefinitionIds, string mapRevision, IReadOnlyCollection<string> validTrackIds)
    {
        lock (gate)
        {
            RequireHost(); observations = observations ?? Array.Empty<AssetLifecycleObservation>(); loadedDefinitionIds = loadedDefinitionIds ?? Array.Empty<string>(); validTrackIds = validTrackIds ?? Array.Empty<string>();
            var fingerprint = string.Join("|", mapRevision, string.Join(",", observations.OrderBy(x => x.PersistentCarGuid).Select(x => x.PersistentCarGuid + ":" + x.DefinitionId + ":" + x.TrackId)), string.Join(",", loadedDefinitionIds.OrderBy(x => x)), string.Join(",", validTrackIds.OrderBy(x => x)));
            var replay = state.AssetLifecycle.Commands.SingleOrDefault(x => x.CommandId == commandId); if (replay != null) { if (replay.Fingerprint != fingerprint) throw new InvalidOperationException("Asset lifecycle command ID payload conflict."); return state.AssetLifecycle.Records.ToArray(); }
            var protectedAssets = state.Ownership.Where(x => x.Owner.Kind != AssetOwnerKind.Merchant || HasActiveContract(x.AssetId)).Select(x => x.AssetId).Distinct(StringComparer.Ordinal).ToArray();
            var resolvedLinks = new List<string>();
            foreach (var assetId in protectedAssets) ReconcileAsset(assetId, observations, loadedDefinitionIds, mapRevision, validTrackIds, resolvedLinks);
            var protectionOutcome = resolvedLinks.Count == 0 ? WorldOwnershipOutcome.Applied : protection.Available ? protection.Protect(commandId + ":cleanup-protection", resolvedLinks) : WorldOwnershipOutcome.Unknown;
            foreach (var record in state.AssetLifecycle.Records.Where(x => protectedAssets.Contains(x.AssetId)))
            {
                var link = state.Assets.Assets.Single(x => x.AssetId == record.AssetId).GameLink.Value;
                record.ProtectionStatus = string.IsNullOrWhiteSpace(link) || !resolvedLinks.Contains(link!) ? LifecycleProtectionStatus.NotRequired : protectionOutcome == WorldOwnershipOutcome.Applied ? LifecycleProtectionStatus.Applied : LifecycleProtectionStatus.Pending;
            }
            state.AssetLifecycle.Commands.Add(new MissionAssignmentCommand { CommandId = commandId, Fingerprint = fingerprint, AssignmentId = "lifecycle", ResultCode = protectionOutcome.ToString() }); return state.AssetLifecycle.Records.Where(x => protectedAssets.Contains(x.AssetId)).ToArray();
        }
    }

    private void ReconcileAsset(string assetId, IReadOnlyList<AssetLifecycleObservation> observations, IReadOnlyCollection<string> loadedDefinitions,
        string mapRevision, IReadOnlyCollection<string> validTracks, List<string> resolvedLinks)
    {
        var asset = state.Assets.Assets.Single(x => x.AssetId == assetId); var fleet = state.Fleet.SingleOrDefault(x => x.AssetId == assetId); var record = state.AssetLifecycle.Records.SingleOrDefault(x => x.AssetId == assetId);
        if (record == null) { record = new AssetLifecycleRecord { AssetId = assetId, OperationalStateBeforeSuspension = fleet?.OperationalState ?? FleetOperationalState.Available }; state.AssetLifecycle.Records.Add(record); }
        if (!loadedDefinitions.Contains(asset.DefinitionId)) { Suspend(record, fleet, asset.GameLink, AssetLifecycleStatus.SuspendedMissingContent, PersistentLinkState.TemporarilyAbsent, "content-definition-unavailable; record retained; no substitution or refund"); return; }
        var matches = observations.Where(x => GuidEquals(x.PersistentCarGuid, asset.GameLink.Value)).ToArray();
        if (matches.Length == 0) { Suspend(record, fleet, asset.GameLink, AssetLifecycleStatus.TemporarilyAbsent, PersistentLinkState.TemporarilyAbsent, "physical-representation-temporarily-absent; no respawn requested"); return; }
        if (matches.Length != 1 || matches[0].DefinitionId != asset.DefinitionId) { Suspend(record, fleet, asset.GameLink, AssetLifecycleStatus.Ambiguous, PersistentLinkState.Ambiguous, "persistent identity is ambiguous or definition conflicts"); return; }
        var observation = matches[0]; asset.GameLink.State = PersistentLinkState.Resolved; asset.GameLink.Detail = "Exact CarGUID and definition reconciled; visible number and livery are non-identity metadata."; resolvedLinks.Add(asset.GameLink.Value!);
        record.LastKnownMapRevision = mapRevision; record.LastKnownTrackId = observation.TrackId;
        if (!string.IsNullOrWhiteSpace(observation.TrackId) && !validTracks.Contains(observation.TrackId!)) { Suspend(record, fleet, asset.GameLink, AssetLifecycleStatus.LocationStale, PersistentLinkState.Resolved, "track identifier is obsolete; respawn and teleport are forbidden"); return; }
        record.Status = AssetLifecycleStatus.Active; record.Detail = "asset-reconciled"; record.Version++;
        if (fleet != null && fleet.OperationalState == FleetOperationalState.ReconcileRequired) { fleet.OperationalState = record.OperationalStateBeforeSuspension == FleetOperationalState.ReconcileRequired ? FleetOperationalState.Available : record.OperationalStateBeforeSuspension; fleet.LastKnownLocation = observation.TrackId ?? fleet.LastKnownLocation; fleet.Version++; }
    }

    private static void Suspend(AssetLifecycleRecord record, FleetAssetState? fleet, PersistentVehicleLink link, AssetLifecycleStatus status, PersistentLinkState linkState, string detail)
    {
        if (fleet != null && fleet.OperationalState != FleetOperationalState.ReconcileRequired) record.OperationalStateBeforeSuspension = fleet.OperationalState;
        record.Status = status; record.Detail = detail; record.Version++;
        link.State = linkState; link.Detail = detail;
        if (fleet != null && fleet.OperationalState != FleetOperationalState.ReconcileRequired) { fleet.OperationalState = FleetOperationalState.ReconcileRequired; fleet.Version++; }
    }

    private bool HasActiveContract(string assetId) => state.Leases.Any(x => x.AssetIds.Contains(assetId) && x.State != LeaseState.Returned && x.State != LeaseState.Purchased && x.State != LeaseState.Cancelled) || state.OutboundLeases.Any(x => x.AssetIds.Contains(assetId) && x.State != OutboundLeaseState.Returned && x.State != OutboundLeaseState.Cancelled) || state.Assignments.Any(x => x.AssetIds.Contains(assetId) && x.State != MissionAssignmentState.Completed && x.State != MissionAssignmentState.Cancelled);
    private static bool GuidEquals(string left, string? right) => Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b) && a == b;
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
}

public static class AssetLifecycleValidation
{
    public static void Validate(AssetLifecycleState state, VehicleAcquisitionSnapshot snapshot)
    {
        if (state == null || state.Records.GroupBy(x => x.AssetId).Any(x => x.Count() != 1) || state.Commands.GroupBy(x => x.CommandId).Any(x => x.Count() != 1) || state.Records.Any(x => !snapshot.Assets.Assets.Any(a => a.AssetId == x.AssetId))) throw new InvalidOperationException("Invalid asset lifecycle state.");
    }
}
