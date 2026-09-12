using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum FleetVehicleKind { Unknown, Locomotive, FreightWagon, PassengerCar }
public enum FleetOperationalState { Available, Reserved, InService, Maintenance, Stored, ReconcileRequired }
public enum FleetCommandAction { Rename, SetOperationalState, AssignOperator, TransferOwnership, ClearOperator, ConfirmPhysicalRemoval }
public enum FleetCommandOutcome { Succeeded, Rejected }

[DataContract]
public sealed class FleetAssetState
{
    [DataMember(Name = "assetId", Order = 1)] public string AssetId { get; set; } = "";
    [DataMember(Name = "kind", Order = 2)] public FleetVehicleKind Kind { get; set; }
    [DataMember(Name = "displayName", Order = 3)] public string DisplayName { get; set; } = "";
    [DataMember(Name = "operator", Order = 4)] public AssetOwnerRef? Operator { get; set; }
    [DataMember(Name = "operationalState", Order = 5)] public FleetOperationalState OperationalState { get; set; } = FleetOperationalState.Available;
    [DataMember(Name = "lastKnownLocation", Order = 6)] public string? LastKnownLocation { get; set; }
    [DataMember(Name = "version", Order = 7)] public long Version { get; set; }
}

[DataContract]
public sealed class FleetCommand
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 2)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 3)] public string AssetId { get; set; } = "";
    [DataMember(Name = "action", Order = 4)] public FleetCommandAction Action { get; set; }
    [DataMember(Name = "expectedFleetVersion", Order = 5)] public long ExpectedFleetVersion { get; set; }
    [DataMember(Name = "expectedOwnershipVersion", Order = 6)] public long ExpectedOwnershipVersion { get; set; }
    [DataMember(Name = "displayName", Order = 7)] public string? DisplayName { get; set; }
    [DataMember(Name = "operationalState", Order = 8)] public FleetOperationalState? OperationalState { get; set; }
    [DataMember(Name = "target", Order = 9)] public AssetOwnerRef? Target { get; set; }
}

[DataContract]
public sealed class FleetCommandRecord
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 3)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 4)] public string AssetId { get; set; } = "";
    [DataMember(Name = "action", Order = 5)] public FleetCommandAction Action { get; set; }
    [DataMember(Name = "outcome", Order = 6)] public FleetCommandOutcome Outcome { get; set; }
    [DataMember(Name = "resultCode", Order = 7)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "fleetVersionBefore", Order = 8)] public long FleetVersionBefore { get; set; }
    [DataMember(Name = "fleetVersionAfter", Order = 9)] public long FleetVersionAfter { get; set; }
    [DataMember(Name = "detail", Order = 10)] public string Detail { get; set; } = "";
}

public static class FleetVehicleClassifier
{
    public static FleetVehicleKind Classify(string? type, string? definitionId)
    {
        var value = ((type ?? "") + " " + (definitionId ?? "")).ToLowerInvariant();
        if (value.Contains("passenger") || value.Contains("coach")) return FleetVehicleKind.PassengerCar;
        if (value.Contains("loco") || value.Contains("shunter") || value.Contains("steam") || value.Contains("diesel")) return FleetVehicleKind.Locomotive;
        if (!string.IsNullOrWhiteSpace(type) || value.Contains("wagon") || value.Contains("car") || value.Contains("tender") || value.Contains("caboose") || value.Contains("flatbed") || value.Contains("gondola") || value.Contains("hopper") || value.Contains("tanker")) return FleetVehicleKind.FreightWagon;
        return FleetVehicleKind.Unknown;
    }
}

public sealed class FleetManagementEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;

    public FleetManagementEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        VehicleAcquisitionPersistence.Validate(state);
    }

    public static FleetAssetState EnsureAsset(VehicleAcquisitionSnapshot state, string assetId, string? type, string? definitionId, string? visibleName)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var existing = state.Fleet.SingleOrDefault(x => x.AssetId == assetId);
        if (existing != null)
        {
            var kind = FleetVehicleClassifier.Classify(type, definitionId);
            if (existing.Kind == FleetVehicleKind.Unknown && kind != FleetVehicleKind.Unknown) { existing.Kind = kind; existing.Version++; }
            return existing;
        }
        var displayName = string.IsNullOrWhiteSpace(visibleName) ? (definitionId ?? assetId) : visibleName!.Trim();
        if (displayName.Length > 48) displayName = displayName.Substring(0, 48);
        var created = new FleetAssetState
        {
            AssetId = assetId,
            Kind = FleetVehicleClassifier.Classify(type, definitionId),
            DisplayName = displayName,
            OperationalState = FleetOperationalState.Available
        };
        state.Fleet.Add(created);
        return created;
    }

    public FleetCommandRecord Execute(FleetCommand command)
    {
        lock (gate)
        {
            Require(command);
            var fingerprint = Fingerprint(command);
            var known = state.FleetCommands.SingleOrDefault(x => x.CommandId == command.CommandId);
            if (known != null)
            {
                if (known.Fingerprint != fingerprint) throw new InvalidOperationException("A fleet command ID cannot be reused with another payload.");
                return known;
            }

            var fleet = state.Fleet.SingleOrDefault(x => x.AssetId == command.AssetId);
            var ownership = state.Ownership.SingleOrDefault(x => x.AssetId == command.AssetId);
            var record = NewRecord(command, fingerprint, fleet?.Version ?? -1);
            var rejection = Validate(command, fleet, ownership);
            if (rejection != null) return Reject(record, rejection);

            switch (command.Action)
            {
                case FleetCommandAction.Rename:
                    fleet!.DisplayName = command.DisplayName!.Trim();
                    break;
                case FleetCommandAction.SetOperationalState:
                    fleet!.OperationalState = command.OperationalState!.Value;
                    break;
                case FleetCommandAction.AssignOperator:
                    fleet!.Operator = Clone(command.Target!);
                    break;
                case FleetCommandAction.TransferOwnership:
                    ownership!.Owner = Clone(command.Target!);
                    ownership.Version++;
                    fleet!.Operator = Clone(command.Target!);
                    break;
                case FleetCommandAction.ClearOperator:
                    fleet!.Operator = null;
                    break;
                default:
                    return Reject(record, "unsupported-action");
            }

            fleet!.Version++;
            record.Outcome = FleetCommandOutcome.Succeeded;
            record.ResultCode = "applied";
            record.FleetVersionAfter = fleet.Version;
            record.Detail = "owner=" + ownership!.Owner.Key + ";operator=" + (fleet.Operator?.Key ?? "none") + ";state=" + fleet.OperationalState;
            state.FleetCommands.Add(record);
            return record;
        }
    }

    public FleetCommandRecord ConfirmPhysicalRemoval(string commandId, string persistentCarGuid, string source)
    {
        lock (gate)
        {
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason);
            if (string.IsNullOrWhiteSpace(commandId) || !Guid.TryParse(persistentCarGuid, out var expected) || expected == Guid.Empty)
                throw new ArgumentException("A permanent physical-removal identity is required.");
            var asset = state.Assets.Assets.SingleOrDefault(value => Guid.TryParse(value.GameLink?.Value, out var actual) && actual == expected);
            if (asset == null) throw new InvalidOperationException("Physical removal does not match a managed asset.");
            var fingerprint = persistentCarGuid.ToLowerInvariant() + "|" + (source ?? "");
            var known = state.FleetCommands.SingleOrDefault(value => value.CommandId == commandId);
            if (known != null) { if (known.Fingerprint != fingerprint) throw new InvalidOperationException("A fleet command ID cannot be reused with another payload."); return known; }
            var fleet = state.Fleet.SingleOrDefault(value => value.AssetId == asset.AssetId);
            var version = fleet?.Version ?? -1;
            var record = new FleetCommandRecord { CommandId = commandId, Fingerprint = fingerprint, RequesterId = "host", AssetId = asset.AssetId, Action = FleetCommandAction.ConfirmPhysicalRemoval, Outcome = FleetCommandOutcome.Succeeded, ResultCode = fleet == null ? "already-removed" : "physical-removal-confirmed", FleetVersionBefore = version, FleetVersionAfter = version < 0 ? -1 : version + 1, Detail = "source=" + (source ?? "unknown") + ";carGuid=" + persistentCarGuid };
            if (fleet != null) state.Fleet.Remove(fleet);
            state.AssetLifecycle.Records.RemoveAll(value => value.AssetId == asset.AssetId);
            state.FleetCommands.Add(record);
            return record;
        }
    }

    private string? Validate(FleetCommand command, FleetAssetState? fleet, AssetOwnership? ownership)
    {
        if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return "host-authority-required";
        if (fleet == null || ownership == null) return "asset-not-managed";
        if (fleet.Version != command.ExpectedFleetVersion || ownership.Version != command.ExpectedOwnershipVersion) return "version-mismatch";
        var player = state.Economy.Players.SingleOrDefault(x => x.PlayerId == command.RequesterId);
        if (player == null) return "unknown-requester";
        var ownerControl = CanManage(ownership.Owner, player);
        var leaseControl = !ownerControl && CanOperateLease(command.AssetId, player);
        if (!ownerControl && !leaseControl) return "fleet-permission-denied";
        if (leaseControl && command.Action != FleetCommandAction.Rename && command.Action != FleetCommandAction.SetOperationalState) return "leased-asset-operation-only";

        if (command.Action == FleetCommandAction.Rename)
        {
            var name = command.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name!.Length > 48) return "invalid-display-name";
        }
        else if (command.Action == FleetCommandAction.SetOperationalState)
        {
            if (!command.OperationalState.HasValue || !CanTransition(fleet.OperationalState, command.OperationalState.Value)) return "invalid-state-transition";
        }
        else if (command.Action == FleetCommandAction.AssignOperator)
        {
            if (!ValidTarget(command.Target, player)) return "invalid-operator";
            if (fleet.OperationalState == FleetOperationalState.ReconcileRequired) return "asset-reconciliation-required";
        }
        else if (command.Action == FleetCommandAction.TransferOwnership)
        {
            if (!ValidTarget(command.Target, player)) return "invalid-target-owner";
            if (fleet.OperationalState != FleetOperationalState.Available && fleet.OperationalState != FleetOperationalState.Stored) return "asset-not-transferable";
            if (ownership.Owner.Kind == AssetOwnerKind.Player && command.Target!.Kind != AssetOwnerKind.Company) return "invalid-player-transfer";
            if (ownership.Owner.Kind == AssetOwnerKind.Company && command.Target!.Kind != AssetOwnerKind.Player) return "invalid-company-transfer";
        }
        else if (command.Action == FleetCommandAction.ClearOperator)
        {
            if (fleet.OperationalState == FleetOperationalState.InService || fleet.OperationalState == FleetOperationalState.Reserved || fleet.OperationalState == FleetOperationalState.ReconcileRequired) return "operator-is-active";
        }
        else return "unsupported-action";
        return null;
    }

    private bool CanManage(AssetOwnerRef owner, PlayerEconomicState player)
    {
        if (owner.Kind == AssetOwnerKind.Player) return owner.OwnerId == player.PlayerId;
        if (owner.Kind != AssetOwnerKind.Company || player.CompanyId != owner.OwnerId) return false;
        var company = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == owner.OwnerId);
        return company != null && !company.Liquidating && (company.LeaderId == player.PlayerId ||
            (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var permissions) && permissions.Contains(CompanyPermission.ManageFleet)));
    }

    private bool CanOperateLease(string assetId, PlayerEconomicState player) => state.Leases.Any(x => x.AssetIds.Contains(assetId) &&
        (x.State == LeaseState.Active || x.State == LeaseState.Delinquent || x.State == LeaseState.ReturnDue) && x.Lessee != null &&
        ((x.Lessee.Kind == AssetOwnerKind.Player && x.Lessee.OwnerId == player.PlayerId) ||
         (x.Lessee.Kind == AssetOwnerKind.Company && x.Lessee.OwnerId == player.CompanyId)));

    private static bool ValidTarget(AssetOwnerRef? target, PlayerEconomicState player) => target != null &&
        ((target.Kind == AssetOwnerKind.Player && target.OwnerId == player.PlayerId) ||
         (target.Kind == AssetOwnerKind.Company && !string.IsNullOrWhiteSpace(player.CompanyId) && target.OwnerId == player.CompanyId));

    private static bool CanTransition(FleetOperationalState from, FleetOperationalState to)
    {
        if (from == to) return true;
        if (from == FleetOperationalState.Reserved || from == FleetOperationalState.ReconcileRequired) return false;
        if (to == FleetOperationalState.Reserved || to == FleetOperationalState.ReconcileRequired) return false;
        if (from == FleetOperationalState.InService) return to == FleetOperationalState.Available;
        return true;
    }

    private FleetCommandRecord Reject(FleetCommandRecord record, string code)
    {
        record.Outcome = FleetCommandOutcome.Rejected;
        record.ResultCode = code;
        record.FleetVersionAfter = record.FleetVersionBefore;
        state.FleetCommands.Add(record);
        return record;
    }

    private static FleetCommandRecord NewRecord(FleetCommand command, string fingerprint, long version) => new FleetCommandRecord
    {
        CommandId = command.CommandId,
        Fingerprint = fingerprint,
        RequesterId = command.RequesterId,
        AssetId = command.AssetId,
        Action = command.Action,
        FleetVersionBefore = version,
        FleetVersionAfter = version
    };

    private static string Fingerprint(FleetCommand command) => string.Join("|", command.RequesterId, command.AssetId, command.Action,
        command.ExpectedFleetVersion, command.ExpectedOwnershipVersion, command.DisplayName ?? "", command.OperationalState?.ToString() ?? "", command.Target?.Key ?? "");

    private static AssetOwnerRef Clone(AssetOwnerRef value) => new AssetOwnerRef { Kind = value.Kind, OwnerId = value.OwnerId };

    private static void Require(FleetCommand command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.CommandId) || string.IsNullOrWhiteSpace(command.RequesterId) || string.IsNullOrWhiteSpace(command.AssetId))
            throw new ArgumentException("A complete fleet command is required.");
    }
}
