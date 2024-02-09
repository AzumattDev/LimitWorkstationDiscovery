using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace LimitWorkstationDiscovery
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class LimitWorkstationDiscoveryPlugin : BaseUnityPlugin
    {
        internal const string ModName = "LimitWorkstationDiscovery";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "Azumatt";
        private const string ModGUID = $"{Author}.{ModName}";
        private static string ConfigFileName = $"{ModGUID}.cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource LimitWorkstationDiscoveryLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
            DiscoveryRange = config("1 - General", "Discovery Range", 0.0f, "The range in which the player can discover objects with the CraftingStation component. Default is 0 to force interaction with the object to learn it first. Vanilla default is 4");
            DiscoveryRange.SettingChanged += (_, _) =>
            {
                if (Player.m_localPlayer == null) return;
                foreach (CraftingStation craftingStation in CraftingStation.m_allStations)
                {
                    craftingStation.m_discoverRange = DiscoveryRange.Value;
                }
            };


            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                LimitWorkstationDiscoveryLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                LimitWorkstationDiscoveryLogger.LogError($"There was an issue loading your {ConfigFileName}");
                LimitWorkstationDiscoveryLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        internal static ConfigEntry<float> DiscoveryRange = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        #endregion
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Start))]
    static class CraftingStationInUseDistancePatch
    {
        static void Prefix(CraftingStation __instance)
        {
            __instance.m_discoverRange = LimitWorkstationDiscoveryPlugin.DiscoveryRange.Value;
        }
    }
}