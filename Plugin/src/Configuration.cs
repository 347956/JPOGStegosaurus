using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using Unity.Collections;

namespace JPOGStegosaurus.Configuration {
    [Serializable]
    public class PluginConfig : SyncedInstance<PluginConfig>
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        //public ConfigEntry<int> SpawnWeight;

        public ConfigEntry<int> MaxIrritationLevel;
        public ConfigEntry<int> IntervalIrrtationDecrement;
        public ConfigEntry<int> IntervalIrrtationIncrement;
        public ConfigEntry<int> IntervalIdling;

        public ConfigEntry<int> DecreaseAmountIrritation;
        public ConfigEntry<int> IncreaseAmountIrritation;


        public ConfigEntry<bool> EnableJPOGStegoSaurus;
        public ConfigEntry<string> StegoSaurusRarity;

        private const string CATEGORY_GENERAL = "1. General";
        private const string CATEGORY_BEHAVIOR = "2. Behavior";
        private const string CATEGORY_SPAWNING_MOONS = "3. Spawning - Moons";


        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public PluginConfig(ConfigFile cfg)
        {
            InitInstance(this);


            EnableJPOGStegoSaurus = cfg.Bind(CATEGORY_GENERAL, "Enable StegoSaurus", true, "Use this option to Enable/Disable the StegoSaurus being able to spawn in game.");

            StegoSaurusRarity = cfg.Bind(CATEGORY_SPAWNING_MOONS,
                                                "StegoSaurus | Spawn Weight.",
                                                "Modded:20,ExperimentationLevel:20,AssuranceLevel:20,VowLevel:30,OffenseLevel:20,MarchLevel:30,RendLevel:10,DineLevel:10,TitanLevel:10,Adamance:20,Embrion:10,Artifice:40,Auralis:20",
                                                "Spawn Weight of the Stegosaurus on all moons, Feel free to add any moon to the list, just follow the format (also needs LLL installed for LE moons to work with this config).\n" +
                                                "Format Example: \"MyCustomLevel:20,OrionLevel:100,Etc:20,EtcTwo:20,EtcThree:20\"");

/*            SpawnWeight = cfg.Bind(CATEGORY_GENERAL, "Spawn weight", 20,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");
*/
            IntervalIrrtationDecrement = cfg.Bind(CATEGORY_BEHAVIOR, "Interval Irrtation Decrement", 5,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            IntervalIrrtationIncrement = cfg.Bind(CATEGORY_BEHAVIOR, "Interval Irrtation Decrement", 2,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            IntervalIdling = IntervalIrrtationIncrement = cfg.Bind(CATEGORY_BEHAVIOR, "Interval Idling", 30,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            MaxIrritationLevel = cfg.Bind(CATEGORY_BEHAVIOR, "Max Irritation Level", 100,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            DecreaseAmountIrritation = cfg.Bind(CATEGORY_BEHAVIOR, "Decrease Amount Irritation", 5,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            IncreaseAmountIrritation= cfg.Bind(CATEGORY_BEHAVIOR, "Increase Amount Irritation", 10,
                "The spawn chance weight for JPOGStegosaurus, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg) {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }

        public static void RequestSync()
        {
            if (!IsClient) return;

            using FastBufferWriter stream = new(IntSize, Allocator.Temp);
            MessageManager.SendNamedMessage("ModName_OnRequestConfigSync", 0uL, stream);
        }

        public static void OnRequestSync(ulong clientId, FastBufferReader _)
        {
            if (!IsHost) return;

            Plugin.Logger.LogInfo($"Config sync request received from client: {clientId}");

            byte[] array = SerializeToBytes(Instance);
            int value = array.Length;

            using FastBufferWriter stream = new(value + IntSize, Allocator.Temp);

            try
            {
                stream.WriteValueSafe(in value, default);
                stream.WriteBytesSafe(array);

                MessageManager.SendNamedMessage("ModName_OnReceiveConfigSync", clientId, stream);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogInfo($"Error occurred syncing config with client: {clientId}\n{e}");
            }
        }

        public static void OnReceiveSync(ulong _, FastBufferReader reader)
        {
            if (!reader.TryBeginRead(IntSize))
            {
                Plugin.Logger.LogError("Config sync error: Could not begin reading buffer.");
                return;
            }

            reader.ReadValueSafe(out int val, default);
            if (!reader.TryBeginRead(val))
            {
                Plugin.Logger.LogError("Config sync error: Host could not sync.");
                return;
            }

            byte[] data = new byte[val];
            reader.ReadBytesSafe(ref data, val);

            SyncInstance(data);

            Plugin.Logger.LogInfo("Successfully synced config with host.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        public static void InitializeLocalPlayer()
        {
            if (IsHost)
            {
                MessageManager.RegisterNamedMessageHandler("JPOGStegosaurus_OnRequestConfigSync", OnRequestSync);
                Synced = true;

                return;
            }

            Synced = false;
            MessageManager.RegisterNamedMessageHandler("JPOGStegosaurus_OnReceiveConfigSync", OnReceiveSync);
            RequestSync();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        public static void PlayerLeave()
        {
            RevertSync();
        }
    }
}