using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace JPOGStegosaurus.Configuration {
    public class PluginConfig
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        // The Stegosaurus logic only runs on the host, so these settings do not have to be synced to clients.
        private const string CATEGORY_BEHAVIOR = "Behavior";

        public ConfigEntry<int> MaxIrritationLevel;
        public ConfigEntry<int> IntervalIrrtationDecrement;
        public ConfigEntry<int> IntervalIrrtationIncrement;
        public ConfigEntry<int> DecreaseAmountIrritation;
        public ConfigEntry<int> IncreaseAmountIrritation;

        public PluginConfig(ConfigFile cfg)
        {
            IntervalIrrtationDecrement = cfg.Bind(CATEGORY_BEHAVIOR, "Interval Irrtation Decrement", 5,
                "The time interval (in seconds) required for the Stegosaurus to decrease its irritation level.");

            IntervalIrrtationIncrement = cfg.Bind(CATEGORY_BEHAVIOR, "Interval Irrtation Increment", 2,
                "The time interval (in seconds) required for the Stegosaurus to increase its irritation level.");

            MaxIrritationLevel = cfg.Bind(CATEGORY_BEHAVIOR, "Max Irritation Level", 100,
                "The max threshold for the irritation level of the Stegosaurus. Once this threshold is reached, the Stegosaurus will start chasing the player.");

            DecreaseAmountIrritation = cfg.Bind(CATEGORY_BEHAVIOR, "Decrease Amount Irritation", 5,
                "The Stegosaurus will decrease its irritation level by this amount every after enough time has passed (Interval Irrtation Decrement)");

            IncreaseAmountIrritation = cfg.Bind(CATEGORY_BEHAVIOR, "Increase Amount Irritation", 10,
                "The amount by which the Stegosaurus increases its irritation level when the player is near it");

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg) {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }
    }
}
