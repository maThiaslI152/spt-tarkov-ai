using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace SAIN.Interop;

/// <summary>
/// Reads optional SAINPerfLog settings without a project reference (avoids circular dependency).
/// </summary>
public static class SainPerfLogInterop
{
    private const string PerfLogGuid = "me.sol.sain.perflog";
    private const string DiagnosticFieldName = "DiagnosticLoggingEnabled";
    private const string VerboseSamplingFieldName = "BigBrainDiagVerboseSampling";
    private static FieldInfo _diagnosticField;
    private static FieldInfo _verboseSamplingField;

    public static bool IsDiagnosticLoggingEnabled
    {
        get
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(PerfLogGuid, out var info) || info?.Instance == null)
                {
                    return false;
                }

                Type pluginType = info.Instance.GetType();
                if (_diagnosticField == null || _diagnosticField.DeclaringType != pluginType)
                {
                    _diagnosticField = pluginType.GetField(
                        DiagnosticFieldName,
                        BindingFlags.Public | BindingFlags.Static);
                }

                if (_diagnosticField == null)
                {
                    return false;
                }

                return _diagnosticField.GetValue(null) is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool IsBigBrainVerboseSamplingEnabled
    {
        get
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(PerfLogGuid, out var info) || info?.Instance == null)
                {
                    return false;
                }

                Type pluginType = info.Instance.GetType();
                if (_verboseSamplingField == null || _verboseSamplingField.DeclaringType != pluginType)
                {
                    _verboseSamplingField = pluginType.GetField(
                        VerboseSamplingFieldName,
                        BindingFlags.Public | BindingFlags.Static);
                }

                if (_verboseSamplingField == null)
                {
                    return false;
                }

                return _verboseSamplingField.GetValue(null) is bool enabled && enabled;
            }
            catch
            {
                return false;
            }
        }
    }
}
