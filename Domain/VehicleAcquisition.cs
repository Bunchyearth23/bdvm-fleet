using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BDVM.Domain;

public enum AssetOwnerKind { Merchant, Player, Company }
public enum AcquisitionState { Reserved, Debited, OwnershipPending, Succeeded, Compensated, ReconcileRequired, Rejected }
public enum OfferState { Available, Reserved, Sold }
public enum WorldOwnershipOutcome { Applied, NotApplied, Unknown }
public enum AcquisitionFailurePoint { None, AfterReservation, AfterDebit, BeforeWorldTransfer, AfterWorldTransfer }

[DataContract]
public sealed class AssetOwnerRef
{
    [DataMember(Name = "kind", Order = 1)] public AssetOwnerKind Kind { get; set; }
    [DataMember(Name = "ownerId", Order = 2)] public string OwnerId { get; set; } = "";
    public string Key => Kind + ":" + OwnerId;
    public static AssetOwnerRef Merchant(string id) => new AssetOwnerRef { Kind = AssetOwnerKind.Merchant, OwnerId = id };
    public static AssetOwnerRef Player(string id) => new AssetOwnerRef { Kind = AssetOwnerKind.Player, OwnerId = id };
    public static AssetOwnerRef Company(string id) => new AssetOwnerRef { Kind = AssetOwnerKind.Company, OwnerId = id };
}

[DataContract]
public sealed class AssetOwnership
{
    [DataMember(Name = "assetId", Order = 1)] public string AssetId { get; set; } = "";
    [DataMember(Name = "owner", Order = 2)] public AssetOwnerRef Owner { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "version", Order = 3)] public long Version { get; set; }
}

[DataContract]
public sealed class VehicleOffer
{
    [DataMember(Name = "offerId", Order = 1)] public string OfferId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 2)] public string AssetId { get; set; } = "";
    [DataMember(Name = "price", Order = 3)] public long Price { get; set; }
    [DataMember(Name = "referenceValue", Order = 4)] public long ReferenceValue { get; set; }
    [DataMember(Name = "referenceSource", Order = 5)] public ReferenceValueSource ReferenceSource { get; set; }
    [DataMember(Name = "observedCondition", Order = 6)] public decimal ObservedCondition { get; set; }
    [DataMember(Name = "appliedRate", Order = 7)] public decimal AppliedRate { get; set; }
    [DataMember(Name = "version", Order = 8)] public long Version { get; set; }
    [DataMember(Name = "state", Order = 9)] public OfferState State { get; set; } = OfferState.Available;
    [DataMember(Name = "reservedBy", Order = 10)] public string? ReservedByCommandId { get; set; }
}

[DataContract]
public sealed class AcquireVehicleCommand
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 2)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "offerId", Order = 3)] public string OfferId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 4)] public string AssetId { get; set; } = "";
    [DataMember(Name = "buyer", Order = 5)] public AssetOwnerRef Buyer { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "payer", Order = 6)] public AccountRef Payer { get; set; } = new AccountRef();
    [DataMember(Name = "expectedVersions", Order = 7)] public Dictionary<string, long> ExpectedVersions { get; set; } = new Dictionary<string, long>();
}

[DataContract]
public sealed class AcquisitionRecord
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 2)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "offerId", Order = 3)] public string OfferId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 4)] public string AssetId { get; set; } = "";
    [DataMember(Name = "buyer", Order = 5)] public AssetOwnerRef Buyer { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "payer", Order = 6)] public AccountRef Payer { get; set; } = new AccountRef();
    [DataMember(Name = "state", Order = 7)] public AcquisitionState State { get; set; }
    [DataMember(Name = "resultCode", Order = 8)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "debitOperationId", Order = 9)] public string DebitOperationId { get; set; } = "";
    [DataMember(Name = "worldOperationId", Order = 10)] public string WorldOperationId { get; set; } = "";
    [DataMember(Name = "compensationOperationId", Order = 11)] public string CompensationOperationId { get; set; } = "";
    [DataMember(Name = "price", Order = 12)] public long Price { get; set; }
    [DataMember(Name = "referenceValue", Order = 13)] public long ReferenceValue { get; set; }
    [DataMember(Name = "referenceSource", Order = 14)] public ReferenceValueSource ReferenceSource { get; set; }
    [DataMember(Name = "observedCondition", Order = 15)] public decimal ObservedCondition { get; set; }
    [DataMember(Name = "appliedRate", Order = 16)] public decimal AppliedRate { get; set; }
    [DataMember(Name = "detail", Order = 17)] public string Detail { get; set; } = "";
}

[DataContract]
public sealed class VehicleAcquisitionSnapshot
{
    public const string CurrentSchema = "bdvm.vehicle-acquisition";
    public const int CurrentVersion = 19;
    [DataMember(Name = "schema", Order = 1)] public string Schema { get; set; } = CurrentSchema;
    [DataMember(Name = "schemaVersion", Order = 2)] public int SchemaVersion { get; set; } = CurrentVersion;
    [DataMember(Name = "checkpointId", Order = 3)] public string CheckpointId { get; set; } = "";
    [DataMember(Name = "economy", Order = 4)] public CompanyEconomySnapshot Economy { get; set; } = new CompanyEconomySnapshot();
    [DataMember(Name = "assets", Order = 5)] public AssetRegistrySnapshot Assets { get; set; } = new AssetRegistrySnapshot();
    [DataMember(Name = "ownership", Order = 6)] public List<AssetOwnership> Ownership { get; set; } = new List<AssetOwnership>();
    [DataMember(Name = "offers", Order = 7)] public List<VehicleOffer> Offers { get; set; } = new List<VehicleOffer>();
    [DataMember(Name = "acquisitions", Order = 8)] public List<AcquisitionRecord> Acquisitions { get; set; } = new List<AcquisitionRecord>();
    [DataMember(Name = "fleet", Order = 9)] public List<FleetAssetState> Fleet { get; set; } = new List<FleetAssetState>();
    [DataMember(Name = "fleetCommands", Order = 10)] public List<FleetCommandRecord> FleetCommands { get; set; } = new List<FleetCommandRecord>();
    [DataMember(Name = "resaleQuotes", Order = 11)] public List<VehicleResaleQuote> ResaleQuotes { get; set; } = new List<VehicleResaleQuote>();
    [DataMember(Name = "resales", Order = 12)] public List<VehicleResaleRecord> Resales { get; set; } = new List<VehicleResaleRecord>();
    [DataMember(Name = "companyLiquidations", Order = 13)] public List<CompanyLiquidationRecord> CompanyLiquidations { get; set; } = new List<CompanyLiquidationRecord>();
    [DataMember(Name = "operatingCosts", Order = 14)] public List<OperatingCostRecord> OperatingCosts { get; set; } = new List<OperatingCostRecord>();
    [DataMember(Name = "market", Order = 15)] public FiniteMarketState Market { get; set; } = new FiniteMarketState();
    [DataMember(Name = "leaseClock", Order = 16)] public LeaseClock LeaseClock { get; set; } = new LeaseClock();
    [DataMember(Name = "leases", Order = 17)] public List<LeaseContract> Leases { get; set; } = new List<LeaseContract>();
    [DataMember(Name = "leaseActions", Order = 18)] public List<LeaseActionRecord> LeaseActions { get; set; } = new List<LeaseActionRecord>();
    [DataMember(Name = "assignments", Order = 19)] public List<MissionAssignment> Assignments { get; set; } = new List<MissionAssignment>();
    [DataMember(Name = "assignmentCommands", Order = 20)] public List<MissionAssignmentCommand> AssignmentCommands { get; set; } = new List<MissionAssignmentCommand>();
    [DataMember(Name = "industrialStocks", Order = 21)] public List<IndustrialStock> IndustrialStocks { get; set; } = new List<IndustrialStock>();
    [DataMember(Name = "industrialRecipes", Order = 22)] public List<IndustrialRecipe> IndustrialRecipes { get; set; } = new List<IndustrialRecipe>();
    [DataMember(Name = "industrialContracts", Order = 23)] public List<IndustrialContract> IndustrialContracts { get; set; } = new List<IndustrialContract>();
    [DataMember(Name = "industrialCommands", Order = 24)] public List<MissionAssignmentCommand> IndustrialCommands { get; set; } = new List<MissionAssignmentCommand>();
    [DataMember(Name = "outboundLeases", Order = 25)] public List<OutboundLeaseContract> OutboundLeases { get; set; } = new List<OutboundLeaseContract>();
    [DataMember(Name = "outboundLeaseActions", Order = 26)] public List<OutboundLeaseActionRecord> OutboundLeaseActions { get; set; } = new List<OutboundLeaseActionRecord>();
    [DataMember(Name = "passengerRoutes", Order = 27)] public List<PassengerRouteDemand> PassengerRoutes { get; set; } = new List<PassengerRouteDemand>();
    [DataMember(Name = "passengerContracts", Order = 28)] public List<PassengerServiceContract> PassengerContracts { get; set; } = new List<PassengerServiceContract>();
    [DataMember(Name = "passengerCommands", Order = 29)] public List<MissionAssignmentCommand> PassengerCommands { get; set; } = new List<MissionAssignmentCommand>();
    [DataMember(Name = "dynamicEconomy", Order = 30)] public DynamicEconomyState DynamicEconomy { get; set; } = new DynamicEconomyState();
    [DataMember(Name = "dedicatedAuthority", Order = 31)] public DedicatedAuthorityState DedicatedAuthority { get; set; } = new DedicatedAuthorityState();
    [DataMember(Name = "assetLifecycle", Order = 32)] public AssetLifecycleState AssetLifecycle { get; set; } = new AssetLifecycleState();
    [DataMember(Name = "financing", Order = 33)] public FinancingStateStore Financing { get; set; } = new FinancingStateStore();
    [DataMember(Name = "triageAssistance", Order = 34)] public TriageAssistanceState TriageAssistance { get; set; } = new TriageAssistanceState();
    [DataMember(Name = "initialDeliveries", Order = 35)] public List<InitialDeliveryGrant> InitialDeliveries { get; set; } = new List<InitialDeliveryGrant>();
}

public interface IExistingVehicleOwnershipAdapter
{
    WorldOwnershipOutcome ApplyOwner(string operationId, string persistentCarGuid, AssetOwnerRef owner);
    WorldOwnershipOutcome InspectOwner(string persistentCarGuid, AssetOwnerRef owner);
}

public interface IAcquisitionCheckpointSink
{
    void Save(string checkpointId, VehicleAcquisitionSnapshot snapshot, AcquisitionState stage);
}

public sealed class NullAcquisitionCheckpointSink : IAcquisitionCheckpointSink
{
    public void Save(string checkpointId, VehicleAcquisitionSnapshot snapshot, AcquisitionState stage) { }
}

public sealed class VehicleAcquisitionEngine
{
    private readonly object gate = new object();
    private readonly IExistingVehicleOwnershipAdapter world;
    private readonly IAcquisitionCheckpointSink checkpoints;
    private readonly INetworkRoleDetector authority;
    public VehicleAcquisitionSnapshot State { get; }
    public AcquisitionFailurePoint FailurePoint { get; set; }

    public VehicleAcquisitionEngine(VehicleAcquisitionSnapshot state, IExistingVehicleOwnershipAdapter world, INetworkRoleDetector authority, IAcquisitionCheckpointSink? checkpoints = null)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.checkpoints = checkpoints ?? new NullAcquisitionCheckpointSink();
        VehicleAcquisitionPersistence.Validate(state);
    }

    public IReadOnlyList<VehicleOffer> SelectableOffers()
    {
        lock (gate)
            return State.Offers.Where(o => o.State == OfferState.Available && IsResolved(o.AssetId) && MerchantOwns(o.AssetId)).Select(CloneOffer).ToArray();
    }

    public AcquisitionRecord Acquire(AcquireVehicleCommand command)
    {
        lock (gate)
        {
            Require(command);
            var known = State.Acquisitions.SingleOrDefault(x => x.CommandId == command.CommandId);
            if (known != null) return known;
            var rejected = Validate(command);
            if (rejected != null) return RecordRejected(command, rejected);

            var offer = State.Offers.Single(x => x.OfferId == command.OfferId);
            var record = NewRecord(command, offer);
            State.Acquisitions.Add(record);
            offer.State = OfferState.Reserved; offer.ReservedByCommandId = command.CommandId; offer.Version++;
            Persist(record, AcquisitionState.Reserved);
            if (Fail(AcquisitionFailurePoint.AfterReservation, record)) return record;

            var wallet = Wallet(command.Payer);
            wallet.Balance -= offer.Price; wallet.Version++;
            record.State = AcquisitionState.Debited; record.DebitOperationId = command.CommandId + ":debit";
            AddPurchaseLedger(record, wallet.Account, offer.Price);
            Persist(record, AcquisitionState.Debited);
            if (Fail(AcquisitionFailurePoint.AfterDebit, record) || Fail(AcquisitionFailurePoint.BeforeWorldTransfer, record)) return record;

            return ApplyWorld(record);
        }
    }

    public AcquisitionRecord Reconcile(string commandId)
    {
        lock (gate)
        {
            var record = State.Acquisitions.Single(x => x.CommandId == commandId);
            if (record.State == AcquisitionState.Succeeded || record.State == AcquisitionState.Compensated || record.State == AcquisitionState.Rejected) return record;
            if (record.State == AcquisitionState.Reserved)
            {
                ReleaseOffer(record); record.State = AcquisitionState.Compensated; record.ResultCode = "reservation-released"; Persist(record, record.State); return record;
            }
            var asset = Asset(record.AssetId);
            var observed = world.InspectOwner(asset.GameLink.Value!, record.Buyer);
            if (observed == WorldOwnershipOutcome.Applied) return CommitOwnership(record);
            if (observed == WorldOwnershipOutcome.NotApplied) return Compensate(record, "world-owner-not-applied");
            record.State = AcquisitionState.ReconcileRequired; record.ResultCode = "world-owner-unknown"; Persist(record, record.State); return record;
        }
    }

    public string Diagnostic()
    {
        lock (gate)
        {
            var lines = new List<string> { "checkpoint=" + State.CheckpointId, "selectable=" + SelectableOffers().Count };
            lines.AddRange(State.Acquisitions.OrderBy(x => x.CommandId).Select(x => x.CommandId + " offer=" + x.OfferId + " asset=" + x.AssetId + " buyer=" + x.Buyer.Key + " payer=" + x.Payer.Key + " state=" + x.State + " result=" + x.ResultCode + " reference=" + x.ReferenceValue + " source=" + x.ReferenceSource + " condition=" + x.ObservedCondition + " rate=" + x.AppliedRate));
            return string.Join(Environment.NewLine, lines);
        }
    }

    private AcquisitionRecord ApplyWorld(AcquisitionRecord record)
    {
        record.State = AcquisitionState.OwnershipPending; record.WorldOperationId = record.CommandId + ":world-owner"; Persist(record, record.State);
        WorldOwnershipOutcome outcome;
        try { outcome = world.ApplyOwner(record.WorldOperationId, Asset(record.AssetId).GameLink.Value!, record.Buyer); }
        catch (Exception ex) { record.State = AcquisitionState.ReconcileRequired; record.ResultCode = "world-adapter-exception"; record.Detail = ex.GetType().Name + ":" + ex.Message; PersistBestEffort(record); return record; }
        if (outcome == WorldOwnershipOutcome.NotApplied) return Compensate(record, "world-owner-not-applied");
        if (outcome == WorldOwnershipOutcome.Unknown) { record.State = AcquisitionState.ReconcileRequired; record.ResultCode = "world-owner-unknown"; Persist(record, record.State); return record; }
        if (Fail(AcquisitionFailurePoint.AfterWorldTransfer, record)) return record;
        return CommitOwnership(record);
    }

    private AcquisitionRecord CommitOwnership(AcquisitionRecord record)
    {
        var owner = State.Ownership.Single(x => x.AssetId == record.AssetId); owner.Owner = record.Buyer; owner.Version++;
        var offer = State.Offers.Single(x => x.OfferId == record.OfferId); offer.State = OfferState.Sold; offer.ReservedByCommandId = record.CommandId; offer.Version++;
        record.State = AcquisitionState.Succeeded; record.ResultCode = "acquired"; Persist(record, record.State); return record;
    }

    private AcquisitionRecord Compensate(AcquisitionRecord record, string reason)
    {
        if (!string.IsNullOrEmpty(record.DebitOperationId) && string.IsNullOrEmpty(record.CompensationOperationId))
        {
            var wallet = Wallet(record.Payer); wallet.Balance += record.Price; wallet.Version++;
            record.CompensationOperationId = record.CommandId + ":compensation";
            State.Economy.Ledger.Add(new LedgerEntry { EntryId = record.CompensationOperationId, CommandId = record.CommandId, Kind = LedgerEntryKind.Reimbursement, Credit = wallet.Account, Amount = record.Price, Detail = reason });
        }
        ReleaseOffer(record); record.State = AcquisitionState.Compensated; record.ResultCode = reason + ":compensated"; Persist(record, record.State); return record;
    }

    private bool Fail(AcquisitionFailurePoint point, AcquisitionRecord record)
    {
        if (FailurePoint != point) return false;
        FailurePoint = AcquisitionFailurePoint.None; record.State = AcquisitionState.ReconcileRequired; record.ResultCode = "injected-failure:" + point; PersistBestEffort(record); return true;
    }

    private string? Validate(AcquireVehicleCommand c)
    {
        if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return "host-authority-required";
        var player = State.Economy.Players.SingleOrDefault(x => x.PlayerId == c.RequesterId); if (player == null) return "unknown-requester";
        var offer = State.Offers.SingleOrDefault(x => x.OfferId == c.OfferId); if (offer == null || offer.AssetId != c.AssetId) return "selection-mismatch";
        if (offer.State != OfferState.Available || offer.ReservedByCommandId != null) return "offer-unavailable";
        var asset = State.Assets.Assets.SingleOrDefault(x => x.AssetId == c.AssetId); if (asset == null || asset.GameLink.State != PersistentLinkState.Resolved) return "asset-not-resolved";
        if (!MerchantOwns(c.AssetId)) return "asset-not-merchant-owned";
        if (c.Buyer.Kind == AssetOwnerKind.Player) { if (c.Buyer.OwnerId != c.RequesterId || c.Payer.Kind != AccountKind.Player || c.Payer.OwnerId != c.RequesterId) return "invalid-player-buyer-or-payer"; }
        else if (c.Buyer.Kind == AssetOwnerKind.Company)
        {
            if (c.Payer.Kind != AccountKind.Company || c.Payer.OwnerId != c.Buyer.OwnerId || player.CompanyId != c.Buyer.OwnerId) return "invalid-company-relation";
            var company = State.Economy.Companies.SingleOrDefault(x => x.CompanyId == c.Buyer.OwnerId); if (company == null || company.Liquidating || !HasFundsPermission(company, c.RequesterId)) return "company-permission-denied";
            if (!Version(c, "company:" + company.CompanyId, company.Version)) return "company-version-mismatch";
        }
        else return "invalid-buyer-kind";
        var wallet = State.Economy.Wallets.SingleOrDefault(x => x.Account.Key == c.Payer.Key); if (wallet == null) return "payer-not-found";
        var ownership = State.Ownership.Single(x => x.AssetId == c.AssetId);
        if (!Version(c, "player:" + player.PlayerId, player.Version) || !Version(c, wallet.Account.Key, wallet.Version) || !Version(c, "offer:" + offer.OfferId, offer.Version) || !Version(c, "asset:" + ownership.AssetId, ownership.Version)) return "version-mismatch";
        if (wallet.Balance < offer.Price) return "insufficient-funds";
        return null;
    }

    private AcquisitionRecord RecordRejected(AcquireVehicleCommand c, string code) { var r = new AcquisitionRecord { CommandId = c.CommandId, RequesterId = c.RequesterId, OfferId = c.OfferId, AssetId = c.AssetId, Buyer = c.Buyer, Payer = c.Payer, State = AcquisitionState.Rejected, ResultCode = code }; State.Acquisitions.Add(r); Persist(r, r.State); return r; }
    private static AcquisitionRecord NewRecord(AcquireVehicleCommand c, VehicleOffer o) => new AcquisitionRecord { CommandId = c.CommandId, RequesterId = c.RequesterId, OfferId = c.OfferId, AssetId = c.AssetId, Buyer = c.Buyer, Payer = c.Payer, State = AcquisitionState.Reserved, Price = o.Price, ReferenceValue = o.ReferenceValue, ReferenceSource = o.ReferenceSource, ObservedCondition = o.ObservedCondition, AppliedRate = o.AppliedRate };
    private void AddPurchaseLedger(AcquisitionRecord r, AccountRef payer, long amount) { if (!State.Economy.Ledger.Any(x => x.EntryId == r.DebitOperationId)) State.Economy.Ledger.Add(new LedgerEntry { EntryId = r.DebitOperationId, CommandId = r.CommandId, Kind = LedgerEntryKind.VehiclePurchase, Debit = payer, Amount = amount, Detail = "offer=" + r.OfferId + ";asset=" + r.AssetId + ";reference=" + r.ReferenceValue + ";source=" + r.ReferenceSource + ";condition=" + r.ObservedCondition + ";rate=" + r.AppliedRate }); }
    private void ReleaseOffer(AcquisitionRecord r) { var o = State.Offers.Single(x => x.OfferId == r.OfferId); if (o.ReservedByCommandId == r.CommandId) { o.State = OfferState.Available; o.ReservedByCommandId = null; o.Version++; } }
    private void Persist(AcquisitionRecord r, AcquisitionState stage) { try { checkpoints.Save(State.CheckpointId, State, stage); } catch (Exception ex) { r.State = AcquisitionState.ReconcileRequired; r.ResultCode = "checkpoint-failure"; r.Detail = ex.GetType().Name + ":" + ex.Message; throw; } }
    private void PersistBestEffort(AcquisitionRecord r) { try { checkpoints.Save(State.CheckpointId, State, r.State); } catch { } }
    private bool IsResolved(string id) { var a = State.Assets.Assets.SingleOrDefault(x => x.AssetId == id); return a != null && a.GameLink.State == PersistentLinkState.Resolved; }
    private bool MerchantOwns(string id) { var o = State.Ownership.SingleOrDefault(x => x.AssetId == id); return o != null && o.Owner.Kind == AssetOwnerKind.Merchant; }
    private FleetAsset Asset(string id) => State.Assets.Assets.Single(x => x.AssetId == id);
    private Wallet Wallet(AccountRef account) => State.Economy.Wallets.Single(x => x.Account.Key == account.Key);
    private static bool HasFundsPermission(CompanyState c, string player) => c.LeaderId == player || (c.DelegatedPermissions.TryGetValue(player, out var rights) && rights.Contains(CompanyPermission.ManageFunds));
    private static bool Version(AcquireVehicleCommand c, string key, long actual) => c.ExpectedVersions.TryGetValue(key, out var expected) && expected == actual;
    private static VehicleOffer CloneOffer(VehicleOffer o) => new VehicleOffer { OfferId = o.OfferId, AssetId = o.AssetId, Price = o.Price, ReferenceValue = o.ReferenceValue, ReferenceSource = o.ReferenceSource, ObservedCondition = o.ObservedCondition, AppliedRate = o.AppliedRate, Version = o.Version, State = o.State, ReservedByCommandId = o.ReservedByCommandId };
    private static void Require(AcquireVehicleCommand c) { if (c == null || string.IsNullOrWhiteSpace(c.CommandId) || string.IsNullOrWhiteSpace(c.RequesterId) || string.IsNullOrWhiteSpace(c.OfferId) || string.IsNullOrWhiteSpace(c.AssetId) || c.Buyer == null || c.Payer == null) throw new ArgumentException("A complete explicit acquisition command is required."); }
}

public static class VehicleAcquisitionPersistence
{
    private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(VehicleAcquisitionSnapshot));
    public static string Serialize(VehicleAcquisitionSnapshot snapshot) { Validate(snapshot); using (var s = new MemoryStream()) { Serializer.WriteObject(s, snapshot); return Encoding.UTF8.GetString(s.ToArray()); } }
    public static VehicleAcquisitionSnapshot Deserialize(string json, string checkpointId) { using (var s = new MemoryStream(Encoding.UTF8.GetBytes(json ?? ""))) { var v = Serializer.ReadObject(s) as VehicleAcquisitionSnapshot ?? throw new InvalidDataException("Missing acquisition snapshot."); Migrate(v); Validate(v); if (v.CheckpointId != checkpointId) throw new InvalidDataException("Checkpoint mismatch; cross-save acquisition state is forbidden."); return v; } }
    public static void Validate(VehicleAcquisitionSnapshot s)
    {
        if (s == null || s.Schema != VehicleAcquisitionSnapshot.CurrentSchema || s.SchemaVersion != VehicleAcquisitionSnapshot.CurrentVersion || string.IsNullOrWhiteSpace(s.CheckpointId) || s.Economy.CheckpointId != s.CheckpointId) throw new InvalidDataException("Unsupported or unscoped acquisition snapshot.");
        s.Fleet = s.Fleet ?? new List<FleetAssetState>(); s.FleetCommands = s.FleetCommands ?? new List<FleetCommandRecord>(); s.ResaleQuotes = s.ResaleQuotes ?? new List<VehicleResaleQuote>(); s.Resales = s.Resales ?? new List<VehicleResaleRecord>(); s.CompanyLiquidations = s.CompanyLiquidations ?? new List<CompanyLiquidationRecord>(); s.OperatingCosts = s.OperatingCosts ?? new List<OperatingCostRecord>(); s.Market = s.Market ?? new FiniteMarketState(); s.LeaseClock = s.LeaseClock ?? new LeaseClock(); s.Leases = s.Leases ?? new List<LeaseContract>(); s.LeaseActions = s.LeaseActions ?? new List<LeaseActionRecord>(); s.Assignments = s.Assignments ?? new List<MissionAssignment>(); s.AssignmentCommands = s.AssignmentCommands ?? new List<MissionAssignmentCommand>(); s.IndustrialStocks = s.IndustrialStocks ?? new List<IndustrialStock>(); s.IndustrialRecipes = s.IndustrialRecipes ?? new List<IndustrialRecipe>(); s.IndustrialContracts = s.IndustrialContracts ?? new List<IndustrialContract>(); s.IndustrialCommands = s.IndustrialCommands ?? new List<MissionAssignmentCommand>(); s.OutboundLeases = s.OutboundLeases ?? new List<OutboundLeaseContract>(); s.OutboundLeaseActions = s.OutboundLeaseActions ?? new List<OutboundLeaseActionRecord>(); s.PassengerRoutes = s.PassengerRoutes ?? new List<PassengerRouteDemand>(); s.PassengerContracts = s.PassengerContracts ?? new List<PassengerServiceContract>(); s.PassengerCommands = s.PassengerCommands ?? new List<MissionAssignmentCommand>(); s.DynamicEconomy = s.DynamicEconomy ?? new DynamicEconomyState(); s.DedicatedAuthority = s.DedicatedAuthority ?? new DedicatedAuthorityState(); s.AssetLifecycle = s.AssetLifecycle ?? new AssetLifecycleState(); s.Financing = s.Financing ?? new FinancingStateStore(); s.TriageAssistance = s.TriageAssistance ?? new TriageAssistanceState(); s.InitialDeliveries = s.InitialDeliveries ?? new List<InitialDeliveryGrant>();
        CompanyEconomyPersistence.Validate(s.Economy); var assetValidation = AssetRegistry.Validate(s.Assets); if (!assetValidation.IsValid) throw new InvalidDataException(string.Join("; ", assetValidation.Errors));
        FiniteMarketValidation.Validate(s.Market);
        LeaseValidation.Validate(s);
        MissionAssignmentValidation.Validate(s);
        IndustrialEconomyValidation.Validate(s);
        OutboundLeaseValidation.Validate(s);
        PassengerEconomyValidation.Validate(s);
        DynamicEconomyValidation.Validate(s.DynamicEconomy);
        DedicatedAuthorityValidation.Validate(s.DedicatedAuthority);
        AssetLifecycleValidation.Validate(s.AssetLifecycle, s);
        FinancingValidation.Validate(s.Financing, s);
        TriageAssistanceValidation.Validate(s.TriageAssistance, s);
        InitialDeliveryValidation.Validate(s);
        if (s.Offers.GroupBy(x => x.OfferId).Any(g => g.Count() != 1) || s.Acquisitions.GroupBy(x => x.CommandId).Any(g => g.Count() != 1) || s.Ownership.GroupBy(x => x.AssetId).Any(g => g.Count() != 1)) throw new InvalidDataException("Duplicate acquisition identity.");
        if (s.Fleet.GroupBy(x => x.AssetId).Any(g => g.Count() != 1) || s.FleetCommands.GroupBy(x => x.CommandId).Any(g => g.Count() != 1) || s.ResaleQuotes.GroupBy(x => x.QuoteId).Any(g => g.Count() != 1) || s.Resales.GroupBy(x => x.CommandId).Any(g => g.Count() != 1) || s.CompanyLiquidations.GroupBy(x => x.CommandId).Any(g => g.Count() != 1) || s.OperatingCosts.GroupBy(x => x.SessionId).Any(g => g.Count() != 1)) throw new InvalidDataException("Duplicate fleet identity.");
        foreach (var o in s.Offers) if (o.Price < 0 || o.ReferenceValue < 0 || o.ObservedCondition < 0m || o.ObservedCondition > 1m || o.AppliedRate < 0m || !s.Assets.Assets.Any(a => a.AssetId == o.AssetId)) throw new InvalidDataException("Invalid vehicle offer.");
        foreach (var a in s.Assets.Assets) if (!s.Ownership.Any(o => o.AssetId == a.AssetId)) throw new InvalidDataException("Every asset requires explicit ownership.");
        foreach (var f in s.Fleet) if (f == null || string.IsNullOrWhiteSpace(f.AssetId) || !s.Assets.Assets.Any(a => a.AssetId == f.AssetId) || f.Version < 0 || (f.DisplayName ?? "").Length > 48) throw new InvalidDataException("Invalid fleet asset state.");
        foreach (var command in s.FleetCommands) if (command == null || string.IsNullOrWhiteSpace(command.CommandId) || string.IsNullOrWhiteSpace(command.Fingerprint) || !s.Assets.Assets.Any(a => a.AssetId == command.AssetId) || command.FleetVersionBefore < -1 || command.FleetVersionAfter < -1) throw new InvalidDataException("Invalid fleet command record.");
        foreach (var quote in s.ResaleQuotes) if (quote == null || string.IsNullOrWhiteSpace(quote.QuoteId) || string.IsNullOrWhiteSpace(quote.Fingerprint) || quote.AssetIds == null || quote.AssetIds.Count == 0 || quote.AssetIds.Distinct(StringComparer.Ordinal).Count() != quote.AssetIds.Count || quote.AssetIds.Any(id => !s.Assets.Assets.Any(a => a.AssetId == id)) || quote.Proceeds < 0 || quote.ReferenceValue < 0 || quote.FuelValue < 0 || quote.Proceeds > quote.ReferenceValue + quote.FuelValue || quote.TransferFee < 0 || quote.ObservedCondition < 0m || quote.ObservedCondition > 1m) throw new InvalidDataException("Invalid resale quote.");
        foreach (var resale in s.Resales) if (resale == null || string.IsNullOrWhiteSpace(resale.CommandId) || string.IsNullOrWhiteSpace(resale.Fingerprint) || resale.AssetIds == null || resale.AssetIds.Count == 0 || resale.AssetIds.Any(id => !s.Assets.Assets.Any(a => a.AssetId == id))) throw new InvalidDataException("Invalid resale record.");
        foreach (var liquidation in s.CompanyLiquidations) if (liquidation == null || string.IsNullOrWhiteSpace(liquidation.CommandId) || string.IsNullOrWhiteSpace(liquidation.Fingerprint) || string.IsNullOrWhiteSpace(liquidation.CompanyId) || liquidation.Debts < 0 || liquidation.Penalties < 0 || liquidation.AssetIds == null || liquidation.BeneficiaryIds == null || liquidation.AssetIds.Any(id => !s.Assets.Assets.Any(a => a.AssetId == id))) throw new InvalidDataException("Invalid company liquidation record.");
        foreach (var cost in s.OperatingCosts) if (cost == null || string.IsNullOrWhiteSpace(cost.SessionId) || string.IsNullOrWhiteSpace(cost.Fingerprint) || string.IsNullOrWhiteSpace(cost.AssetId) || cost.Payer == null || cost.MaximumAuthorizedCost < 0 || cost.ReservedAmount < 0 || cost.ReservedAmount > cost.MaximumAuthorizedCost || cost.VanillaBalanceBefore < 0 || cost.VanillaBalanceAfter < 0 || cost.ActualCost < 0 || cost.ConditionBefore < 0m || cost.ConditionBefore > 1m || cost.ConditionAfter < 0m || cost.ConditionAfter > 1m || !s.Assets.Assets.Any(a => a.AssetId == cost.AssetId) || (cost.Payer.Kind == AccountKind.Player && cost.ReservedAmount != 0) || (cost.State == OperatingCostState.Open && cost.Payer.Kind == AccountKind.Company && cost.MaximumAuthorizedCost > 0 && (cost.ReservedAmount != cost.MaximumAuthorizedCost || cost.ReservationReleased)) || (cost.State != OperatingCostState.Open && cost.ReservedAmount > 0 && !cost.ReservationReleased)) throw new InvalidDataException("Invalid operating cost record.");
    }

    private static void Migrate(VehicleAcquisitionSnapshot snapshot)
    {
        if (snapshot.Schema == VehicleAcquisitionSnapshot.CurrentSchema && snapshot.SchemaVersion >= 1 && snapshot.SchemaVersion < VehicleAcquisitionSnapshot.CurrentVersion)
        {
            var sourceVersion = snapshot.SchemaVersion;
            snapshot.Fleet = snapshot.Fleet ?? new List<FleetAssetState>();
            snapshot.FleetCommands = snapshot.FleetCommands ?? new List<FleetCommandRecord>();
            snapshot.ResaleQuotes = snapshot.ResaleQuotes ?? new List<VehicleResaleQuote>();
            snapshot.Resales = snapshot.Resales ?? new List<VehicleResaleRecord>();
            snapshot.CompanyLiquidations = snapshot.CompanyLiquidations ?? new List<CompanyLiquidationRecord>();
            snapshot.OperatingCosts = snapshot.OperatingCosts ?? new List<OperatingCostRecord>();
            snapshot.Market = snapshot.Market ?? new FiniteMarketState();
            snapshot.LeaseClock = snapshot.LeaseClock ?? new LeaseClock();
            snapshot.Leases = snapshot.Leases ?? new List<LeaseContract>();
            snapshot.LeaseActions = snapshot.LeaseActions ?? new List<LeaseActionRecord>();
            snapshot.Assignments = snapshot.Assignments ?? new List<MissionAssignment>();
            snapshot.AssignmentCommands = snapshot.AssignmentCommands ?? new List<MissionAssignmentCommand>();
            snapshot.IndustrialStocks = snapshot.IndustrialStocks ?? new List<IndustrialStock>();
            snapshot.IndustrialRecipes = snapshot.IndustrialRecipes ?? new List<IndustrialRecipe>();
            snapshot.IndustrialContracts = snapshot.IndustrialContracts ?? new List<IndustrialContract>();
            snapshot.IndustrialCommands = snapshot.IndustrialCommands ?? new List<MissionAssignmentCommand>();
            snapshot.OutboundLeases = snapshot.OutboundLeases ?? new List<OutboundLeaseContract>();
            snapshot.OutboundLeaseActions = snapshot.OutboundLeaseActions ?? new List<OutboundLeaseActionRecord>();
            snapshot.PassengerRoutes = snapshot.PassengerRoutes ?? new List<PassengerRouteDemand>();
            snapshot.PassengerContracts = snapshot.PassengerContracts ?? new List<PassengerServiceContract>();
            snapshot.PassengerCommands = snapshot.PassengerCommands ?? new List<MissionAssignmentCommand>();
            snapshot.DynamicEconomy = snapshot.DynamicEconomy ?? new DynamicEconomyState();
            snapshot.DedicatedAuthority = snapshot.DedicatedAuthority ?? new DedicatedAuthorityState();
            snapshot.AssetLifecycle = snapshot.AssetLifecycle ?? new AssetLifecycleState();
            snapshot.Financing = snapshot.Financing ?? new FinancingStateStore();
            snapshot.TriageAssistance = snapshot.TriageAssistance ?? new TriageAssistanceState();
            snapshot.InitialDeliveries = snapshot.InitialDeliveries ?? new List<InitialDeliveryGrant>();
            if (sourceVersion < 18)
            {
                foreach (var cost in snapshot.OperatingCosts)
                {
                    cost.ReservedAmount = 0;
                    cost.ReservationReleased = true;
                    if (cost.State == OperatingCostState.Open && cost.Payer?.Kind == AccountKind.Company)
                    {
                        cost.State = OperatingCostState.Rejected;
                        cost.ResultCode = "legacy-session-reservation-missing";
                    }
                }
            }
            foreach (var quote in snapshot.ResaleQuotes)
            {
                quote.AssetIds = quote.AssetIds == null || quote.AssetIds.Count == 0 ? new List<string> { quote.AssetId } : quote.AssetIds;
                if (string.IsNullOrWhiteSpace(quote.Fingerprint)) quote.Fingerprint = "migrated-resale-quote:" + quote.QuoteId;
            }
            foreach (var resale in snapshot.Resales)
                resale.AssetIds = resale.AssetIds == null || resale.AssetIds.Count == 0 ? new List<string> { resale.AssetId } : resale.AssetIds;
            foreach (var asset in snapshot.Assets?.Assets ?? new List<FleetAsset>())
                FleetManagementEngine.EnsureAsset(snapshot, asset.AssetId, null, asset.DefinitionId, asset.DefinitionId);
            snapshot.SchemaVersion = VehicleAcquisitionSnapshot.CurrentVersion;
        }
    }
}
