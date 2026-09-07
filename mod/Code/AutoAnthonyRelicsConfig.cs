using BaseLib.Config;

namespace AutoAnthonyRelics;

/// <summary>Runtime toggles (Settings -> Mod Settings), Spire1Config pattern.</summary>
[ConfigHoverTipsByDefault]
internal class AutoAnthonyRelicsConfig : SimpleModConfig
{
    /// <summary>Master switch. When false, chaos relics never generate or apply.</summary>
    public static bool EnableChaosRelics { get; set; } = true;

    /// <summary>
    /// Entry multiplier vs the card algorithm baseline. Default 3 per the user
    /// order (each relic gets 3x the entries a card of the same rarity roll
    /// would get). 1 = card-like counts.
    /// </summary>
    public static int ChaosRelicMultiplier { get; set; } = 3;
}
