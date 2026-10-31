using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using EFT.Hideout;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
[assembly: AssemblyInformationalVersion("2.1.0")]
[assembly: AssemblyCompany("MusicManiac")]
[assembly: AssemblyProduct("Gigachad Workout")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MusicManiac")]

namespace GigachadWorkout.Client;

[BepInPlugin("com.musicmaniac.gigachadworkout", "Gigachad Workout", "2.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string ThemeName = "GigaChad Theme (2:26)";
    private const string HeartName = "Can You Feel My Heart (3:48)";
    private const float ThemeDuration = 146.39f;
    private const float HeartDuration = 228.258f;
    private const string ThemeFile = "g3ox_em - GigaChad Theme (Phonk House Version).mp3";
    private const string HeartFile = "Bring Me The Horizon - Can You Feel My Heart.mp3";
    private ConfigEntry<string>? _song;
    private ConfigEntry<float>? _volume;
    private ConfigEntry<bool>? _immediateSeverePain;
    private ConfigEntry<bool>? _fullSongWorkout;
    private AudioSource? _source;
    private AudioClip? _clip;
    private Harmony? _harmony;
    private WorkoutBehaviour? _activeWorkout;
    private int _generation;
    private float _workoutSongEndTime;
    private bool _extendCurrentWorkout;

    private void Awake()
    {
        Instance = this;
        _song = Config.Bind("Workout Music", "Song", ThemeName,
            new ConfigDescription("Song played when a gym workout starts.",
                new AcceptableValueList<string>(ThemeName, HeartName)));
        _volume = Config.Bind("Workout Music", "Volume", 50f,
            new ConfigDescription("Workout music volume, from 0 to 100.",
                new AcceptableValueRange<float>(0f, 100f)));
        _immediateSeverePain = Config.Bind("Workout", "Immediate severe muscle pain", false,
            "Apply severe muscle pain after the first gym workout, skipping mild muscle pain.");
        _fullSongWorkout = Config.Bind("Workout", "Last for full song", false,
            "Keep the gym workout going for the selected song's full duration. Applies to the next workout.");
        _song.SettingChanged += OnSongChanged;
        _volume.SettingChanged += OnVolumeChanged;

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        _source.loop = true;
        _source.volume = Mathf.Clamp01(_volume.Value / 100f);

        _harmony = new Harmony("com.musicmaniac.gigachadworkout");
        _harmony.Patch(
            AccessTools.Method(typeof(WorkoutBehaviour), "Process"),
            postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterWorkoutProcess)));
        _harmony.Patch(
            AccessTools.Method(typeof(WorkoutBehaviour), "Clear"),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeWorkoutClear)));
        _harmony.Patch(
            AccessTools.Method(typeof(HideoutAreaQTEOverlay), "Close"),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeOverlayClose)));
        _harmony.Patch(
            AccessTools.Method(typeof(OfflineHealthController), nameof(OfflineHealthController.CreateMusclePainEffect)),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeCreateMusclePainEffect)));
        _harmony.Patch(
            AccessTools.Method(typeof(QTEController), nameof(QTEController.StartQteBySequence)),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeStartQteBySequence)));
        Logger.LogInfo("Gigachad Workout loaded.");
    }

    private static void AfterWorkoutProcess(WorkoutBehaviour __instance)
    {
        if (Instance != null)
            Instance.StartWorkoutMusic(__instance);
    }

    private static void BeforeWorkoutClear(WorkoutBehaviour __instance)
    {
        if (Instance != null && Instance._activeWorkout == __instance)
            Instance.StopWorkoutMusic();
    }

    private static void BeforeOverlayClose()
    {
        if (Instance != null && Instance._activeWorkout != null)
            Instance.StopWorkoutMusic();
    }

    private static bool BeforeCreateMusclePainEffect(OfflineHealthController __instance, float duration)
    {
        if (Instance?._immediateSeverePain?.Value != true)
            return true;

        __instance.CreateMusclePain<OfflineHealthController.SevereMusclePain>(duration);
        return false;
    }

    private static void BeforeStartQteBySequence(ref IEnumerable<QuickTimeEvent> qteDatas)
    {
        if (Instance?._extendCurrentWorkout == true && Instance._activeWorkout != null)
            qteDatas = Instance.ExtendWorkoutToSongEnd(qteDatas);
    }

    private IEnumerable<QuickTimeEvent> ExtendWorkoutToSongEnd(IEnumerable<QuickTimeEvent> original)
    {
        QuickTimeEvent? repeat = null;
        foreach (QuickTimeEvent circle in original)
        {
            repeat ??= circle;
            yield return circle;
        }

        while (repeat != null && _extendCurrentWorkout && Time.unscaledTime < _workoutSongEndTime)
            yield return repeat;
    }

    private static Plugin? Instance { get; set; }

    private void StartWorkoutMusic(WorkoutBehaviour workout)
    {
        _activeWorkout = workout;
        _extendCurrentWorkout = _fullSongWorkout?.Value == true;
        _workoutSongEndTime = Time.unscaledTime + (_song?.Value == HeartName ? HeartDuration : ThemeDuration);
        PlaySelectedSong();
    }

    private void PlaySelectedSong()
    {
        StopAudio();
        if (_song == null || _activeWorkout == null)
            return;

        string file = _song.Value == HeartName ? HeartFile : ThemeFile;
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", file);
        if (!File.Exists(path))
        {
            Logger.LogWarning($"Workout song is missing: {path}");
            return;
        }

        StartCoroutine(LoadAndPlay(new Uri(path).AbsoluteUri, _generation));
    }

    private IEnumerator LoadAndPlay(string uri, int generation)
    {
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return request.SendWebRequest();
            if (generation != _generation || _activeWorkout == null)
                yield break;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Logger.LogWarning($"Workout song failed to load: {request.error}");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Logger.LogWarning("Workout song decoded to a null AudioClip.");
                yield break;
            }

            _clip = clip;
            if (_source != null)
            {
                _source.clip = clip;
                _source.volume = Mathf.Clamp01((_volume?.Value ?? 50f) / 100f);
                _source.loop = !_extendCurrentWorkout;
                if (_extendCurrentWorkout)
                    _workoutSongEndTime = Time.unscaledTime + clip.length;
                _source.Play();
            }
        }
    }

    private void OnSongChanged(object sender, EventArgs args)
    {
        if (_activeWorkout != null)
            PlaySelectedSong();
    }

    private void OnVolumeChanged(object sender, EventArgs args)
    {
        if (_source != null)
            _source.volume = Mathf.Clamp01((_volume?.Value ?? 50f) / 100f);
    }

    private void StopWorkoutMusic()
    {
        _activeWorkout = null;
        _extendCurrentWorkout = false;
        StopAudio();
    }

    private void StopAudio()
    {
        _generation++;
        if (_source != null)
        {
            _source.Stop();
            _source.clip = null;
        }
        if (_clip != null)
        {
            Destroy(_clip);
            _clip = null;
        }
    }

    private void OnDestroy()
    {
        StopWorkoutMusic();
        _harmony?.UnpatchSelf();
        Instance = null;
    }
}
