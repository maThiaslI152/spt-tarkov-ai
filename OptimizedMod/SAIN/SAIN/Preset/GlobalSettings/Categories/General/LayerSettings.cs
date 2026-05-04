using SAIN.Attributes;

namespace SAIN.Preset.GlobalSettings;

public class LayerSettings : SAINSettingsBase<LayerSettings>, ISAINSettings
{
    [Description(
        "BigBrain: highest SAIN combat layer. Must stay below SAINAvoidThreatLayer (80). "
            + "Raised above typical BotMind/QuestingBots quest layers so combat can preempt quest when IsActive."
    )]
    [DeveloperOption]
    [MinMax(0, 79)]
    public int SAINCombatSquadLayerPriority = 78;

    [Description(
        "BigBrain: solo combat. Keep below squad priority and below AvoidThreat (80). Restart required."
    )]
    [DeveloperOption]
    [MinMax(0, 78)]
    public int SAINCombatSoloLayerPriority = 77;

    [Description("BigBrain: below solo combat so fights beat extract. Restart required.")]
    [DeveloperOption]
    [MinMax(0, 76)]
    public int SAINExtractLayerPriority = 74;
}
