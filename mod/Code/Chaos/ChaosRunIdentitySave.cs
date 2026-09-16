using System;
using System.Runtime.CompilerServices;
using BaseLib.Patches.Saves;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Persists the run identity (WS-0916-06) with the run save, through BaseLib's
/// existing per-save extension surface - no parallel persistence layer.
///
/// WHY THIS IS ENOUGH: BaseLib 3.4.5 already patches every place the basegame
/// moves a run, so one registration covers all of them:
/// - <c>RunManager.ToSave</c> postfix builds the extended data from
///   <c>State</c> -&gt; our getter, so the token lands in the run save JSON;
/// - <c>RunState.FromSerializable</c> postfix -&gt; our setter, so a load
///   restores the token;
/// - <c>SerializableRun.Serialize/Deserialize</c> postfixes, so the same token
///   rides the multiplayer packet path (both ends agree on the identity);
/// - <c>Anonymized</c>/<c>CanonicalizeSave</c> postfixes, so the token is not
///   dropped by save canonicalization.
/// The value is stored per RunState instance in a weak table, so nothing
/// survives a run it does not belong to.
///
/// REGISTRATION TIMING (load-bearing): BaseLib materializes the extended JSON
/// properties lazily and then freezes the list
/// (ExtendedSaveHandlers._initializedSaveProps) - a registration performed
/// after that point is dropped with a warning and the value would never be
/// written or read. <see cref="Register"/> therefore runs from
/// <c>MainFile.Initialize</c>, before any run can be saved or loaded.
///
/// FAILURE CONTRACT: if BaseLib refuses the registration the identity cannot be
/// persisted. That is reported as an Error and
/// <see cref="RegistrationSucceeded"/> stays false, which makes every capture
/// fall back to the deterministic legacy identity
/// (<see cref="ChaosRunIdentity.Legacy"/>) instead of degrading to a
/// process-local decision.
/// </summary>
internal static class ChaosRunIdentitySave
{
    /// <summary>
    /// Save key of the identity value. Prefixed with the mod id so it cannot
    /// collide with another mod's entry in BaseLib's shared per-type
    /// dictionary.
    /// </summary>
    internal const string SaveKey = "quriouscraftingrelics_run_identity";

    /// <summary>Identity per RunState instance (weak: dies with the run).</summary>
    private static readonly ConditionalWeakTable<IRunState, string> Tokens = new();

    /// <summary>True once BaseLib accepted the registration.</summary>
    internal static bool RegistrationSucceeded { get; private set; }

    /// <summary>
    /// Registers the identity with BaseLib. Idempotent; called once from
    /// MainFile.Initialize.
    /// </summary>
    internal static void Register()
    {
        if (RegistrationSucceeded)
        {
            return;
        }
        try
        {
            RegistrationSucceeded = ExtendedSaveTypes.RegisterSavedValue<IRunState, string>(
                SaveKey,
                static runState => TokenOf(runState),
                static (runState, value) => Adopt(runState, value),
                static (value, writer) => writer.WriteString(value),
                static reader => reader.ReadString());
            if (!RegistrationSucceeded)
            {
                MainFile.Logger.Error(
                    "[QuriousCraftingRelics] run identity could not be registered with BaseLib; " +
                    "run saves will carry no identity token and captures fall back to the " +
                    "deterministic start-time identity.");
            }
        }
        catch (Exception e)
        {
            RegistrationSucceeded = false;
            MainFile.Logger.Error($"[QuriousCraftingRelics] run identity registration failed: {e.Message}");
        }
    }

    /// <summary>Token stored for a run state; null when none (BaseLib then
    /// writes nothing for this key).</summary>
    internal static string? TokenOf(IRunState? runState)
    {
        if (runState is null)
        {
            return null;
        }
        return Tokens.TryGetValue(runState, out string? token) ? token : null;
    }

    /// <summary>Stores the identity for a run state.</summary>
    internal static void Remember(IRunState? runState, string? token)
    {
        if (runState is null || string.IsNullOrEmpty(token))
        {
            return;
        }
        Tokens.AddOrUpdate(runState, token);
    }

    /// <summary>Setter handed to BaseLib: a loaded save carried this token.</summary>
    private static void Adopt(IRunState? runState, string? value)
    {
        Remember(runState, value);
    }
}
