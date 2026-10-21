using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]
[assembly: AssemblyCompany("MusicManiac")]
[assembly: AssemblyProduct("Gigachad Workout")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MusicManiac")]

namespace GigachadWorkout;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1000)]
public sealed class GigachadWorkout : IOnLoad
{
    private readonly ISptLogger<GigachadWorkout> _logger;
    private readonly HideoutTable _hideout;
    private readonly WorkoutConfig _config;
    private readonly string _modPath;

    public GigachadWorkout(ISptLogger<GigachadWorkout> logger, HideoutTable hideout, ModHelper modHelper)
    {
        _logger = logger;
        _hideout = hideout;
        _modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _config = JsonSerializer.Deserialize<WorkoutConfig>(File.ReadAllText(Path.Combine(_modPath, "config.json")))
            ?? throw new InvalidOperationException("Workout config could not be loaded.");
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (_config.ExtraCircles < 0)
            throw new InvalidOperationException("extraCircles must be nonnegative.");

        WorkoutPainPatch.Initialize(_modPath);

        QteData workout = _hideout.Qte.FirstOrDefault(qte => qte.Area == HideoutAreas.Gym)
            ?? throw new InvalidOperationException("Gym workout QTE was not loaded.");
        List<QuickTimeEvent> circles = workout.QuickTimeEvents
            ?? throw new InvalidOperationException("Gym workout circles were not loaded.");
        if (circles.Count == 0)
            throw new InvalidOperationException("Gym workout has no circles to copy.");

        QuickTimeEvent first = circles[0];
        if (_config.MakeAllCirclesSameAsFirst)
        {
            for (int i = 1; i < circles.Count; i++)
                circles[i] = first with { };
        }
        circles.InsertRange(0, Enumerable.Range(0, _config.ExtraCircles).Select(_ => first with { }));

        Dictionary<QteEffectType, QteResult> results = workout.Results
            ?? throw new InvalidOperationException("Gym workout results were not loaded.");
        QteResult finish = results[QteEffectType.finishEffect];
        QteEffect musclePain = finish.RewardEffects?.FirstOrDefault(effect => effect.Type == QteRewardType.MusclePain)
            ?? throw new InvalidOperationException("Gym muscle pain reward was not loaded.");
        musclePain.Time = _config.MusclePainDuration;

        if (_config.DisableFracture)
        {
            List<QteEffect> failEffects = results[QteEffectType.singleFailEffect].RewardEffects
                ?? throw new InvalidOperationException("Gym failure rewards were not loaded.");
            failEffects.Clear();
        }

        List<QteEffect> successEffects = results[QteEffectType.singleSuccessEffect].RewardEffects
            ?? throw new InvalidOperationException("Gym skill rewards were not loaded.");
        foreach (QteEffect reward in successEffects.Where(effect => effect.LevelMultipliers is not null))
        {
            if (_config.SameExpRegardlessOfSkillLevel)
                reward.LevelMultipliers = [new SkillLevelMultiplier { Level = 0, MultiplierValue = 6 }];

            foreach (SkillLevelMultiplier multiplier in reward.LevelMultipliers!)
                multiplier.MultiplierValue *= _config.ExpMultiplier;
        }

        _logger.Success($"[GigachadWorkout] Updated gym workout with {_config.ExtraCircles} extra circles.");
        return Task.CompletedTask;
    }
}

public sealed class WorkoutConfig
{
    [JsonPropertyName("extraCircles")]
    public int ExtraCircles { get; set; } = 5;
    [JsonPropertyName("makeAllCirclesSameAsFirst")]
    public bool MakeAllCirclesSameAsFirst { get; set; } = true;
    [JsonPropertyName("disableFracture")]
    public bool DisableFracture { get; set; } = true;
    [JsonPropertyName("musclePainDuration")]
    public long MusclePainDuration { get; set; } = 86400;
    [JsonPropertyName("sameExpRegardlessOfSkillLevel")]
    public bool SameExpRegardlessOfSkillLevel { get; set; } = true;
    [JsonPropertyName("expMultiplier")]
    public float ExpMultiplier { get; set; } = 3;
}

public sealed record GigachadWorkoutModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.musicmaniac.gigachadworkout";
    public string Name { get; init; } = "Gigachad Workout";
    public string Author { get; init; } = "MusicManiac";
    public List<string>? Contributors { get; init; } = ["MusicManiac"];
    public SemanticVersioning.Version Version { get; init; } = new("2.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; } = [];
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = [];
    public string? Url { get; init; } = "https://github.com/MusicManiac/GigachadWorkout";
    public string License { get; init; } = "MIT";
}
