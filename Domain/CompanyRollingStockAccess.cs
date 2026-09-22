using System;
using System.Collections.Generic;

namespace BDVM.Domain;

/// <summary>Small live-row index owned by the Unity authority. Never serialize or publish it to workers.</summary>
public sealed class CompanyRollingStockAccess
{
    private readonly Dictionary<Guid, string> cars = new Dictionary<Guid, string>();
    private readonly Dictionary<string, AssetOwnership> owners = new Dictionary<string, AssetOwnership>(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerEconomicState> players = new Dictionary<string, PlayerEconomicState>(StringComparer.Ordinal);
    private readonly Dictionary<string, CompanyState> companies = new Dictionary<string, CompanyState>(StringComparer.Ordinal);
    private readonly HashSet<Guid> ambiguous = new HashSet<Guid>();
    public CompanyRollingStockAccess(VehicleAcquisitionSnapshot state)
    {
        foreach (var asset in state.Assets.Assets)
            if (Guid.TryParse(asset.GameLink.Value, out var guid) && guid != Guid.Empty)
            {
                if (cars.ContainsKey(guid)) ambiguous.Add(guid);
                else cars.Add(guid, asset.AssetId);
            }
        foreach (var owner in state.Ownership) owners.Add(owner.AssetId, owner);
        foreach (var player in state.Economy.Players) players.Add(player.PlayerId, player);
        foreach (var company in state.Economy.Companies) companies.Add(company.CompanyId, company);
    }
    public bool Allows(string actorId, string carGuid)
    {
        if (!Guid.TryParse(carGuid, out var guid) || guid == Guid.Empty) return false;
        if (ambiguous.Contains(guid)) return false;
        if (!cars.TryGetValue(guid, out var assetId)) return true; // Unmanaged stock keeps native rules.
        if (!owners.TryGetValue(assetId, out var ownership)) return false;
        var owner = ownership.Owner;
        if (owner.Kind != AssetOwnerKind.Company) return true; // Personal/merchant policy is unchanged.
        return !string.IsNullOrEmpty(actorId) && players.TryGetValue(actorId, out var player) &&
            player.CompanyId == owner.OwnerId && companies.TryGetValue(owner.OwnerId, out var company) &&
            !company.Liquidating && company.Members.Contains(actorId);
    }
}
