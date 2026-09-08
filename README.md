# BDVM - Fleet

`BDVM.Fleet` models railway equipment as persistent economic assets. It owns identity, ownership, acquisition, resale, leasing, lifecycle protection and the versioned classification policy for physical rolling-stock population.

## Status

| Property | Value |
| --- | --- |
| Module kind | Fleet feature |
| Target framework | .NET Framework 4.8 (`net48`) |
| Required modules | `BDVM.Common`, `BDVM.Companies` |
| Standalone install | Not yet |
| Current runtime host | `BDVM.Full` |

## Responsibilities

- Assign stable identities to assets independently of temporary scene objects.
- Distinguish merchant, player and company ownership.
- Reserve, debit, transfer and checkpoint acquisitions with compensation or reconciliation states when an external operation fails.
- Sell assets only after release guards confirm that the complete asset bundle is safe to remove.
- Calculate resale against condition and the configured economic policy.
- Model leases, deposits, rent installments, damage settlement, purchase options and recall.
- Track assets that are temporarily absent, stale, ambiguous or missing required content without silently deleting ownership.
- Protect owned rolling stock from cleanup systems through a dedicated lifecycle port.

## Key surfaces

Important entry points include `VehicleAcquisitionEngine`, `VehicleResaleEngine`, the fleet-management and leasing engines, and `AssetLifecycleEngine`. Durable models include `AssetOwnership`, `VehicleOffer`, acquisition and resale records, persistent asset links and lifecycle state. World interaction is isolated behind `IExistingVehicleOwnershipAdapter`, checkpoint sinks and release/protection ports.

## Boundaries

Fleet does not decide market supply or prices, debit wallets directly, spawn game objects on its own or generate freight jobs. `WorldPopulationPolicy` classifies purchased, leased, starter, recovery, external traffic, natural, contract-provided, tutorial and unknown sources; runtime adapters enforce those decisions at targeted generator boundaries. `BDVM.Market` supplies catalog and financing decisions, `BDVM.Companies` owns money, and the runtime composition provides Unity adapters.

## Dependencies and composition

The declared dependencies are `BDVM.Common` and `BDVM.Companies`. During the current migration, the repository owns `Domain/`, but those files are linked into `BDVM.Full`; the standalone project currently compiles the module marker. This avoids duplicate runtime types until packaging is finalized.

External dependencies: none. Vanilla and Custom Car Loader vehicles are represented through stable identifiers and Unity adapters; Fleet does not link against a Custom Car Loader assembly.

## Build

Keep the required repositories as siblings under `src/`, then run:

```powershell
dotnet build .\BDVM.Fleet.csproj -c Release
```

Build `BDVM.Full` with a valid Derail Valley installation to compile the Unity ownership, acquisition, release and cleanup adapters.

## Testing and installation

The domain suite covers acquisition rollback, retry safety, resale release guards, leasing schedules, persistence and lifecycle ambiguity. There is no standalone mod package yet. In-game use currently comes from the matching `BDVM.Full` composition.

## Compatibility

Persistent asset IDs must remain stable across saves and sessions. Unknown or conflicting world ownership produces a reconcile-required state rather than guessing. Removing or downgrading content must never make a company asset disappear economically.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and the applied copyright [NOTICE](NOTICE).
