using System;
using System.Collections.Generic;
using System.Reflection;
using BaseLib.Config;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace QuriousCraftingRelics;

/// <summary>
/// Mod entry point.
///
/// CONTENT-GATE CONTRACT (2026-10-02): the master switch
/// (<see cref="QuriousCraftingRelicsConfig.EnableChaosRelics"/>) plus the
/// per-group switches (pool / runtime hooks+loc / seed capture / extra template
/// pool) decide, at startup, which Harmony patch classes are installed at all.
///
/// ORDERING (verified against engine-dllsrc 2026-10-02): ModManager.Initialize
/// runs this initializer from OneTimeInitialization.ExecuteVeryEarly BEFORE
/// ModelDb.Init (ExecuteEssential). BaseLib's CustomRelicModel constructor
/// autoAdd's each slot type into ModHelper's pending list while ModelDb.Init
/// constructs the model types, so the pending list is EMPTY here and cannot be
/// stripped from this method. The pool group is therefore made truly
/// zero-registration by a tiny prefix on ModHelper.AddModelToPool, installed
/// before ModelDb.Init: while the group is off it REFUSES our own slot types
/// (exact type check only - other mods' entries are untouched), so the frozen
/// SharedRelicPool list never contains them and the pool replacement patches
/// are not installed at all.
///
/// Harmony cannot uninstall, so a group turned OFF after startup is enforced by
/// every installed body reading the live gates, while a group turned ON later
/// installs its missing classes through the ConfigChanged subscription below -
/// EXCEPT the pool group and the master switch, whose slot registration happens
/// once at ModelDb.Init and therefore requires a game restart to re-enable.
/// Settings-entry patches are never gated: they are the only path back to
/// re-enabling a group.
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "QuriousCraftingRelics";
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    /// <summary>
    /// True when the off-state registration gate is really installed; the pool
    /// group's install decision falls back to the protective patches when the
    /// gate could not be installed.
    /// </summary>
    internal static bool PoolRegistrationGateInstalled { get; private set; }

    /// <summary>
    /// Whether the 60 slot types were actually registered into the engine's
    /// SharedRelicPool during this process's ModelDb.Init. Latched from the
    /// startup config, never from live values: a pool that never received our
    /// slots must never be replaced (see Initialize).
    /// </summary>
    internal static bool PoolRegisteredAtStartup { get; private set; }

    public static void Initialize()
    {
        try
        {
            // MUST run before the config instance exists: the ModConfig
            // constructor chain (CheckConfigProperties -> Init -> Load) is
            // synchronous and reads the cfg file by root-namespace-derived
            // path, so a post-construction migration would be too late.
            // The migration never throws and touches no Godot API (see its
            // doc), so it cannot abort init; we log its report from here
            // because this method really does run inside Godot.
            ConfigMigrationReport migration = ConfigMigration.MigrateLegacyConfig();
            if (migration.Describe() is { } migrationLine)
            {
                if (migration.Succeeded)
                {
                    Logger.Info($"[QuriousCraftingRelics] {migrationLine}");
                }
                else
                {
                    Logger.Error($"[QuriousCraftingRelics] {migrationLine}");
                }
            }

            // Settings -> Mod Settings UI registration.
            ModConfigRegistry.Register(ModId, new QuriousCraftingRelicsConfig());

            // The shared compat is also built standalone, where a null gate
            // means enabled. This package reads the master switch live instead.
            Compat.RrcTreasureKeyCompat.EnabledGate = static () => QuriousCraftingRelicsConfig.EnableChaosRelics;

            // Run identity persistence (WS-0916-06). MUST happen here, during
            // mod init: BaseLib materializes the extended save properties lazily
            // and then freezes that list, so a registration performed after the
            // first save/load would be dropped and the identity would never be
            // written or read. A refused registration is reported and leaves the
            // registry on the deterministic legacy identity instead of a
            // process-local one.
            //
            // Registered even while the master switch is off: this is a pure
            // save-shape registration, not a gameplay patch, and registering it
            // LATER (after BaseLib froze the list) is impossible - so a profile
            // that starts with the mod disabled and enables it later would
            // otherwise never get identity persistence. With the master off no
            // run is captured, so the getter returns null and the save carries
            // nothing.
            Chaos.ChaosRunIdentitySave.Register();

            // Godot scenes shipped in the .pck (v1: none, but register anyway -
            // costs nothing and future-proof for icon-atlas scenes).

            // Pool-registration gate FIRST: with the pool group off, our own
            // slot types must not reach ModelDb's frozen pool list at all
            // (they are constructed later, during ModelDb.Init).
            Harmony harmony = new(ModId);
            InstallPoolRegistrationGate(harmony);

            // Latch the STARTUP registration fact. If the group was off and
            // the gate is installed, the 60 slot types will be refused during
            // ModelDb.Init; in every other case (group on, or gate failed)
            // they will be registered. A pool that never received our slots
            // must never be replaced - that would strip vanilla and leave an
            // empty bag. The gate prefix reads this latch, so it must be set
            // before ModelDb.Init even though registration happens later.
            PoolRegisteredAtStartup =
                QuriousCraftingRelicsConfig.PoolReplacementActive || !PoolRegistrationGateInstalled;
            Logger.Info(
                $"[QuriousCraftingRelics] startup registration latch: poolSlots={PoolRegisteredAtStartup}, " +
                $"gate={PoolRegistrationGateInstalled}");

            // Apply Harmony patches (Spire1 per-type try/catch pattern: one
            // bad patch must never abort the set). Patch classes are filtered
            // by the content-group gates: a group that is OFF at startup is
            // not installed at all; re-enabling it later installs the missing
            // classes through ConfigChanged (Harmony has no uninstall, so the
            // inverse direction is enforced by each body reading the live
            // gates). Exception: the pool group needs a restart (see class
            // doc). The settings-screen patches are never gated - they are the
            // only way to turn a group back on.
            InstallGatedPatches(harmony);
            if (ModConfigRegistry.Get(ModId) is { } registeredConfig)
            {
                registeredConfig.ConfigChanged += (_, _) =>
                {
                    InstallGatedPatches(harmony);
                    // The compat postfix is installed directly (not a
                    // [HarmonyPatch] class), so the class sweep above cannot
                    // pick it up when the master switch is re-enabled.
                    if (QuriousCraftingRelicsConfig.EnableChaosRelics)
                    {
                        Compat.RrcTreasureKeyCompat.TryInstall(harmony);
                    }
                };
            }

            // Cross-mod bugfix (RelicRewardChoices + Act4Heart Sapphire Key):
            // installs only when both mods are present and no twin instance
            // already claimed it (see RrcTreasureKeyCompat doc). Gated by the
            // master switch: contract 1 requires a disabled mod to install no
            // in-run behavior patch at all.
            if (QuriousCraftingRelicsConfig.EnableChaosRelics)
            {
                Compat.RrcTreasureKeyCompat.TryInstall(harmony);
            }

            // Touch the registry so slot-marker types resolve at startup: any
            // type-load failure surfaces in the log immediately instead of on
            // first pool generation mid-run.
            int slots = Pools.ChaosRelicRegistry.Types.Count;
            Logger.Info($"[QuriousCraftingRelics] initialized: {slots} chaos relic slots, " +
                        $"multiplier x{QuriousCraftingRelicsConfig.ChaosRelicMultiplier}, " +
                        $"master={QuriousCraftingRelicsConfig.EnableChaosRelics}, " +
                        $"pool={QuriousCraftingRelicsConfig.EnableChaosRelicPool}, " +
                        $"runtime={QuriousCraftingRelicsConfig.EnableRuntimeHooks}, " +
                        $"seed={QuriousCraftingRelicsConfig.EnableSeedCapture}, " +
                        $"extra={QuriousCraftingRelicsConfig.EnableExtraPool}");
        }
        catch (Exception e)
        {
            Logger.Error($"[QuriousCraftingRelics] initializer failed: {e}");
            throw;
        }
    }

    /// <summary>Patch classes already installed, so re-entrancy through
    /// ConfigChanged cannot double-install (Harmony tolerates it, but the set
    /// also keeps the retry-on-failure contract).</summary>
    private static readonly HashSet<Type> InstalledPatchTypes = new();

    /// <summary>
    /// Installs every [HarmonyPatch] class allowed by the CURRENT content-group
    /// gates. Idempotent; safe to call again after a config change. Classes
    /// skipped because their group was off at startup are installed the first
    /// time the group is re-enabled.
    /// </summary>
    private static void InstallGatedPatches(Harmony harmony)
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
            {
                continue;
            }
            if (!ShouldInstallPatch(type))
            {
                continue;
            }
            lock (InstalledPatchTypes)
            {
                if (!InstalledPatchTypes.Add(type))
                {
                    continue;
                }
            }
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                lock (InstalledPatchTypes)
                {
                    InstalledPatchTypes.Remove(type);
                }
                Logger.Error($"Harmony patch {type.Name} failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Install-time gate per patch class (2026-10-02 content groups, fail closed):
    /// - settings entry / submenu registration: always (the re-enabling path);
    /// - pool replacement classes: installed only when our slots really
    ///   reached the engine pool at startup (PoolRegisteredAtStartup). With
    ///   the group off at startup the gate refused them, so no pool class is
    ///   installed at all (true zero-registration). When the gate failed, the
    ///   slots are present and the class stays as the last-resort strip point
    ///   (non-zero registration, reported in the log);
    /// - runtime hooks: master AND the runtime group. The run-start-time
    ///   bookkeeping consumer belongs HERE, not to seed capture: with the
    ///   runtime group off it must not be installed even when seed capture is on;
    /// - seed capture classes: master AND the seed-capture group;
    /// - anything not listed above: REFUSED (fail closed). A new Harmony class
    ///   is inert until it is deliberately mapped to a switch here.
    /// </summary>
    private static bool ShouldInstallPatch(Type type)
    {
        if (type == typeof(Patches.RelicsSettingsScreenPatch)
            || type == typeof(Patches.RelicsSettingsSubmenuRegistrationPatch))
        {
            return true;
        }
        if (type == typeof(Patches.ChaosRelicPoolReplacementPlayerPatch)
            || type == typeof(Patches.ChaosRelicPoolReplacementEnumerablePatch))
        {
            // Install the pool classes only when our slots really reached the
            // engine pool at startup. With them absent (zero-registration),
            // there is nothing to replace and nothing to strip. When they are
            // present the class is needed either way: replacement while the
            // group is live, strip-own-slots when it is turned off. If the
            // registration gate itself failed, the classes stay as the
            // last-resort strip point (non-zero registration, logged).
            return PoolRegisteredAtStartup;
        }
        if (type == typeof(Patches.RunStartTimeCapturePatch))
        {
            return QuriousCraftingRelicsConfig.RuntimeHooksActive;
        }
        if (type == typeof(Patches.RunSeedEarlyTrackSingleplayerPatch)
            || type == typeof(Patches.RunSeedEarlyTrackMultiplayerPatch)
            || type == typeof(Patches.RunSeedTrackPatch)
            || type == typeof(Patches.RunSeedCleanUpPatch))
        {
            return QuriousCraftingRelicsConfig.SeedCaptureActive;
        }
        Logger.Error(
            $"[QuriousCraftingRelics] unclassified Harmony patch {type.Name} refused (fail closed); " +
            "map it to a content-group switch in ShouldInstallPatch.");
        return false;
    }

    /// <summary>
    /// Prefix gate for the engine's pool-registration entry point, installed
    /// while the pool replacement set (master AND pool group AND seed capture)
    /// is not active. If any of those legs is off, replacement can never be
    /// valid this process, so registering the slots would only risk leaks.
    ///
    /// BaseLib's CustomRelicModel constructor calls
    /// ModHelper.AddModelToPool(typeof(SharedRelicPool), slotType) during
    /// ModelDb.Init, which runs AFTER this initializer. Refusing OUR OWN slot
    /// types here (exact ChaosRelicModel subclasses only) keeps them out of
    /// the frozen SharedRelicPool list, so the pool group really is
    /// zero-registration: no pool replacement patch is installed and no
    /// effect-less slot can leak into the vanilla pool. Other mods' entries
    /// pass through untouched.
    ///
    /// Fail closed: if the target method cannot be patched (engine drift), the
    /// pool replacement patches stay installed as the last-resort strip point
    /// and the log records why.
    /// </summary>
    private static void InstallPoolRegistrationGate(Harmony harmony)
    {
        if (QuriousCraftingRelicsConfig.PoolReplacementActive)
        {
            return; // pool group active: registration must proceed
        }
        try
        {
            var modHelper = Type.GetType("MegaCrit.Sts2.Core.Modding.ModHelper, sts2");
            var addModel = modHelper?.GetMethod(
                "AddModelToPool",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type), typeof(Type) },
                null);
            if (addModel is null)
            {
                Logger.Error("[QuriousCraftingRelics] ModHelper.AddModelToPool(Type,Type) not found; pool group keeps its protective patch.");
                PoolRegistrationGateInstalled = false;
                return;
            }
            var prefix = new HarmonyMethod(typeof(MainFile).GetMethod(
                nameof(BlockOwnPoolRegistration),
                BindingFlags.NonPublic | BindingFlags.Static));
            harmony.Patch(addModel, prefix: prefix);
            PoolRegistrationGateInstalled = true;
            Logger.Info("[QuriousCraftingRelics] pool inactive at startup: own pool registration gate installed.");
        }
        catch (Exception e)
        {
            PoolRegistrationGateInstalled = false;
            Logger.Error($"[QuriousCraftingRelics] pool registration gate failed: {e.Message}; pool group keeps its protective patch.");
        }
    }

    /// <summary>
    /// Prefix for ModHelper.AddModelToPool: refuse ONLY this mod's own slot
    /// types while the startup registration latch is false. Runs for every mod's registration,
    /// so it must be allocation-free, exception-safe and must never veto
    /// another mod's model. <paramref name="poolType"/> is unused (kept for
    /// Harmony's parameter binding).
    /// </summary>
    private static bool BlockOwnPoolRegistration(Type poolType, Type modelType)
    {
        try
        {
            // Startup snapshot decides; live config edits during the
            // ModelDb.Init window must not flip the registration decision.
            if (!PoolRegisteredAtStartup
                && modelType is not null
                && typeof(Models.ChaosRelicModel).IsAssignableFrom(modelType))
            {
                return false; // our own slot: do not register while off
            }
        }
        catch
        {
            // Never let a gate failure veto another mod's registration.
        }
        return true;
    }
}
