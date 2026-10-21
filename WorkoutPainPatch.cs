using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace GigachadWorkout;

internal static class WorkoutPainPatch
{
    private const string PluginConfigName = "com.musicmaniac.gigachadworkout.cfg";
    private const string OptionName = "Immediate severe muscle pain";
    private static string? _pluginConfigPath;
    private static bool _patched;

    internal static void Initialize(string modPath)
    {
        string configDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(modPath,
            "..", "..", "..", "..", "BepInEx", "config"));
        _pluginConfigPath = System.IO.Path.Combine(configDirectory, PluginConfigName);
        if (_patched)
            return;

        var method = AccessTools.Method(typeof(HideoutController), "HandleMusclePain")
            ?? throw new MissingMethodException(typeof(HideoutController).FullName, "HandleMusclePain");
        new Harmony("com.musicmaniac.gigachadworkout.pain").Patch(method,
            postfix: new HarmonyMethod(typeof(WorkoutPainPatch), nameof(AfterHandleMusclePain)));
        _patched = true;
    }

    private static void AfterHandleMusclePain(PmcData pmcData)
    {
        if (!ImmediateSeverePainEnabled())
            return;

        if (pmcData.Health?.BodyParts is not { } bodyParts ||
            !bodyParts.TryGetValue("Chest", out BodyPartHealth? chest) ||
            chest.Effects is not { } effects ||
            !effects.Remove("MildMusclePain", out BodyPartEffectProperties? pain))
            return;

        effects["SevereMusclePain"] = pain;
    }

    private static bool ImmediateSeverePainEnabled()
    {
        if (_pluginConfigPath is null || !File.Exists(_pluginConfigPath))
            return false;

        try
        {
            bool inWorkoutSection = false;
            foreach (string line in File.ReadLines(_pluginConfigPath))
            {
                string entry = line.Trim();
                if (entry.StartsWith("[", StringComparison.Ordinal))
                {
                    inWorkoutSection = string.Equals(entry, "[Workout]", StringComparison.Ordinal);
                    continue;
                }
                if (!inWorkoutSection || entry.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int equals = entry.IndexOf('=');
                if (equals >= 0 && string.Equals(entry[..equals].Trim(), OptionName, StringComparison.Ordinal))
                    return bool.TryParse(entry[(equals + 1)..].Trim(), out bool enabled) && enabled;
            }
        }
        catch (IOException)
        {
            return false;
        }
        return false;
    }
}
