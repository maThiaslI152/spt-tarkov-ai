using SAIN.Attributes;

namespace SAIN.Preset.GlobalSettings;

public class RogueBaseDefenseSettings : SAINSettingsBase<RogueBaseDefenseSettings>, ISAINSettings
{
    [Name("Enable Rogue Base Defense Policy")]
    [Description("When enabled, ExUsec squads on Lighthouse favor coordinated hold/patrol behavior and avoid looting.")]
    public bool EnableRogueBaseDefensePolicy = true;

    [Name("Disable Rogue Looting On Base")]
    [Description("Uses LootingBots interop to prevent ExUsec looting while Rogue base defense policy is active.")]
    public bool DisableRogueLootingOnBase = true;

    [Name("Only On Lighthouse")]
    [Description("Scope guard for Rogue base defense policy. If enabled, policy applies only when current map is Lighthouse.")]
    public bool OnlyOnLighthouse = true;

    [Name("Rogue Leader Hold Seconds")]
    [Description("Minimum time to keep a selected Rogue squad leader before re-election, unless leader becomes invalid.")]
    [MinMax(2f, 30f, 0.5f)]
    public float RogueLeaderHoldSeconds = 8f;

    [Name("Rogue Order TTL Seconds")]
    [Description("Lifetime of squad orders before automatic expiry/cancel.")]
    [MinMax(1f, 20f, 0.5f)]
    public float RogueOrderTtlSeconds = 6f;
}
