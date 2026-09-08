using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum LeaseState { Offered, Active, Delinquent, ReturnPending, Returned, PurchasePending, Purchased, Cancelled }
public enum LeaseActionState { Succeeded, Rejected, ReconcileRequired }

[DataContract]
public sealed class LeaseClock
{
    [DataMember(Name = "activeTick", Order = 1)] public long ActiveTick { get; set; }
    [DataMember(Name = "version", Order = 2)] public long Version { get; set; }
}

[DataContract]
public sealed class LeaseClockAdvance
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "activeGameplayTicks", Order = 2)] public long ActiveGameplayTicks { get; set; }
    [DataMember(Name = "sleepTicks", Order = 3)] public long SleepTicks { get; set; }
    [DataMember(Name = "fastTravelTicks", Order = 4)] public long FastTravelTicks { get; set; }
    [DataMember(Name = "sessionOpen", Order = 5)] public bool SessionOpen { get; set; }
    [DataMember(Name = "paused", Order = 6)] public bool Paused { get; set; }
}

[DataContract]
public sealed class LeaseInstallment
{
    [DataMember(Name = "dueTick", Order = 1)] public long DueTick { get; set; }
    [DataMember(Name = "amount", Order = 2)] public long Amount { get; set; }
    [DataMember(Name = "paid", Order = 3)] public bool Paid { get; set; }
    [DataMember(Name = "ledgerEntryId", Order = 4)] public string? LedgerEntryId { get; set; }
}

[DataContract]
public sealed class LeaseContract
{
    [DataMember(Name = "leaseId", Order = 1)] public string LeaseId { get; set; } = "";
    [DataMember(Name = "assetIds", Order = 2)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "lessor", Order = 3)] public AssetOwnerRef Lessor { get; set; } = AssetOwnerRef.Merchant("market");
    [DataMember(Name = "lessee", Order = 4)] public AssetOwnerRef? Lessee { get; set; }
    [DataMember(Name = "payer", Order = 5)] public AccountRef? Payer { get; set; }
    [DataMember(Name = "deposit", Order = 6)] public long Deposit { get; set; }
    [DataMember(Name = "heldDeposit", Order = 7)] public long HeldDeposit { get; set; }
    [DataMember(Name = "initialFee", Order = 8)] public long InitialFee { get; set; }
    [DataMember(Name = "rentAmount", Order = 9)] public long RentAmount { get; set; }
    [DataMember(Name = "rentIntervalTicks", Order = 10)] public long RentIntervalTicks { get; set; }
    [DataMember(Name = "durationTicks", Order = 11)] public long DurationTicks { get; set; }
    [DataMember(Name = "startTick", Order = 12)] public long StartTick { get; set; }
    [DataMember(Name = "endTick", Order = 13)] public long EndTick { get; set; }
    [DataMember(Name = "nextDueTick", Order = 14)] public long NextDueTick { get; set; }
    [DataMember(Name = "purchaseOptionPrice", Order = 15)] public long? PurchaseOptionPrice { get; set; }
    [DataMember(Name = "conditionAtStart", Order = 16)] public decimal ConditionAtStart { get; set; }
    [DataMember(Name = "conditionAtReturn", Order = 17)] public decimal? ConditionAtReturn { get; set; }
    [DataMember(Name = "maximumDamageCharge", Order = 18)] public long MaximumDamageCharge { get; set; }
    [DataMember(Name = "outstandingDebt", Order = 19)] public long OutstandingDebt { get; set; }
    [DataMember(Name = "state", Order = 20)] public LeaseState State { get; set; }
    [DataMember(Name = "installments", Order = 21)] public List<LeaseInstallment> Installments { get; set; } = new List<LeaseInstallment>();
    [DataMember(Name = "version", Order = 22)] public long Version { get; set; }
    [DataMember(Name = "pendingOperationId", Order = 23)] public string? PendingOperationId { get; set; }
}

[DataContract]
public sealed class LeaseActionRecord
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; } = "";
    [DataMember(Name = "leaseId", Order = 3)] public string LeaseId { get; set; } = "";
    [DataMember(Name = "state", Order = 4)] public LeaseActionState State { get; set; }
    [DataMember(Name = "resultCode", Order = 5)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "amount", Order = 6)] public long Amount { get; set; }
}

public sealed class LeaseEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IAssetReleaseGuard releaseGuard;
    private readonly IExistingVehicleOwnershipAdapter world;

    public LeaseEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IAssetReleaseGuard releaseGuard, IExistingVehicleOwnershipAdapter world)
    { this.state = state; this.authority = authority; this.releaseGuard = releaseGuard; this.world = world; VehicleAcquisitionPersistence.Validate(state); }

    public LeaseContract CreateOffer(string leaseId, IReadOnlyList<string> assetIds, long deposit, long initialFee, long rent, long interval,
        long duration, long? purchaseOption, decimal condition, long maximumDamageCharge)
    {
        lock (gate)
        {
            RequireHost();
            var known = state.Leases.SingleOrDefault(x => x.LeaseId == leaseId); if (known != null) return known;
            var ids = assetIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (string.IsNullOrWhiteSpace(leaseId) || ids.Count == 0 || deposit < 0 || initialFee < 0 || rent < 0 || interval <= 0 || duration <= 0 || purchaseOption < 0 || condition < 0m || condition > 1m || maximumDamageCharge < 0) throw new ArgumentException("Invalid lease terms.");
            foreach (var id in ids)
            {
                var owner = state.Ownership.Single(x => x.AssetId == id).Owner;
                if (owner.Kind != AssetOwnerKind.Merchant) throw new InvalidOperationException("Inbound lease offers require merchant-owned assets.");
                if (state.Leases.Any(x => x.AssetIds.Contains(id) && (x.State == LeaseState.Offered || x.State == LeaseState.Active || x.State == LeaseState.Delinquent || x.State == LeaseState.ReturnPending || x.State == LeaseState.PurchasePending))) throw new InvalidOperationException("Asset already belongs to an active lease workflow.");
                if (!state.Fleet.Any(x => x.AssetId == id)) throw new InvalidOperationException("Every leased asset must be in the fleet registry.");
            }
            var lease = new LeaseContract { LeaseId = leaseId, AssetIds = ids, Deposit = deposit, InitialFee = initialFee, RentAmount = rent,
                RentIntervalTicks = interval, DurationTicks = duration, PurchaseOptionPrice = purchaseOption, ConditionAtStart = condition,
                MaximumDamageCharge = maximumDamageCharge, State = LeaseState.Offered, Version = 1 };
            state.Leases.Add(lease); return lease;
        }
    }

    public LeaseActionRecord Accept(string commandId, string requesterId, string leaseId, AssetOwnerRef lessee, AccountRef payer, long expectedLeaseVersion, long expectedWalletVersion)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = string.Join("|", requesterId, leaseId, lessee.Key, payer.Key, expectedLeaseVersion, expectedWalletVersion);
            var known = Known(commandId, fingerprint); if (known != null) return known;
            var lease = state.Leases.Single(x => x.LeaseId == leaseId); var record = New(commandId, fingerprint, leaseId); state.LeaseActions.Add(record);
            var player = state.Economy.Players.SingleOrDefault(x => x.PlayerId == requesterId); var wallet = state.Economy.Wallets.SingleOrDefault(x => x.Account.Key == payer.Key);
            if (lease.State != LeaseState.Offered || lease.Version != expectedLeaseVersion) return Reject(record, "lease-unavailable-or-stale");
            if (player == null || !CanUse(player, lessee, payer) || wallet == null || wallet.Version != expectedWalletVersion) return Reject(record, "payer-or-lessee-refused");
            var due = checked(lease.Deposit + lease.InitialFee); if (wallet.Balance < due) return Reject(record, "insufficient-funds");
            wallet.Balance -= due; wallet.Version++; lease.HeldDeposit = lease.Deposit; lease.Lessee = Clone(lessee); lease.Payer = Clone(payer);
            lease.StartTick = state.LeaseClock.ActiveTick; lease.EndTick = checked(lease.StartTick + lease.DurationTicks); lease.NextDueTick = checked(lease.StartTick + lease.RentIntervalTicks); lease.State = LeaseState.Active; lease.Version++;
            AddLedger(commandId + ":deposit", commandId, LedgerEntryKind.LeaseDeposit, payer, lease.Deposit, "lease-deposit-held;lease=" + leaseId);
            AddLedger(commandId + ":fee", commandId, LedgerEntryKind.LeaseRent, payer, lease.InitialFee, "lease-initial-fee;lease=" + leaseId);
            foreach (var id in lease.AssetIds) { var fleet = state.Fleet.Single(x => x.AssetId == id); fleet.Operator = Clone(lessee); fleet.OperationalState = FleetOperationalState.Available; fleet.Version++; }
            return Success(record, "lease-accepted", due);
        }
    }

    public long Advance(LeaseClockAdvance advance)
    {
        lock (gate)
        {
            RequireHost();
            if (string.IsNullOrWhiteSpace(advance.CommandId) || advance.ActiveGameplayTicks < 0 || advance.SleepTicks < 0 || advance.FastTravelTicks < 0) throw new ArgumentException("Invalid lease clock advance.");
            var fingerprint = string.Join("|", "clock", advance.ActiveGameplayTicks, advance.SleepTicks, advance.FastTravelTicks, advance.SessionOpen, advance.Paused);
            var known = state.LeaseActions.SingleOrDefault(x => x.CommandId == advance.CommandId); if (known != null) { if (known.Fingerprint != fingerprint) throw new InvalidOperationException("Lease clock command ID payload conflict."); return state.LeaseClock.ActiveTick; }
            var delta = !advance.SessionOpen || advance.Paused ? 0 : checked(advance.ActiveGameplayTicks + advance.SleepTicks + advance.FastTravelTicks);
            state.LeaseClock.ActiveTick = checked(state.LeaseClock.ActiveTick + delta); state.LeaseClock.Version++;
            state.LeaseActions.Add(new LeaseActionRecord { CommandId = advance.CommandId, Fingerprint = fingerprint, LeaseId = "clock", State = LeaseActionState.Succeeded, ResultCode = "lease-clock-advanced", Amount = delta });
            foreach (var lease in state.Leases.Where(x => x.State == LeaseState.Active || x.State == LeaseState.Delinquent).ToArray()) ChargeDue(lease);
            return state.LeaseClock.ActiveTick;
        }
    }

    public LeaseActionRecord Return(string commandId, string requesterId, string leaseId, decimal conditionAtReturn)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + leaseId + "|" + conditionAtReturn; var known = Known(commandId, fingerprint); if (known != null) return known;
            var lease = state.Leases.Single(x => x.LeaseId == leaseId); var record = New(commandId, fingerprint, leaseId); state.LeaseActions.Add(record);
            if ((lease.State != LeaseState.Active && lease.State != LeaseState.Delinquent) || conditionAtReturn < 0m || conditionAtReturn > 1m || lease.Lessee == null || lease.Payer == null) return Reject(record, "lease-not-returnable");
            if (!RequesterControls(requesterId, lease.Lessee, false)) return Reject(record, "lessee-control-required");
            foreach (var id in lease.AssetIds) { var asset = state.Assets.Assets.Single(x => x.AssetId == id); var inspection = releaseGuard.Inspect(asset.GameLink.Value!); if (inspection.Status != AssetReleaseStatus.Releasable) return Reject(record, "asset-not-returnable:" + inspection.Status); }
            var damage = checked(decimal.ToInt64(decimal.Floor(Math.Max(0m, lease.ConditionAtStart - conditionAtReturn) * lease.MaximumDamageCharge)));
            var liabilities = checked(lease.OutstandingDebt + damage); var appliedDeposit = Math.Min(lease.HeldDeposit, liabilities); var refund = lease.HeldDeposit - appliedDeposit; lease.OutstandingDebt = liabilities - appliedDeposit; lease.HeldDeposit = 0; lease.ConditionAtReturn = conditionAtReturn;
            var wallet = state.Economy.Wallets.Single(x => x.Account.Key == lease.Payer.Key); if (refund > 0) { wallet.Balance += refund; wallet.Version++; AddLedger(commandId + ":deposit-refund", commandId, LedgerEntryKind.LeaseDepositRefund, null, refund, "lease-deposit-refund;lease=" + leaseId, wallet.Account); }
            if (appliedDeposit > 0) AddLedger(commandId + ":deposit-applied", commandId, LedgerEntryKind.LeaseDamage, lease.Payer, appliedDeposit, "lease-deposit-applied;lease=" + leaseId + ";damage=" + damage);
            foreach (var id in lease.AssetIds) { var fleet = state.Fleet.Single(x => x.AssetId == id); fleet.Operator = null; fleet.OperationalState = FleetOperationalState.Stored; fleet.Version++; }
            lease.State = LeaseState.Returned; lease.Version++; return Success(record, lease.OutstandingDebt > 0 ? "lease-returned-with-debt" : "lease-returned", refund);
        }
    }

    public LeaseActionRecord ExercisePurchaseOption(string commandId, string requesterId, string leaseId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + leaseId + "|purchase"; var known = Known(commandId, fingerprint); if (known != null) return known;
            var lease = state.Leases.Single(x => x.LeaseId == leaseId); var record = New(commandId, fingerprint, leaseId); state.LeaseActions.Add(record);
            if ((lease.State != LeaseState.Active && lease.State != LeaseState.Delinquent) || lease.PurchaseOptionPrice == null || lease.Lessee == null || lease.Payer == null) return Reject(record, "purchase-option-unavailable");
            if (!RequesterControls(requesterId, lease.Lessee, true)) return Reject(record, "lessee-control-required");
            var total = checked(lease.PurchaseOptionPrice.Value + lease.OutstandingDebt); var depositApplied = Math.Min(lease.HeldDeposit, total); var debit = total - depositApplied;
            var wallet = state.Economy.Wallets.Single(x => x.Account.Key == lease.Payer.Key); if (wallet.Balance < debit) return Reject(record, "insufficient-funds");
            foreach (var id in lease.AssetIds) { var asset = state.Assets.Assets.Single(x => x.AssetId == id); if (releaseGuard.Inspect(asset.GameLink.Value!).Status != AssetReleaseStatus.Releasable) return Reject(record, "purchase-asset-not-releasable"); }
            wallet.Balance -= debit; wallet.Version++; record.Amount = debit; lease.State = LeaseState.PurchasePending; lease.PendingOperationId = commandId; lease.Version++;
            var allApplied = true;
            foreach (var id in lease.AssetIds) { var asset = state.Assets.Assets.Single(x => x.AssetId == id); if (world.ApplyOwner(commandId + ":purchase:" + id, asset.GameLink.Value!, lease.Lessee) != WorldOwnershipOutcome.Applied) allApplied = false; }
            return allApplied ? CommitPurchase(record, lease) : Pending(record, "lease-purchase-world-pending");
        }
    }

    public LeaseActionRecord ReconcilePurchase(string commandId)
    {
        lock (gate)
        {
            RequireHost(); var record = state.LeaseActions.Single(x => x.CommandId == commandId); if (record.State != LeaseActionState.ReconcileRequired) return record;
            var lease = state.Leases.Single(x => x.LeaseId == record.LeaseId); if (lease.State != LeaseState.PurchasePending || lease.Lessee == null) return Reject(record, "lease-purchase-state-conflict");
            var allApplied = true;
            foreach (var id in lease.AssetIds)
            {
                var asset = state.Assets.Assets.Single(x => x.AssetId == id); var result = world.InspectOwner(asset.GameLink.Value!, lease.Lessee);
                if (result != WorldOwnershipOutcome.Applied) result = world.ApplyOwner(commandId + ":purchase:" + id, asset.GameLink.Value!, lease.Lessee);
                if (result != WorldOwnershipOutcome.Applied) allApplied = false;
            }
            return allApplied ? CommitPurchase(record, lease) : Pending(record, "lease-purchase-world-still-pending");
        }
    }

    private LeaseActionRecord CommitPurchase(LeaseActionRecord record, LeaseContract lease)
    {
        var total = checked(lease.PurchaseOptionPrice!.Value + lease.OutstandingDebt); var depositApplied = Math.Min(lease.HeldDeposit, total);
        lease.HeldDeposit -= depositApplied; lease.OutstandingDebt = 0;
        foreach (var id in lease.AssetIds) { var owner = state.Ownership.Single(x => x.AssetId == id); if (owner.Owner.Key != lease.Lessee!.Key) { owner.Owner = Clone(lease.Lessee); owner.Version++; } }
        lease.State = LeaseState.Purchased; lease.PendingOperationId = null; lease.Version++;
        AddLedger(record.CommandId + ":purchase", record.CommandId, LedgerEntryKind.LeasePurchase, lease.Payer, total, "lease-purchase;lease=" + lease.LeaseId + ";depositApplied=" + depositApplied);
        return Success(record, "lease-purchased", record.Amount);
    }

    private void ChargeDue(LeaseContract lease)
    {
        while (lease.NextDueTick <= state.LeaseClock.ActiveTick && lease.NextDueTick <= lease.EndTick)
        {
            var dueTick = lease.NextDueTick; var installment = lease.Installments.SingleOrDefault(x => x.DueTick == dueTick);
            if (installment == null) { installment = new LeaseInstallment { DueTick = dueTick, Amount = lease.RentAmount }; lease.Installments.Add(installment); }
            if (!installment.Paid)
            {
                var wallet = state.Economy.Wallets.Single(x => x.Account.Key == lease.Payer!.Key);
                if (wallet.Balance >= lease.RentAmount) { wallet.Balance -= lease.RentAmount; wallet.Version++; installment.Paid = true; installment.LedgerEntryId = lease.LeaseId + ":rent:" + dueTick; AddLedger(installment.LedgerEntryId, installment.LedgerEntryId, LedgerEntryKind.LeaseRent, lease.Payer, lease.RentAmount, "lease-rent;lease=" + lease.LeaseId + ";due=" + dueTick); }
                else { lease.OutstandingDebt = checked(lease.OutstandingDebt + lease.RentAmount); lease.State = LeaseState.Delinquent; }
            }
            lease.NextDueTick = checked(lease.NextDueTick + lease.RentIntervalTicks); lease.Version++;
        }
    }

    private bool CanUse(PlayerEconomicState player, AssetOwnerRef lessee, AccountRef payer)
    {
        if (lessee.Key != (payer.Kind == AccountKind.Player ? "Player:" + payer.OwnerId : "Company:" + payer.OwnerId)) return false;
        if (payer.Kind == AccountKind.Player) return payer.OwnerId == player.PlayerId;
        var company = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == payer.OwnerId); return company != null && player.CompanyId == company.CompanyId && (company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) && rights.Contains(CompanyPermission.ManageFunds) && rights.Contains(CompanyPermission.ManageFleet)));
    }
    private bool RequesterControls(string requesterId, AssetOwnerRef lessee, bool funds)
    {
        var player = state.Economy.Players.Single(x => x.PlayerId == requesterId); if (lessee.Kind == AssetOwnerKind.Player) return lessee.OwnerId == requesterId;
        var company = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == lessee.OwnerId); if (company == null || player.CompanyId != company.CompanyId) return false;
        if (company.LeaderId == requesterId) return true;
        return company.DelegatedPermissions.TryGetValue(requesterId, out var rights) && rights.Contains(CompanyPermission.ManageFleet) && (!funds || rights.Contains(CompanyPermission.ManageFunds));
    }
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
    private LeaseActionRecord? Known(string id, string fingerprint) { var r = state.LeaseActions.SingleOrDefault(x => x.CommandId == id); if (r != null && r.Fingerprint != fingerprint) throw new InvalidOperationException("Lease command ID payload conflict."); return r; }
    private static LeaseActionRecord New(string id, string fingerprint, string lease) => new LeaseActionRecord { CommandId = id, Fingerprint = fingerprint, LeaseId = lease };
    private static LeaseActionRecord Reject(LeaseActionRecord r, string code) { r.State = LeaseActionState.Rejected; r.ResultCode = code; return r; }
    private static LeaseActionRecord Pending(LeaseActionRecord r, string code) { r.State = LeaseActionState.ReconcileRequired; r.ResultCode = code; return r; }
    private static LeaseActionRecord Success(LeaseActionRecord r, string code, long amount) { r.State = LeaseActionState.Succeeded; r.ResultCode = code; r.Amount = amount; return r; }
    private void AddLedger(string id, string command, LedgerEntryKind kind, AccountRef? debit, long amount, string detail, AccountRef? credit = null) { if (amount <= 0 || state.Economy.Ledger.Any(x => x.EntryId == id)) return; state.Economy.Ledger.Add(new LedgerEntry { EntryId = id, CommandId = command, Kind = kind, Debit = debit == null ? null : Clone(debit), Credit = credit == null ? null : Clone(credit), Amount = amount, Detail = detail }); }
    private static AssetOwnerRef Clone(AssetOwnerRef x) => new AssetOwnerRef { Kind = x.Kind, OwnerId = x.OwnerId };
    private static AccountRef Clone(AccountRef x) => new AccountRef { Kind = x.Kind, OwnerId = x.OwnerId };
}

public static class LeaseValidation
{
    public static void Validate(VehicleAcquisitionSnapshot state)
    {
        if (state.LeaseClock == null || state.LeaseClock.ActiveTick < 0 || state.Leases.GroupBy(x => x.LeaseId).Any(x => x.Count() != 1) || state.LeaseActions.GroupBy(x => x.CommandId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Invalid lease state.");
        foreach (var lease in state.Leases) if (string.IsNullOrWhiteSpace(lease.LeaseId) || lease.AssetIds.Count == 0 || lease.AssetIds.Distinct(StringComparer.Ordinal).Count() != lease.AssetIds.Count || lease.AssetIds.Any(id => !state.Assets.Assets.Any(a => a.AssetId == id)) || lease.Deposit < 0 || lease.HeldDeposit < 0 || lease.InitialFee < 0 || lease.RentAmount < 0 || lease.RentIntervalTicks <= 0 || lease.DurationTicks <= 0 || lease.ConditionAtStart < 0m || lease.ConditionAtStart > 1m || lease.ConditionAtReturn < 0m || lease.ConditionAtReturn > 1m || lease.MaximumDamageCharge < 0 || lease.OutstandingDebt < 0) throw new InvalidOperationException("Invalid lease contract.");
    }
}
