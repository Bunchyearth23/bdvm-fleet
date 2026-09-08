using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum OutboundLeaseState { Offered, ActivationPending, Active, ReturnDue, ReturnPending, Returned, Cancelled }
public enum OutboundLeaseActionState { Succeeded, Rejected, ReconcileRequired }

public interface IOutboundLeaseSimulationPort
{
    bool Available { get; }
    WorldOwnershipOutcome Begin(string operationId, IReadOnlyList<string> persistentCarGuids, string declaredDestination);
    WorldOwnershipOutcome InspectBegin(string operationId, IReadOnlyList<string> persistentCarGuids);
    WorldOwnershipOutcome Return(string operationId, IReadOnlyList<string> persistentCarGuids, string returnLocation);
    WorldOwnershipOutcome InspectReturn(string operationId, IReadOnlyList<string> persistentCarGuids, string returnLocation);
}

/// <summary>
/// Explicit economic simulation: it never drives, despawns, teleports or otherwise mutates a Unity vehicle.
/// A future world adapter can replace this port without changing the contract engine.
/// </summary>
public sealed class DeclaredOffSceneLeaseSimulationPort : IOutboundLeaseSimulationPort
{
    public bool Available => true;
    public WorldOwnershipOutcome Begin(string operationId, IReadOnlyList<string> persistentCarGuids, string declaredDestination) => WorldOwnershipOutcome.Applied;
    public WorldOwnershipOutcome InspectBegin(string operationId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.Applied;
    public WorldOwnershipOutcome Return(string operationId, IReadOnlyList<string> persistentCarGuids, string returnLocation) => WorldOwnershipOutcome.Applied;
    public WorldOwnershipOutcome InspectReturn(string operationId, IReadOnlyList<string> persistentCarGuids, string returnLocation) => WorldOwnershipOutcome.Applied;
}

public sealed class OutboundLeaseCompanyContractCancellationPort : ICompanyContractCancellationPort
{
    private readonly VehicleAcquisitionSnapshot state;
    private readonly OutboundLeaseEngine engine;
    public OutboundLeaseCompanyContractCancellationPort(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IAssetReleaseGuard releaseGuard, IOutboundLeaseSimulationPort simulation)
    { this.state = state ?? throw new ArgumentNullException(nameof(state)); engine = new OutboundLeaseEngine(state, authority, releaseGuard, simulation); }
    public WorldOwnershipOutcome Cancel(string operationId, string companyId)
    {
        try { engine.CancelForCompanyLiquidation(operationId, companyId); return Inspect(companyId); }
        catch { return WorldOwnershipOutcome.Unknown; }
    }
    public WorldOwnershipOutcome Inspect(string companyId) => state.OutboundLeases.Any(x => x.Owner.Kind == AssetOwnerKind.Company && x.Owner.OwnerId == companyId && x.State != OutboundLeaseState.Returned && x.State != OutboundLeaseState.Cancelled) ? WorldOwnershipOutcome.NotApplied : WorldOwnershipOutcome.Applied;
}

[DataContract]
public sealed class OutboundLeaseInstallment
{
    [DataMember(Name = "dueTick", Order = 1)] public long DueTick { get; set; }
    [DataMember(Name = "amount", Order = 2)] public long Amount { get; set; }
    [DataMember(Name = "credited", Order = 3)] public bool Credited { get; set; }
    [DataMember(Name = "ledgerEntryId", Order = 4)] public string? LedgerEntryId { get; set; }
}

[DataContract]
public sealed class OutboundLeaseContract
{
    [DataMember(Name = "contractId", Order = 1)] public string ContractId { get; set; } = "";
    [DataMember(Name = "assetIds", Order = 2)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "owner", Order = 3)] public AssetOwnerRef Owner { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "beneficiary", Order = 4)] public AccountRef Beneficiary { get; set; } = new AccountRef();
    [DataMember(Name = "externalLesseeId", Order = 5)] public string ExternalLesseeId { get; set; } = "market-lessee";
    [DataMember(Name = "declaredDestination", Order = 6)] public string DeclaredDestination { get; set; } = "off-scene-market";
    [DataMember(Name = "originLocation", Order = 7)] public string OriginLocation { get; set; } = "unknown";
    [DataMember(Name = "returnLocation", Order = 8)] public string ReturnLocation { get; set; } = "unknown";
    [DataMember(Name = "conditionAtStart", Order = 9)] public decimal ConditionAtStart { get; set; }
    [DataMember(Name = "conditionAtReturn", Order = 10)] public decimal? ConditionAtReturn { get; set; }
    [DataMember(Name = "rentAmount", Order = 11)] public long RentAmount { get; set; }
    [DataMember(Name = "rentIntervalTicks", Order = 12)] public long RentIntervalTicks { get; set; }
    [DataMember(Name = "durationTicks", Order = 13)] public long DurationTicks { get; set; }
    [DataMember(Name = "earlyRecallFee", Order = 14)] public long EarlyRecallFee { get; set; }
    [DataMember(Name = "startTick", Order = 15)] public long StartTick { get; set; }
    [DataMember(Name = "endTick", Order = 16)] public long EndTick { get; set; }
    [DataMember(Name = "nextDueTick", Order = 17)] public long NextDueTick { get; set; }
    [DataMember(Name = "state", Order = 18)] public OutboundLeaseState State { get; set; }
    [DataMember(Name = "installments", Order = 19)] public List<OutboundLeaseInstallment> Installments { get; set; } = new List<OutboundLeaseInstallment>();
    [DataMember(Name = "pendingOperationId", Order = 20)] public string? PendingOperationId { get; set; }
    [DataMember(Name = "version", Order = 21)] public long Version { get; set; }
}

[DataContract]
public sealed class OutboundLeaseActionRecord
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; } = "";
    [DataMember(Name = "contractId", Order = 3)] public string ContractId { get; set; } = "";
    [DataMember(Name = "state", Order = 4)] public OutboundLeaseActionState State { get; set; }
    [DataMember(Name = "resultCode", Order = 5)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "amount", Order = 6)] public long Amount { get; set; }
}

public sealed class OutboundLeaseEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IAssetReleaseGuard releaseGuard;
    private readonly IOutboundLeaseSimulationPort simulation;

    public OutboundLeaseEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IAssetReleaseGuard releaseGuard, IOutboundLeaseSimulationPort simulation)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.releaseGuard = releaseGuard ?? throw new ArgumentNullException(nameof(releaseGuard));
        this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        VehicleAcquisitionPersistence.Validate(state);
    }

    public OutboundLeaseContract Publish(string commandId, string requesterId, string contractId, IReadOnlyList<string> assetIds,
        long rent, long interval, long duration, long earlyRecallFee, decimal condition, string destination, string returnLocation)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = string.Join("|", requesterId, contractId, string.Join(",", assetIds ?? Array.Empty<string>()), rent, interval, duration, earlyRecallFee, condition, destination, returnLocation);
            var replay = Known(commandId, fingerprint); if (replay != null) return state.OutboundLeases.Single(x => x.ContractId == replay.ContractId);
            if (string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(requesterId) || string.IsNullOrWhiteSpace(contractId) || assetIds == null || assetIds.Count == 0 || rent < 0 || interval <= 0 || duration <= 0 || earlyRecallFee < 0 || condition < 0m || condition > 1m || string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(returnLocation)) throw new ArgumentException("Invalid outbound lease terms.");
            if (state.OutboundLeases.Any(x => x.ContractId == contractId)) throw new InvalidOperationException("Outbound lease contract identity already exists.");
            var ids = assetIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (ids.Count != assetIds.Count) throw new InvalidOperationException("Outbound lease assets must be unique.");
            EnsureCompleteBundles(ids);
            var player = state.Economy.Players.SingleOrDefault(x => x.PlayerId == requesterId) ?? throw new InvalidOperationException("Unknown requester.");
            var owners = ids.Select(id => state.Ownership.Single(x => x.AssetId == id).Owner).ToArray();
            if (owners.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != 1 || !CanManage(player, owners[0])) throw new InvalidOperationException("Explicit owner fleet permission is required.");
            var owner = Clone(owners[0]);
            if (owner.Kind == AssetOwnerKind.Merchant) throw new InvalidOperationException("Only personal or company assets can be leased out.");
            foreach (var id in ids)
            {
                var fleet = state.Fleet.SingleOrDefault(x => x.AssetId == id) ?? throw new InvalidOperationException("Every outbound lease asset must be in the fleet registry.");
                if ((fleet.OperationalState != FleetOperationalState.Available && fleet.OperationalState != FleetOperationalState.Stored) || fleet.Operator != null) throw new InvalidOperationException("Outbound lease asset is not idle.");
                if (state.Leases.Any(x => x.AssetIds.Contains(id) && !Terminal(x.State)) || state.Assignments.Any(x => x.AssetIds.Contains(id) && x.State != MissionAssignmentState.Completed && x.State != MissionAssignmentState.Cancelled) || state.OutboundLeases.Any(x => x.AssetIds.Contains(id) && !Terminal(x.State))) throw new InvalidOperationException("Asset already belongs to an active contract workflow.");
                var asset = state.Assets.Assets.Single(x => x.AssetId == id);
                var inspected = releaseGuard.Inspect(asset.GameLink.Value!);
                if (inspected.Status != AssetReleaseStatus.Releasable) throw new InvalidOperationException("Outbound lease asset is not safely releasable: " + inspected.Status + ".");
            }
            var origin = state.Fleet.Where(x => ids.Contains(x.AssetId)).Select(x => x.LastKnownLocation).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "unknown";
            var contract = new OutboundLeaseContract { ContractId = contractId, AssetIds = ids, Owner = owner,
                Beneficiary = owner.Kind == AssetOwnerKind.Player ? AccountRef.Player(owner.OwnerId) : AccountRef.Company(owner.OwnerId),
                RentAmount = rent, RentIntervalTicks = interval, DurationTicks = duration, EarlyRecallFee = earlyRecallFee,
                ConditionAtStart = condition, OriginLocation = origin, DeclaredDestination = destination.Trim(), ReturnLocation = returnLocation.Trim(),
                State = OutboundLeaseState.Offered, Version = 1 };
            state.OutboundLeases.Add(contract);
            foreach (var id in ids) { var fleet = state.Fleet.Single(x => x.AssetId == id); fleet.OperationalState = FleetOperationalState.Reserved; fleet.Version++; }
            Record(commandId, fingerprint, contractId, OutboundLeaseActionState.Succeeded, "outbound-lease-published");
            return contract;
        }
    }

    public OutboundLeaseActionRecord Activate(string commandId, string requesterId, string contractId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + contractId + "|activate"; var known = Known(commandId, fingerprint); if (known != null) return known;
            var contract = state.OutboundLeases.Single(x => x.ContractId == contractId); var action = Record(commandId, fingerprint, contractId, OutboundLeaseActionState.Rejected, "prepared");
            if (contract.State != OutboundLeaseState.Offered || !RequesterControls(requesterId, contract.Owner)) return Reject(action, "outbound-lease-activation-refused");
            if (!simulation.Available) return Reject(action, "outbound-lease-simulation-unavailable");
            var outcome = simulation.Begin(commandId + ":begin", Links(contract), contract.DeclaredDestination);
            if (outcome == WorldOwnershipOutcome.NotApplied) return Reject(action, "outbound-lease-begin-refused");
            if (outcome == WorldOwnershipOutcome.Unknown) { contract.State = OutboundLeaseState.ActivationPending; contract.PendingOperationId = commandId; contract.Version++; return Pending(action, "outbound-lease-begin-pending"); }
            return CommitActivation(action, contract);
        }
    }

    public long ProcessClock(string commandId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "clock|" + state.LeaseClock.ActiveTick; var known = Known(commandId, fingerprint); if (known != null) return known.Amount;
            long total = 0;
            foreach (var contract in state.OutboundLeases.Where(x => x.State == OutboundLeaseState.Active).ToArray())
            {
                while (contract.NextDueTick <= state.LeaseClock.ActiveTick && contract.NextDueTick <= contract.EndTick)
                {
                    var due = contract.NextDueTick; var installment = contract.Installments.SingleOrDefault(x => x.DueTick == due);
                    if (installment == null) { installment = new OutboundLeaseInstallment { DueTick = due, Amount = contract.RentAmount }; contract.Installments.Add(installment); }
                    if (!installment.Credited)
                    {
                        var wallet = state.Economy.Wallets.Single(x => x.Account.Key == contract.Beneficiary.Key);
                        wallet.Balance = checked(wallet.Balance + installment.Amount); wallet.Version++;
                        installment.Credited = true; installment.LedgerEntryId = contract.ContractId + ":outbound-rent:" + due;
                        if (installment.Amount > 0 && !state.Economy.Ledger.Any(x => x.EntryId == installment.LedgerEntryId)) state.Economy.Ledger.Add(new LedgerEntry { EntryId = installment.LedgerEntryId, CommandId = commandId, Kind = LedgerEntryKind.OutboundLeaseRent, Credit = Clone(contract.Beneficiary), Amount = installment.Amount, Detail = "outbound-lease-rent;contract=" + contract.ContractId + ";due=" + due + ";simulation=declared-off-scene" });
                        total = checked(total + installment.Amount);
                    }
                    contract.NextDueTick = checked(contract.NextDueTick + contract.RentIntervalTicks); contract.Version++;
                }
                if (state.LeaseClock.ActiveTick >= contract.EndTick) { contract.State = OutboundLeaseState.ReturnDue; contract.Version++; }
            }
            Record(commandId, fingerprint, "clock", OutboundLeaseActionState.Succeeded, "outbound-lease-clock-processed", total);
            return total;
        }
    }

    public OutboundLeaseActionRecord Return(string commandId, string contractId, decimal conditionAtReturn, string? returnLocation = null)
    {
        lock (gate)
        {
            RequireHost(); var location = string.IsNullOrWhiteSpace(returnLocation) ? state.OutboundLeases.Single(x => x.ContractId == contractId).ReturnLocation : returnLocation!.Trim();
            var fingerprint = string.Join("|", contractId, "return", conditionAtReturn, location); var known = Known(commandId, fingerprint); if (known != null) return known;
            var contract = state.OutboundLeases.Single(x => x.ContractId == contractId); var action = Record(commandId, fingerprint, contractId, OutboundLeaseActionState.Rejected, "prepared");
            if ((contract.State != OutboundLeaseState.Active && contract.State != OutboundLeaseState.ReturnDue) || conditionAtReturn < 0m || conditionAtReturn > 1m) return Reject(action, "outbound-lease-not-returnable");
            var outcome = simulation.Return(commandId + ":return", Links(contract), location);
            if (outcome == WorldOwnershipOutcome.NotApplied) return Reject(action, "outbound-lease-return-refused");
            contract.ConditionAtReturn = conditionAtReturn; contract.ReturnLocation = location;
            if (outcome == WorldOwnershipOutcome.Unknown) { contract.State = OutboundLeaseState.ReturnPending; contract.PendingOperationId = commandId; contract.Version++; return Pending(action, "outbound-lease-return-pending"); }
            return CommitReturn(action, contract, false);
        }
    }

    public OutboundLeaseActionRecord Recall(string commandId, string requesterId, string contractId, decimal conditionAtReturn, string? returnLocation = null)
    {
        lock (gate)
        {
            RequireHost(); var contract = state.OutboundLeases.Single(x => x.ContractId == contractId); var location = string.IsNullOrWhiteSpace(returnLocation) ? contract.ReturnLocation : returnLocation!.Trim();
            var fingerprint = string.Join("|", requesterId, contractId, "recall", conditionAtReturn, location); var known = Known(commandId, fingerprint); if (known != null) return known;
            var action = Record(commandId, fingerprint, contractId, OutboundLeaseActionState.Rejected, "prepared");
            if (!RequesterControls(requesterId, contract.Owner) || conditionAtReturn < 0m || conditionAtReturn > 1m) return Reject(action, "outbound-lease-recall-refused");
            if (contract.State == OutboundLeaseState.Offered) { contract.ConditionAtReturn = conditionAtReturn; contract.ReturnLocation = location; return CommitReturn(action, contract, true); }
            if (contract.State != OutboundLeaseState.Active && contract.State != OutboundLeaseState.ReturnDue) return Reject(action, "outbound-lease-recall-refused");
            var wallet = state.Economy.Wallets.Single(x => x.Account.Key == contract.Beneficiary.Key);
            if (wallet.Balance < contract.EarlyRecallFee) return Reject(action, "outbound-lease-recall-fee-unfunded");
            var outcome = simulation.Return(commandId + ":return", Links(contract), location);
            contract.ConditionAtReturn = conditionAtReturn; contract.ReturnLocation = location; action.Amount = contract.EarlyRecallFee;
            if (outcome == WorldOwnershipOutcome.Unknown) { contract.State = OutboundLeaseState.ReturnPending; contract.PendingOperationId = commandId; contract.Version++; return Pending(action, "outbound-lease-recall-pending"); }
            if (outcome == WorldOwnershipOutcome.NotApplied) return Reject(action, "outbound-lease-recall-refused");
            ApplyRecallFee(action, contract, wallet);
            return CommitReturn(action, contract, true);
        }
    }

    public OutboundLeaseActionRecord Reconcile(string commandId)
    {
        lock (gate)
        {
            RequireHost(); var action = state.OutboundLeaseActions.Single(x => x.CommandId == commandId); if (action.State != OutboundLeaseActionState.ReconcileRequired) return action;
            var contract = state.OutboundLeases.Single(x => x.ContractId == action.ContractId);
            if (contract.State == OutboundLeaseState.ActivationPending)
            {
                var result = simulation.InspectBegin(commandId + ":begin", Links(contract));
                if (result == WorldOwnershipOutcome.NotApplied) result = simulation.Begin(commandId + ":begin", Links(contract), contract.DeclaredDestination);
                return result == WorldOwnershipOutcome.Applied ? CommitActivation(action, contract) : Pending(action, "outbound-lease-begin-still-pending");
            }
            if (contract.State == OutboundLeaseState.ReturnPending)
            {
                var result = simulation.InspectReturn(commandId + ":return", Links(contract), contract.ReturnLocation);
                if (result == WorldOwnershipOutcome.NotApplied) result = simulation.Return(commandId + ":return", Links(contract), contract.ReturnLocation);
                if (result != WorldOwnershipOutcome.Applied) return Pending(action, "outbound-lease-return-still-pending");
                var recalled = action.Fingerprint.Contains("|recall|");
                if (recalled)
                {
                    var wallet = state.Economy.Wallets.Single(x => x.Account.Key == contract.Beneficiary.Key);
                    if (wallet.Balance < action.Amount) return Pending(action, "outbound-lease-recall-fee-unfunded");
                    ApplyRecallFee(action, contract, wallet);
                }
                return CommitReturn(action, contract, recalled);
            }
            return Reject(action, "outbound-lease-reconcile-state-conflict");
        }
    }

    public void CancelForCompanyLiquidation(string operationId, string companyId)
    {
        lock (gate)
        {
            RequireHost();
            foreach (var contract in state.OutboundLeases.Where(x => x.Owner.Kind == AssetOwnerKind.Company && x.Owner.OwnerId == companyId && !Terminal(x.State)).ToArray())
            {
                var outcome = simulation.Return(operationId + ":" + contract.ContractId, Links(contract), contract.ReturnLocation);
                if (outcome != WorldOwnershipOutcome.Applied) throw new InvalidOperationException("Outbound lease cancellation is pending: " + contract.ContractId);
                var synthetic = new OutboundLeaseActionRecord { CommandId = operationId + ":" + contract.ContractId, Fingerprint = "liquidation|" + companyId, ContractId = contract.ContractId };
                state.OutboundLeaseActions.Add(synthetic); CommitReturn(synthetic, contract, true);
            }
        }
    }

    private OutboundLeaseActionRecord CommitActivation(OutboundLeaseActionRecord action, OutboundLeaseContract contract)
    {
        contract.StartTick = state.LeaseClock.ActiveTick; contract.EndTick = checked(contract.StartTick + contract.DurationTicks); contract.NextDueTick = checked(contract.StartTick + contract.RentIntervalTicks); contract.State = OutboundLeaseState.Active; contract.PendingOperationId = null; contract.Version++;
        foreach (var id in contract.AssetIds) { var fleet = state.Fleet.Single(x => x.AssetId == id); fleet.Operator = null; fleet.OperationalState = FleetOperationalState.Reserved; fleet.LastKnownLocation = "off-scene:" + contract.DeclaredDestination; fleet.Version++; }
        return Success(action, "outbound-lease-active");
    }

    private OutboundLeaseActionRecord CommitReturn(OutboundLeaseActionRecord action, OutboundLeaseContract contract, bool cancelled)
    {
        foreach (var id in contract.AssetIds) { var fleet = state.Fleet.Single(x => x.AssetId == id); fleet.Operator = null; fleet.OperationalState = FleetOperationalState.Available; fleet.LastKnownLocation = contract.ReturnLocation; fleet.Version++; }
        contract.State = cancelled ? OutboundLeaseState.Cancelled : OutboundLeaseState.Returned; contract.PendingOperationId = null; contract.Version++;
        return Success(action, cancelled ? "outbound-lease-cancelled" : "outbound-lease-returned");
    }

    private void EnsureCompleteBundles(IReadOnlyCollection<string> ids)
    {
        foreach (var bundle in state.Assets.Bundles.Where(x => x.ComponentAssetIds.Any(ids.Contains)))
            if (bundle.ComponentAssetIds.Any(x => !ids.Contains(x))) throw new InvalidOperationException("A bundle component can only be leased through its complete bundle.");
    }
    private void ApplyRecallFee(OutboundLeaseActionRecord action, OutboundLeaseContract contract, Wallet wallet)
    {
        var entryId = action.CommandId + ":recall-fee"; if (state.Economy.Ledger.Any(x => x.EntryId == entryId)) return;
        if (action.Amount > 0) { wallet.Balance -= action.Amount; wallet.Version++; state.Economy.Ledger.Add(new LedgerEntry { EntryId = entryId, CommandId = action.CommandId, Kind = LedgerEntryKind.OutboundLeaseRecallFee, Debit = Clone(contract.Beneficiary), Amount = action.Amount, Detail = "outbound-lease-early-recall;contract=" + contract.ContractId }); }
    }
    private bool CanManage(PlayerEconomicState player, AssetOwnerRef owner)
    {
        if (owner.Kind == AssetOwnerKind.Player) return owner.OwnerId == player.PlayerId;
        if (owner.Kind != AssetOwnerKind.Company || player.CompanyId != owner.OwnerId) return false;
        var company = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == owner.OwnerId); return company != null && !company.Liquidating && (company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) && rights.Contains(CompanyPermission.ManageFleet)));
    }
    private bool RequesterControls(string requesterId, AssetOwnerRef owner) => CanManage(state.Economy.Players.Single(x => x.PlayerId == requesterId), owner);
    private IReadOnlyList<string> Links(OutboundLeaseContract contract) => contract.AssetIds.Select(id => state.Assets.Assets.Single(x => x.AssetId == id).GameLink.Value!).ToArray();
    private OutboundLeaseActionRecord? Known(string id, string fingerprint) { var r = state.OutboundLeaseActions.SingleOrDefault(x => x.CommandId == id); if (r != null && r.Fingerprint != fingerprint) throw new InvalidOperationException("Outbound lease command ID payload conflict."); return r; }
    private OutboundLeaseActionRecord Record(string id, string fingerprint, string contract, OutboundLeaseActionState actionState, string result, long amount = 0) { var r = new OutboundLeaseActionRecord { CommandId = id, Fingerprint = fingerprint, ContractId = contract, State = actionState, ResultCode = result, Amount = amount }; state.OutboundLeaseActions.Add(r); return r; }
    private static OutboundLeaseActionRecord Reject(OutboundLeaseActionRecord r, string code) { r.State = OutboundLeaseActionState.Rejected; r.ResultCode = code; return r; }
    private static OutboundLeaseActionRecord Pending(OutboundLeaseActionRecord r, string code) { r.State = OutboundLeaseActionState.ReconcileRequired; r.ResultCode = code; return r; }
    private static OutboundLeaseActionRecord Success(OutboundLeaseActionRecord r, string code) { r.State = OutboundLeaseActionState.Succeeded; r.ResultCode = code; return r; }
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
    private static bool Terminal(LeaseState x) => x == LeaseState.Returned || x == LeaseState.Purchased || x == LeaseState.Cancelled;
    private static bool Terminal(OutboundLeaseState x) => x == OutboundLeaseState.Returned || x == OutboundLeaseState.Cancelled;
    private static AssetOwnerRef Clone(AssetOwnerRef x) => new AssetOwnerRef { Kind = x.Kind, OwnerId = x.OwnerId };
    private static AccountRef Clone(AccountRef x) => new AccountRef { Kind = x.Kind, OwnerId = x.OwnerId };
}

public static class OutboundLeaseValidation
{
    public static void Validate(VehicleAcquisitionSnapshot state)
    {
        if (state.OutboundLeases.GroupBy(x => x.ContractId).Any(x => x.Count() != 1) || state.OutboundLeaseActions.GroupBy(x => x.CommandId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Duplicate outbound lease identity.");
        foreach (var c in state.OutboundLeases)
            if (string.IsNullOrWhiteSpace(c.ContractId) || c.AssetIds == null || c.AssetIds.Count == 0 || c.AssetIds.Distinct(StringComparer.Ordinal).Count() != c.AssetIds.Count || c.AssetIds.Any(id => !state.Fleet.Any(f => f.AssetId == id)) || c.Owner == null || c.Owner.Kind == AssetOwnerKind.Merchant || c.Beneficiary == null || c.RentAmount < 0 || c.RentIntervalTicks <= 0 || c.DurationTicks <= 0 || c.EarlyRecallFee < 0 || c.ConditionAtStart < 0m || c.ConditionAtStart > 1m || c.ConditionAtReturn < 0m || c.ConditionAtReturn > 1m) throw new InvalidOperationException("Invalid outbound lease contract.");
    }
}
