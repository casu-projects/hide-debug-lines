using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using MpHideDebugLine.Patches;
using UnityEngine;

namespace MpHideDebugLine
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log => Instance != null ? Instance.Logger : null;

        public static ConfigEntry<bool> Enabled;

        public static ConfigEntry<bool> HideOverlay;

        public static ConfigEntry<float> ToastDurationSeconds;
        public static ConfigEntry<float> FadeSeconds;
        public static ConfigEntry<int> MaxVisible;
        public static ConfigEntry<float> MaxWidth;
        public static ConfigEntry<bool> ShowInGame;
        public static ConfigEntry<bool> ShowInMenus;

        public static ConfigEntry<KeyCode> CenterToggleKey;
        public static ConfigEntry<float> DimAmount;
        public static ConfigEntry<float> PanelWidthRatio;
        public static ConfigEntry<float> CenterMargin;
        public static ConfigEntry<float> SlideDuration;
        public static ConfigEntry<int> MaxHistory;

        private Harmony _harmony;
        private ToastRenderer _renderer;
        private readonly ToastQueue _queue = new ToastQueue();

        private float _loadTime;
        private bool _bootMessageRecovered;

        public static bool CenterOpen { get; internal set; }

        // The EventSystem disabled while open; kept by reference so it restores exactly.
        private UnityEngine.EventSystems.EventSystem _blockedEventSystem;

        // Delays Center close until LateUpdate so the frame's ESC handling is blocked.
        private bool _pendingCloseCenter;

        private void Awake()
        {
            Instance = this;
            InitConfig();

            // Renderer is created lazily (not in Awake) because boot cleanup destroys it.
            CenterOpen = false;

            try
            {
                _harmony = new Harmony(PluginInfo.GUID);
                _harmony.PatchAll();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Harmony patch failed: {ex}");
            }

            // Registers the Message Center keybind in the game's built-in Input tab.
            try
            {
                KrokoshaCasualtiesUtils.Util.RegisterKeybind(MpHideDebugLineKeybind.Name, KeyCode.RightBracket);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Keybind registration failed: {ex}");
            }

            InjectSettingLabels();

            _loadTime = Time.realtimeSinceStartup;
            Logger.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded.");
        }

        // Injects the "Hide Message Toasts" label (Lang.EN accessed via reflection).
        private void InjectSettingLabels()
        {
            try
            {
                var field = typeof(KrokoshaCasualtiesMP.Lang).GetField(
                    "EN",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var en = field?.GetValue(null) as System.Collections.Generic.Dictionary<string, string>;
                if (en == null)
                    return;

                en["setting_ui_hidemessagetoasts"] = "Hide Message Toasts";
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Setting label injection failed: {ex}");
            }
        }

        private void InitConfig()
        {
            ResetConfigIfVersionChanged();

            Enabled = Config.Bind("General", "Enabled", true, "Master toggle.");
            HideOverlay = Config.Bind("Overlay", "HideOverlay", true, "Hide the mod's top-left debug overlay.");
            ToastDurationSeconds = Config.Bind("Toast", "Duration", 4f, "Toast display duration (seconds).");
            FadeSeconds = Config.Bind("Toast", "FadeSeconds", 0.35f, "Fade in/out duration (seconds).");
            MaxVisible = Config.Bind("Toast", "MaxVisible", 8, "Max toasts shown at once.");
            MaxWidth = Config.Bind("Toast", "MaxWidth", 400f, "Toast max width cap (px).");
            ShowInGame = Config.Bind("Toast", "ShowInGame", true, "Show toasts while in the world.");
            ShowInMenus = Config.Bind("Toast", "ShowInMenus", true, "Show toasts while in menus.");

            CenterToggleKey = Config.Bind("Center", "ToggleKey", KeyCode.RightBracket, "Status Messages Bar toggle key fallback (default ]). The key set in the game's controls tab takes priority.");
            DimAmount = Config.Bind("Center", "DimAmount", 0.5f, "Screen dim while Status Messages Bar is open (0~1).");
            PanelWidthRatio = Config.Bind("Center", "PanelWidthRatio", 0.3f, "Status Messages Bar panel width ratio (0~1 of screen width).");
            CenterMargin = Config.Bind("Center", "Margin", 16f, "Status Messages Bar top/bottom/right margin (px).");
            SlideDuration = Config.Bind("Center", "SlideDuration", 0.3f, "Status Messages Bar slide in/out duration (seconds).");
            MaxHistory = Config.Bind("Center", "MaxHistory", 200, "Max messages kept in Status Messages Bar.");

            // Record the version marker so user settings persist across sessions.
            Config.Bind("General", "ConfigVersion", ConfigVersion, "Config version marker (internal).");
        }

        private const int ConfigVersion = 7;

        // Resets config once when the cfg version marker changes, to apply new defaults.
        private void ResetConfigIfVersionChanged()
        {
            try
            {
                int stored = 0;
                try
                {
                    if (Config.TryGetEntry("General", "ConfigVersion", out BepInEx.Configuration.ConfigEntry<int> found)
                        && found != null)
                    {
                        stored = found.Value;
                    }
                }
                catch
                {
                    stored = 0;
                }

                if (stored != ConfigVersion)
                {
                    Config.Clear();
                    Logger?.LogInfo("Config version changed; reset to defaults.");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"Config version check failed: {ex}");
            }
        }

        private void Update()
        {
            if (!Enabled.Value)
                return;

            TryRecoverBootMessage();
            HandleNotificationCenterToggle();
            HandleEventSystemBlock();
        }

        // While open, disables the uGUI HUD EventSystem to block clicks.
        private void HandleEventSystemBlock()
        {
            if (CenterOpen)
            {
                if (_blockedEventSystem == null)
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null)
                    {
                        es.enabled = false;       // OnDisable nulls EventSystem.current
                        _blockedEventSystem = es; // keep the exact instance
                    }
                }
            }
            else if (_blockedEventSystem != null)
            {
                var es = _blockedEventSystem;
                _blockedEventSystem = null;
                if (es != null && (bool)es) // scene-change / fake-null guard
                {
                    es.enabled = true;
                    es.SetSelectedGameObject(null); // defensive: clear leftover selection
                }
            }
        }

        private void HandleNotificationCenterToggle()
        {
            // Use the settings-bound key, falling back to cfg if unbound.
            KeyCode toggle = MpHideDebugLineKeybind.GetBoundKey();
            if (toggle == KeyCode.None)
                toggle = CenterToggleKey.Value;

            if (toggle != KeyCode.None && Input.GetKeyDown(toggle))
            {
                CenterOpen = !CenterOpen;
                return;
            }

            // Esc close delayed until LateUpdate so the frame's ESC handling is blocked.
            if (CenterOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                _pendingCloseCenter = true;
            }
        }

        private void LateUpdate()
        {
            if (_pendingCloseCenter)
            {
                _pendingCloseCenter = false;
                CenterOpen = false;
            }
        }

        // Recovers the pre-load status message as a toast after a short delay.
        private void TryRecoverBootMessage()
        {
            if (_bootMessageRecovered)
                return;

            if (Time.realtimeSinceStartup - _loadTime < 3f)
                return;

            _bootMessageRecovered = true;

            try
            {
                string msg = KrokoshaCasualtiesMP.KrokoshaScavMultiplayer.multiplayer_status_message;
                double changeTime = KrokoshaCasualtiesMP.KrokoshaScavMultiplayer.last_multiplayer_status_message_change_time;

                if (!string.IsNullOrEmpty(msg)
                    && !string.Equals(msg, "Game initialized.", StringComparison.Ordinal)
                    && Time.realtimeSinceStartupAsDouble - changeTime < 30d)
                {
                    EnqueueStatusMessage(msg, false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Boot message recovery failed: {ex}");
            }
        }

        private void EnsureRenderer()
        {
            if (_renderer != null && (bool)_renderer)
                return;

            _renderer = null;
            try
            {
                _renderer = ToastRenderer.Create();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Renderer create failed: {ex}");
                _renderer = null;
            }
        }

        public ToastRenderer GetRenderer()
        {
            EnsureRenderer();
            return _renderer;
        }

        public void EnqueueStatusMessage(string msg, bool isError)
        {
            if (!Enabled.Value)
                return;
            _queue.Show(msg, isError);
        }
    }
}