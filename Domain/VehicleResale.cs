using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum ResaleQuoteState { Available, Sold, Cancelled }
public enum ResaleState { Succeeded, Rejected, ReconcileRequired }
public enum ResaleFailurePoint { None, AfterWorldTransfer, AfterOwnershipCommit, AfterCredit }
public enum AssetReleaseStatus { Releasable, Blocked, Unknown }

[DataContract]
public sealed class AssetReleaseInspection
{
    [DataMember(Name = "status", Order = 1)] public AssetReleaseStatus Status { get; set; }
    [DataMember(Name = "detail", Order = 2)] public string Detail { get; set; } = "";
}

public interface IAssetReleaseGuard
{
    AssetReleaseInspection Inspect(string persistentCarGuid);
}

public interface IAssetBundleReleaseGuard : IAssetReleaseGuard
{
    IReadOnlyDictionary<string, AssetReleaseInspection> InspectBundle(IReadOnlyList<string> persistentCarGuids);
}

[DataContract]
public sealed class VehicleResaleQuote
{
    [DataMember(Name = "quoteId", Order = 1)] public string QuoteId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 2)] public string AssetId { get; set; } = "";
    [DataMember(Name = "seller", Order = 3)] public AssetOwnerRef Seller { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "payee", Order = 4)] public AccountRef Payee { get; set; } = new AccountRef();
    [DataMember(Name = "proceeds", Order = 5)] public long Proceeds { get; set; }
    [DataMember(Name = "referenceValue", Order = 6)] public long ReferenceValue { get; set; }
    [DataMember(Name = "referenceSource", Order = 7)] public ReferenceValueSource ReferenceSource { get; set; }
    [DataMember(Name = "observedCondition", Order = 8)] public decimal ObservedCondition { get; set; }
    [DataMember(Name = "appliedRate", Order = 9)] public decimal AppliedRate { get; set; }
    [DataMember(Name = "transferFee", Order = 10)] public long TransferFee { get; set; }
    [DataMember(Name = "version", Order = 11)] public long Version { get; set; }
    [DataMember(Name = "state", Order = 12)] public ResaleQuoteState State { get; set; }
    [DataMember(Name = "fuelValue", Order = 13)] public long FuelValue { get; set; }
    [DataMember(Name = "assetIds", Order = 14)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "bundleId", Order = 15)] public string? BundleId { get; set; }
    [DataMember(Name = "fingerprint", Order = 16)] public string Fingerprint { get; set; } = "";
}

[DataContract]
public sealed class SellVehicleCommand
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 2)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "quoteId", Order = 3)] public string QuoteId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 4)] public string AssetId { get; set; } = "";
    [DataMember(Name = "expectedFleetVersion", Order = 5)] public long ExpectedFleetVersion { get; set; }
    [DataMember(Name = "expectedOwnershipVersion", Order = 6)] public long ExpectedOwnershipVersion { get; set; }
    [DataMember(Name = "expectedQuoteVersion", Order = 7)] public long ExpectedQuoteVersion { get; set; }
    [DataMember(Name = "expectedWalletVersion", Order = 8)] public long ExpectedWalletVersion { get; set; }
    [DataMember(Name = "expectedFleetVersions", Order = 9)] public Dictionary<string, long> ExpectedFleetVersions { get; set; } = new Dictionary<string, long>();
    [DataMember(Name = "expectedOwnershipVersions", Order = 10)] public Dictionary<string, long> ExpectedOwnershipVersions { get; set; } = new Dictionary<string, long>();
}

[DataContract]
public sealed class VehicleResaleRecord
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 3)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "quoteId", Order = 4)] public string QuoteId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 5)] public string AssetId { get; set; } = "";
    [DataMember(Name = "state", Order = 6)] public ResaleState State { get; set; }
    [DataMember(Name = "resultCode", Order = 7)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "proceeds", Order = 8)] public long Proceeds { get; set; }
    [DataMember(Name = "referenceValue", Order = 9)] public long ReferenceValue { get; set; }
    [DataMember(Name = "referenceSource", Order = 10)] public ReferenceValueSource ReferenceSource { get; set; }
    [DataMember(Name = "observedCondition", Order = 11)] public decimal ObservedCondition { get; set; }
    [DataMember(Name = "appliedRate", Order = 12)] public decimal AppliedRate { get; set; }
    [DataMember(Name = "transferFee", Order = 13)] public long TransferFee { get; set; }
    [DataMember(Name = "releaseDetail", Order = 14)] public string ReleaseDetail { get; set; } = "";
    [DataMember(Name = "fuelValue", Order = 15)] public long FuelValue { get; set; }
    [DataMember(Name = "assetIds", Order = 16)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "ownershipCommitted", Order = 17)] public bool OwnershipCommitted { get; set; }
    [DataMember(Name = "creditCommitted", Order = 18)] public bool CreditCommitted { get; set; }
}

public sealed class VehicleResaleEngine
{
    private sealed class InspectedAsset
    {
        public string Id { get; set; } = "";
        public AssetReleaseInspection Inspection { get; set; } = new AssetReleaseInspection();
    }

    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IAssetReleaseGuard releaseGuard;
    private readonly IExistingVehicleOwnershipAdapter world;
    public ResaleFailurePoint FailurePoint { get; set; }

    public VehicleResaleEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IAssetReleaseGuard releaseGuard, IExistingVehicleOwnershipAdapter world)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.releaseGuard = releaseGuard ?? throw new ArgumentNullException(nameof(releaseGuard));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        VehicleAcquisitionPersistence.Validate(state);
    }

    public VehicleResaleQuote PrepareQuote(string quoteId, string requesterId, string assetId, long proceeds, long referenceValue,
        ReferenceValueSource source, decimal condition, long fuelValue, long transferFee) =>
        Prepare(quoteId, requesterId, null, new[] { assetId }, proceeds, referenceValue, source, condition, fuelValue, transferFee);

    public VehicleResaleQuote PrepareBundleQuote(string quoteId, string requesterId, string bundleId, long proceeds, long referenceValue,
        ReferenceValueSource source, decimal condition, long fuelValue, long transferFee)
    {
        var bundle = state.Assets.Bundles.SingleOrDefault(x => x.BundleId == bundleId) ?? throw new InvalidOperationException("Unknown asset bundle.");
        return Prepare(quoteId, requesterId, bundleId, bundle.ComponentAssetIds, proceeds, referenceValue, source, condition, fuelValue, transferFee);
    }

    private VehicleResaleQuote Prepare(string quoteId, string requesterId, string? bundleId, IEnumerable<string> assetIds, long proceeds,
        long referenceValue, ReferenceValueSource source, decimal condition, long fuelValue, long transferFee)
    {
        lock (gate)
        {
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) throw new InvalidOperationException("Host authority is required.");
            var ids = (assetIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (string.IsNullOrWhiteSpace(quoteId) || string.IsNullOrWhiteSpace(requesterId) || ids.Count == 0) throw new ArgumentException("A complete resale quote identity is required.");
            if (bundleId != null && ids.Count < 2) throw new InvalidOperationException("A resale bundle requires at least two assets.");
            if (bundleId == null && state.Assets.Bundles.Any(x => x.ComponentAssetIds.Any(ids.Contains)))
                throw new InvalidOperationException("A bundle component can only be sold through its complete bundle.");
            if (proceeds < 0 || referenceValue < 0 || fuelValue < 0 || transferFee < 0 || condition < 0m || condition > 1m || proceeds > referenceValue + fuelValue) throw new ArgumentOutOfRangeException(nameof(proceeds));
            var fingerprint = QuoteFingerprint(requesterId, bundleId, ids, proceeds, referenceValue, source, condition, fuelValue, transferFee);
            var known = state.ResaleQuotes.SingleOrDefault(x => x.QuoteId == quoteId);
            if (known != null)
            {
                if (known.Fingerprint != fingerprint) throw new InvalidOperationException("A resale quote ID cannot be reused with another payload.");
                return known;
            }
            var owners = ids.Select(id => state.Ownership.Single(x => x.AssetId == id).Owner).ToArray();
            if (owners.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != 1) throw new InvalidOperationException("Every bundle component must have the same owner.");
            var player = state.Economy.Players.Single(x => x.PlayerId == requesterId);
            if (!CanManage(owners[0], player, true)) throw new InvalidOperationException("Fleet and funds permissions are required.");
            foreach (var id in ids) if (!state.Fleet.Any(x => x.AssetId == id)) throw new InvalidOperationException("Every resale component must be managed by the fleet registry.");
            var payee = owners[0].Kind == AssetOwnerKind.Player ? AccountRef.Player(owners[0].OwnerId) : AccountRef.Company(owners[0].OwnerId);
            var totalValue = referenceValue + fuelValue;
            var rate = totalValue == 0 ? 0m : Math.Round((decimal)(proceeds + transferFee) / totalValue, 6, MidpointRounding.AwayFromZero);
            var quote = new VehicleResaleQuote
            {
                QuoteId = quoteId, AssetId = ids[0], AssetIds = ids, BundleId = bundleId, Fingerprint = fingerprint,
                Seller = Clone(owners[0]), Payee = payee, Proceeds = proceeds, ReferenceValue = referenceValue,
                ReferenceSource = source, ObservedCondition = condition, AppliedRate = rate, TransferFee = transferFee,
                FuelValue = fuelValue, State = ResaleQuoteState.Available
            };
            state.ResaleQuotes.Add(quote);
            return quote;
        }
    }

    public VehicleResaleRecord Sell(SellVehicleCommand command)
    {
        lock (gate)
        {
            Require(command);
            var fingerprint = Fingerprint(command);
            var known = state.Resales.SingleOrDefault(x => x.CommandId == command.CommandId);
            if (known != null)
            {
                if (known.Fingerprint != fingerprint) throw new InvalidOperationException("A resale command ID cannot be reused with another payload.");
                return known;
            }
            var quote = state.ResaleQuotes.SingleOrDefault(x => x.QuoteId == command.QuoteId);
            var record = NewRecord(command, quote, fingerprint);
            state.Resales.Add(record);
            var rejection = Validate(command, quote);
            if (rejection != null) return Reject(record, rejection);

            var inspections = InspectAll(quote!).ToArray();
            record.ReleaseDetail = string.Join("|", inspections.Select(x => x.Id + ":" + x.Inspection.Detail));
            if (inspections.Any(x => x.Inspection.Status == AssetReleaseStatus.Blocked)) return Reject(record, "asset-not-releasable");
            if (inspections.Any(x => x.Inspection.Status == AssetReleaseStatus.Unknown)) return Reject(record, "release-state-unknown");

            var applied = new List<string>();
            foreach (var id in quote.AssetIds)
            {
                var asset = state.Assets.Assets.Single(x => x.AssetId == id);
                WorldOwnershipOutcome outcome;
                try { outcome = world.ApplyOwner(command.CommandId + ":merchant:" + id, asset.GameLink.Value!, AssetOwnerRef.Merchant("runtime-market")); }
                catch (Exception exception) { record.ReleaseDetail += ";" + exception.GetType().Name + ":" + exception.Message; return Pending(record, applied.Count == 0 ? "world-adapter-exception" : "world-partial-transfer"); }
                if (outcome == WorldOwnershipOutcome.Unknown) return Pending(record, applied.Count == 0 ? "world-owner-unknown" : "world-partial-transfer");
                if (outcome == WorldOwnershipOutcome.NotApplied) return applied.Count == 0 ? Reject(record, "world-owner-not-applied") : Pending(record, "world-partial-transfer");
                applied.Add(id);
            }
            if (Fail(ResaleFailurePoint.AfterWorldTransfer, record)) return record;
            return Commit(record, quote);
        }
    }

    public VehicleResaleRecord Reconcile(string commandId)
    {
        lock (gate)
        {
            var record = state.Resales.Single(x => x.CommandId == commandId);
            if (record.State != ResaleState.ReconcileRequired) return record;
            var quote = state.ResaleQuotes.Single(x => x.QuoteId == record.QuoteId);
            if (!record.OwnershipCommitted)
            {
                var results = quote.AssetIds.Select(id => world.InspectOwner(state.Assets.Assets.Single(x => x.AssetId == id).GameLink.Value!, AssetOwnerRef.Merchant("runtime-market"))).ToArray();
                if (results.Any(x => x == WorldOwnershipOutcome.Unknown) || results.Distinct().Count() > 1) return Pending(record, "world-partial-or-unknown");
                if (results.All(x => x == WorldOwnershipOutcome.NotApplied)) return Reject(record, "world-owner-not-applied");
            }
            return Commit(record, quote);
        }
    }

    private VehicleResaleRecord Commit(VehicleResaleRecord record, VehicleResaleQuote quote)
    {
        if (!record.OwnershipCommitted)
        {
            foreach (var id in quote.AssetIds)
            {
                var ownership = state.Ownership.Single(x => x.AssetId == id);
                var fleet = state.Fleet.Single(x => x.AssetId == id);
                ownership.Owner = AssetOwnerRef.Merchant("runtime-market"); ownership.Version++;
                fleet.Operator = null; fleet.OperationalState = FleetOperationalState.Stored; fleet.Version++;
            }
            quote.State = ResaleQuoteState.Sold; quote.Version++;
            record.OwnershipCommitted = true;
        }
        if (Fail(ResaleFailurePoint.AfterOwnershipCommit, record)) return record;
        if (!record.CreditCommitted)
        {
            var wallet = state.Economy.Wallets.Single(x => x.Account.Key == quote.Payee.Key);
            wallet.Balance += quote.Proceeds; wallet.Version++;
            var entryId = record.CommandId + ":credit";
            if (!state.Economy.Ledger.Any(x => x.EntryId == entryId)) state.Economy.Ledger.Add(new LedgerEntry
            {
                EntryId = entryId, CommandId = record.CommandId, Kind = LedgerEntryKind.VehicleSale, Credit = wallet.Account, Amount = quote.Proceeds,
                Detail = "quote=" + quote.QuoteId + ";bundle=" + (quote.BundleId ?? "none") + ";assets=" + string.Join(",", quote.AssetIds) + ";reference=" + quote.ReferenceValue + ";source=" + quote.ReferenceSource + ";condition=" + quote.ObservedCondition + ";rate=" + quote.AppliedRate + ";fuel=" + quote.FuelValue + ";fee=" + quote.TransferFee
            });
            record.CreditCommitted = true;
        }
        if (Fail(ResaleFailurePoint.AfterCredit, record)) return record;
        record.State = ResaleState.Succeeded; record.ResultCode = "sold"; return record;
    }

    private string? Validate(SellVehicleCommand command, VehicleResaleQuote? quote)
    {
        if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return "host-authority-required";
        if (quote == null || quote.AssetId != command.AssetId) return "quote-selection-mismatch";
        if (quote.State != ResaleQuoteState.Available) return "quote-unavailable";
        var player = state.Economy.Players.SingleOrDefault(x => x.PlayerId == command.RequesterId);
        if (player == null || !CanManage(quote.Seller, player, true)) return "fleet-or-funds-permission-denied";
        foreach (var id in quote.AssetIds)
        {
            var fleet = state.Fleet.SingleOrDefault(x => x.AssetId == id);
            var ownership = state.Ownership.SingleOrDefault(x => x.AssetId == id);
            if (fleet == null || ownership == null || ownership.Owner.Key != quote.Seller.Key) return "ownership-mismatch";
            if (state.OperatingCosts.Any(x => x.AssetId == id && (x.State == OperatingCostState.Open || x.ExternalSettlement == ExternalSettlementState.Pending || x.ExternalSettlement == ExternalSettlementState.Conflict))) return "operating-cost-session-active";
            if (fleet.OperationalState != FleetOperationalState.Available && fleet.OperationalState != FleetOperationalState.Stored) return "asset-not-transferable";
            if (fleet.Operator != null) return "operator-assignment-active";
            if (fleet.Version != Expected(command.ExpectedFleetVersions, id, command.ExpectedFleetVersion) || ownership.Version != Expected(command.ExpectedOwnershipVersions, id, command.ExpectedOwnershipVersion)) return "version-mismatch";
        }
        var wallet = state.Economy.Wallets.SingleOrDefault(x => x.Account.Key == quote.Payee.Key);
        if (wallet == null) return "payee-not-found";
        if (quote.Version != command.ExpectedQuoteVersion || wallet.Version != command.ExpectedWalletVersion) return "version-mismatch";
        return null;
    }

    private AssetReleaseInspection Inspect(string assetId)
    {
        try { return releaseGuard.Inspect(state.Assets.Assets.Single(x => x.AssetId == assetId).GameLink.Value!); }
        catch (Exception exception) { return new AssetReleaseInspection { Status = AssetReleaseStatus.Unknown, Detail = "inspection-exception:" + exception.GetType().Name }; }
    }

    private IEnumerable<InspectedAsset> InspectAll(VehicleResaleQuote quote)
    {
        if (quote.BundleId != null && releaseGuard is IAssetBundleReleaseGuard bundleGuard)
        {
            var links = quote.AssetIds.ToDictionary(id => id, id => state.Assets.Assets.Single(x => x.AssetId == id).GameLink.Value!, StringComparer.Ordinal);
            IReadOnlyDictionary<string, AssetReleaseInspection> results;
            try { results = bundleGuard.InspectBundle(links.Values.ToArray()); }
            catch (Exception exception)
            {
                return quote.AssetIds.Select(id => new InspectedAsset { Id = id, Inspection = new AssetReleaseInspection { Status = AssetReleaseStatus.Unknown, Detail = "bundle-inspection-exception:" + exception.GetType().Name } });
            }
            return quote.AssetIds.Select(id => new InspectedAsset { Id = id, Inspection = results.TryGetValue(links[id], out var inspection) ? inspection : new AssetReleaseInspection { Status = AssetReleaseStatus.Unknown, Detail = "bundle-inspection-result-missing" } });
        }
        return quote.AssetIds.Select(id => new InspectedAsset { Id = id, Inspection = Inspect(id) });
    }

    private bool CanManage(AssetOwnerRef owner, PlayerEconomicState player, bool requireFunds)
    {
        if (owner.Kind == AssetOwnerKind.Player) return owner.OwnerId == player.PlayerId;
        if (owner.Kind != AssetOwnerKind.Company || player.CompanyId != owner.OwnerId) return false;
        var company = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == owner.OwnerId);
        if (company == null || company.Liquidating) return false;
        if (company.LeaderId == player.PlayerId) return true;
        if (!company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights)) return false;
        return rights.Contains(CompanyPermission.ManageFleet) && (!requireFunds || rights.Contains(CompanyPermission.ManageFunds));
    }

    private bool Fail(ResaleFailurePoint point, VehicleResaleRecord record)
    {
        if (FailurePoint != point) return false;
        FailurePoint = ResaleFailurePoint.None; Pending(record, "injected-failure:" + point); return true;
    }

    private static long Expected(Dictionary<string, long>? versions, string assetId, long fallback) => versions != null && versions.TryGetValue(assetId, out var value) ? value : fallback;
    private static VehicleResaleRecord Reject(VehicleResaleRecord record, string code) { record.State = ResaleState.Rejected; record.ResultCode = code; return record; }
    private static VehicleResaleRecord Pending(VehicleResaleRecord record, string code) { record.State = ResaleState.ReconcileRequired; record.ResultCode = code; return record; }
    private static VehicleResaleRecord NewRecord(SellVehicleCommand command, VehicleResaleQuote? quote, string fingerprint) => new VehicleResaleRecord
    {
        CommandId = command.CommandId, Fingerprint = fingerprint, RequesterId = command.RequesterId, QuoteId = command.QuoteId, AssetId = command.AssetId,
        AssetIds = quote?.AssetIds.ToList() ?? new List<string> { command.AssetId }, Proceeds = quote?.Proceeds ?? 0, ReferenceValue = quote?.ReferenceValue ?? 0,
        ReferenceSource = quote?.ReferenceSource ?? ReferenceValueSource.ConfiguredModel, ObservedCondition = quote?.ObservedCondition ?? 0m,
        AppliedRate = quote?.AppliedRate ?? 0m, TransferFee = quote?.TransferFee ?? 0, FuelValue = quote?.FuelValue ?? 0
    };
    private static string QuoteFingerprint(string requester, string? bundle, IEnumerable<string> ids, long proceeds, long reference, ReferenceValueSource source, decimal condition, long fuel, long fee) =>
        string.Join("|", requester, bundle ?? "", string.Join(",", ids), proceeds, reference, source, condition, fuel, fee);
    private static string Fingerprint(SellVehicleCommand command) => string.Join("|", command.RequesterId, command.QuoteId, command.AssetId,
        command.ExpectedFleetVersion, command.ExpectedOwnershipVersion, command.ExpectedQuoteVersion, command.ExpectedWalletVersion,
        string.Join(",", (command.ExpectedFleetVersions ?? new Dictionary<string, long>()).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)),
        string.Join(",", (command.ExpectedOwnershipVersions ?? new Dictionary<string, long>()).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
    private static AssetOwnerRef Clone(AssetOwnerRef value) => new AssetOwnerRef { Kind = value.Kind, OwnerId = value.OwnerId };
    private static void Require(SellVehicleCommand command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.CommandId) || string.IsNullOrWhiteSpace(command.RequesterId) || string.IsNullOrWhiteSpace(command.QuoteId) || string.IsNullOrWhiteSpace(command.AssetId))
            throw new ArgumentException("A complete resale command is required.");
    }
}
