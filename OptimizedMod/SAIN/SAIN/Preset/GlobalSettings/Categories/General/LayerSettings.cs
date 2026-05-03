using SAIN.Attributes;

namespace SAIN.Preset.GlobalSettings;

public class LayerSettings : SAINSettingsBase<LayerSettings>, ISAINSettings
{
    [Description("Requires Restart. Dont touch unless you know what this is")]
    [DeveloperOption]
    [MinMax(0, 100)]
    public int SAINCombatSquadLayerPriority = 70;

    [Description("Requires Restart. Dont touch unless you know what this is")]
    [DeveloperOption]
    [MinMax(0, 100)]
    public int SAINExtractLayerPriority = 65;

    [Description("Requires Restart. Dont touch unless you know what this is")]
    [DeveloperOption]
    [MinMax(0, 100)]
    public int SAINCombatSoloLayerPriority = 69;
}
