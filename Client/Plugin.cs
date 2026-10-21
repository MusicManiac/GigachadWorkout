using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using EFT.Hideout;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]
[assembly: AssemblyCompany("MusicManiac")]
[assembly: AssemblyProduct("Gigachad Workout")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MusicManiac")]

namespace GigachadWorkout.Client;

[BepInPlugin("com.musicmaniac.gigachadworkout", "Gigachad Workout", "2.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string ThemeName = "GigaChad Theme (2:26)";
    private const string HeartName = "Can You Feel My Heart (3:48)";
    private const float ThemeDuration = 146.39f;
    private const float HeartDuration = 228.258f;
    private const string ThemeFile = "g3ox_em - GigaChad Theme (Phonk House Version).mp3";
    private const string HeartFile = "Bring Me The Horizon - Can You Feel My Heart.mp3";
    private const string WorkoutMode = "Workout start";
    private const string BoomboxMode = "Gym boombox";

    private ConfigEntry<string>? _song;
    private ConfigEntry<string>? _mode;
    private ConfigEntry<float>? _volume;
    private ConfigEntry<bool>? _immediateSeverePain;
    private ConfigEntry<bool>? _fullSongWorkout;
    private AudioSource? _source;
    private AudioSource? _boomboxSource;
    private AudioClip? _clip;
    private Harmony? _harmony;
    private WorkoutBehaviour? _activeWorkout;
    private AudioArray? _boomboxArray;
    private readonly Dictionary<AudioSource, bool> _originalMute = new();
    private float _nextBoomboxCheck;
    private float _nativeSilentSince = -1f;
    private int _generation;
    private float _workoutSongEndTime;
    private bool _extendCurrentWorkout;

    private void Awake()
    {
        Instance = this;
        _song = Config.Bind("Workout Music", "Song", ThemeName,
            new ConfigDescription("Song used by the selected playback mode.",
                new AcceptableValueList<string>(ThemeName, HeartName)));
        _mode = Config.Bind("Workout Music", "Playback mode", WorkoutMode,
            new ConfigDescription("Play during a workout, or loop from the gym boombox while it is on.",
                new AcceptableValueList<string>(WorkoutMode, BoomboxMode)));
        _volume = Config.Bind("Workout Music", "Volume", 50f,
            new ConfigDescription("Workout music volume, from 0 to 100.",
                new AcceptableValueRange<float>(0f, 100f)));
        _immediateSeverePain = Config.Bind("Workout", "Immediate severe muscle pain", false,
            "Apply severe muscle pain after the first gym workout, skipping mild muscle pain.");
        _fullSongWorkout = Config.Bind("Workout", "Last for full song", false,
            "Keep the gym workout going for the selected song's full duration. Applies to the next workout.");
        _song.SettingChanged += OnSongChanged;
        _mode.SettingChanged += OnModeChanged;
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

    private void Update()
    {
        if (_mode?.Value != BoomboxMode || Time.unscaledTime < _nextBoomboxCheck)
            return;

        _nextBoomboxCheck = Time.unscaledTime + 0.25f;
        if (FindObjectOfType<HideoutPlayerOwner>() == null)
        {
            if (_boomboxSource != null || _clip != null)
                StopBoomboxMusic();
            _boomboxArray = null;
            return;
        }

        if (_boomboxArray == null || !_boomboxArray.isActiveAndEnabled)
            FindGymBoombox();

        AudioSource[] nativeSources = _boomboxArray != null
            ? _boomboxArray.GetComponents<AudioSource>()
            : Array.Empty<AudioSource>();
        AudioSource? playingSource = Array.Find(nativeSources, source => source != null && source.isPlaying);
        if (playingSource == null)
        {
            if (_boomboxSource != null && _boomboxSource.isPlaying)
            {
                if (_nativeSilentSince < 0f)
                    _nativeSilentSince = Time.unscaledTime;
                if (Time.unscaledTime - _nativeSilentSince < 1f)
                    return;
            }
            if (_boomboxSource != null || _clip != null)
                StopBoomboxMusic();
            return;
        }
        _nativeSilentSince = -1f;

        if (_boomboxSource == null)
        {
            var holder = new GameObject("GigachadWorkoutBoomboxMusic");
            holder.transform.position = playingSource.transform.position;
            _boomboxSource = holder.AddComponent<AudioSource>();
            _boomboxSource.playOnAwake = false;
            _boomboxSource.loop = true;
            _boomboxSource.spatialBlend = 1f;
            _boomboxSource.minDistance = playingSource.minDistance;
            _boomboxSource.maxDistance = playingSource.maxDistance;
            _boomboxSource.rolloffMode = playingSource.rolloffMode;
            _boomboxSource.outputAudioMixerGroup = playingSource.outputAudioMixerGroup;
            PlaySelectedSong();
        }
        else
            _boomboxSource.transform.position = playingSource.transform.position;

        if (_boomboxSource != null && _boomboxSource.isPlaying)
            MuteNativeBoombox();
    }

    private void FindGymBoombox()
    {
        _boomboxArray = null;
        foreach (AudioArray array in FindObjectsOfType<AudioArray>())
        {
            if (array.gameObject.name != "BoomboxAudio" ||
                array.GetComponent<AudioSource>() == null)
                continue;

            for (Transform? parent = array.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.name.IndexOf("level1", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _boomboxArray = array;
                Logger.LogInfo($"Found gym boombox: {array.gameObject.name} under {parent.name}");
                return;
            }
        }
        _nextBoomboxCheck = Time.unscaledTime + 1f;
    }

    private void StartWorkoutMusic(WorkoutBehaviour workout)
    {
        if (_mode?.Value == WorkoutMode)
            StopAudio();
        _activeWorkout = workout;
        _extendCurrentWorkout = _fullSongWorkout?.Value == true;
        _workoutSongEndTime = Time.unscaledTime + (_song?.Value == HeartName ? HeartDuration : ThemeDuration);
        if (_mode?.Value == WorkoutMode)
            PlaySelectedSong();
    }

    private void PlaySelectedSong()
    {
        StopAudio();
        if (_song == null || (_mode?.Value == WorkoutMode && _activeWorkout == null) ||
            (_mode?.Value == BoomboxMode && _boomboxSource == null))
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
            if (generation != _generation ||
                (_mode?.Value == WorkoutMode && _activeWorkout == null) ||
                (_mode?.Value == BoomboxMode && (_boomboxSource == null ||
                    _boomboxArray == null ||
                    Array.Find(_boomboxArray.GetComponents<AudioSource>(),
                        source => source != null && source.isPlaying) == null)))
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
            AudioSource? target = _mode?.Value == BoomboxMode ? _boomboxSource : _source;
            if (target != null)
            {
                target.clip = clip;
                target.volume = Mathf.Clamp01((_volume?.Value ?? 50f) / 100f);
                if (target == _source)
                {
                    target.loop = !_extendCurrentWorkout;
                    if (_extendCurrentWorkout)
                        _workoutSongEndTime = Time.unscaledTime + clip.length;
                }
                target.Play();
                if (target == _boomboxSource)
                    MuteNativeBoombox();
            }
        }
    }

    private void OnSongChanged(object sender, EventArgs args)
    {
        if ((_activeWorkout != null && _mode?.Value == WorkoutMode) ||
            (_boomboxSource != null && _mode?.Value == BoomboxMode))
            PlaySelectedSong();
    }

    private void OnModeChanged(object sender, EventArgs args)
    {
        StopAudio();
        if (_boomboxSource != null)
        {
            Destroy(_boomboxSource.gameObject);
            _boomboxSource = null;
        }
        if (_mode?.Value == WorkoutMode && _activeWorkout != null)
            PlaySelectedSong();
        else
            _nextBoomboxCheck = 0f;
    }

    private void OnVolumeChanged(object sender, EventArgs args)
    {
        if (_source != null)
            _source.volume = Mathf.Clamp01((_volume?.Value ?? 50f) / 100f);
        if (_boomboxSource != null)
            _boomboxSource.volume = Mathf.Clamp01((_volume?.Value ?? 50f) / 100f);
    }

    private void StopWorkoutMusic()
    {
        _activeWorkout = null;
        _extendCurrentWorkout = false;
        if (_mode?.Value == WorkoutMode)
            StopAudio();
    }

    private void StopBoomboxMusic()
    {
        StopAudio();
        if (_boomboxSource != null)
        {
            Destroy(_boomboxSource.gameObject);
            _boomboxSource = null;
        }
    }

    private void StopAudio()
    {
        _generation++;
        RestoreNativeBoombox();
        if (_source != null)
        {
            _source.Stop();
            _source.clip = null;
        }
        if (_boomboxSource != null)
        {
            _boomboxSource.Stop();
            _boomboxSource.clip = null;
        }
        if (_clip != null)
        {
            Destroy(_clip);
            _clip = null;
        }
    }

    private void MuteNativeBoombox()
    {
        if (_boomboxArray == null)
            return;
        foreach (AudioSource nativeSource in _boomboxArray.GetComponents<AudioSource>())
            MuteNativeSource(nativeSource);
    }

    private void MuteNativeSource(AudioSource nativeSource)
    {
        if (nativeSource == null)
            return;
        if (!_originalMute.ContainsKey(nativeSource))
            _originalMute.Add(nativeSource, nativeSource.mute);
        nativeSource.mute = true;
    }

    private void RestoreNativeBoombox()
    {
        foreach (KeyValuePair<AudioSource, bool> entry in _originalMute)
            if (entry.Key != null)
                entry.Key.mute = entry.Value;
        _originalMute.Clear();
    }

    private void OnDestroy()
    {
        StopWorkoutMusic();
        StopBoomboxMusic();
        _harmony?.UnpatchSelf();
        Instance = null;
    }
}
