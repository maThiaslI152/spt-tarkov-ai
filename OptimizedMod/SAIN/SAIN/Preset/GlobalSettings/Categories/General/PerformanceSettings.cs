using SAIN.Attributes;

namespace SAIN.Preset.GlobalSettings;

public class PerformanceSettings : SAINSettingsBase<PerformanceSettings>, ISAINSettings
{
    [Name("Performance Mode")]
    [Description(
        "Master toggle for all performance optimizations. When ON, all sub-settings below are active. "
            + "When OFF, bot AI runs at full quality regardless of distance or bot count."
    )]
    public bool PerformanceMode = false;

    [Name("Max AI Frame Budget (ms)")]
    [Description(
        "Milliseconds of SAIN bot ManualUpdate work allowed per Unity frame (visible/audible/occluded tiers share this cap via round-robin). "
            + "Higher values reduce skipped ticks and combat jitter at the cost of CPU. Typical 2–6."
    )]
    [MinMax(1f, 10f, 0.5f)]
    [Advanced]
    public float MaxAiBudgetMilliseconds = 2f;

    [Name("Vision Raycast Frequency")]
    [Description("How often vision raycasts update per second. Lower = less CPU, slower bot reactions. Range: 10-30 Hz")]
    [MinMax(10f, 30f, 1f)]
    [Advanced]
    public float VisionRaycastFrequency = 30f;

    [Name("Look Sensor Frequency")]
    [Description("How often the EFT look sensor updates per second. Lower = less CPU, slower target acquisition. Range: 10-30 Hz")]
    [MinMax(10f, 30f, 1f)]
    [Advanced]
    public float LookUpdateFrequency = 30f;

    [Name("Cover Finder Frequency")]
    [Description("How often bots search for cover per second when in combat. Lower = less CPU, slower cover finding. Range: 2-10 Hz")]
    [MinMax(2f, 10f, 1f)]
    [Advanced]
    public float CoverFindFrequency = 10f;

    [Name("Max Raycasts Per Body Part")]
    [Description("Number of raycast types per body part: 1=LoS only, 2=LoS+Vision, 3=LoS+Vision+Shoot. Lower = faster but may miss some checks.")]
    [MinMax(1, 3, 1)]
    [Advanced]
    public int MaxRaycastsPerEnemy = 3;

    [Name("Single-part vision beyond distance (m)")]
    [Description(
        "Beyond this distance to an enemy, `VisionRaycastJob` checks only one body part (center-of-mass style) instead of every part — saves CPU. "
            + "Does not apply when `Vision use full parts for human beyond distance` is on for human targets."
    )]
    [MinMax(50f, 500f, 5f)]
    [Advanced]
    public float VisionSinglePartBeyondDistanceMeters = 150f;

    [Name("Vision: full body parts for human beyond distance")]
    [Description(
        "When on, human (PMC/Player) targets keep full per-part raycasts beyond `Vision single-part beyond distance`. "
            + "AI-vs-AI targets still use single-part beyond that distance. Costs more CPU on open maps."
    )]
    [Advanced]
    public bool VisionUseFullPartsForHumanBeyondDistance = false;

    [Name("Far Bot CPU Reduction")]
    [Description("Multiplier applied to vision-related CPU cost for bots in the 'Far' AI limit tier. 0.5 = half cost.")]
    [MinMax(0.1f, 1f, 100f)]
    [Advanced]
    public float FarBotCpuReduction = 0.5f;

    [Name("Very Far Bot CPU Reduction")]
    [Description("Multiplier applied to vision-related CPU cost for bots in the 'VeryFar' AI limit tier.")]
    [MinMax(0.1f, 1f, 100f)]
    [Advanced]
    public float VeryFarBotCpuReduction = 0.25f;

    [Name("Narnia Bot CPU Reduction")]
    [Description("Multiplier applied to vision-related CPU cost for bots in the 'Narnia' AI limit tier. Near-zero means almost no vision cost.")]
    [MinMax(0f, 1f, 100f)]
    [Advanced]
    public float NarniaBotCpuReduction = 0f;
}
