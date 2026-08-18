// FILE: BA_Tools/Warnings/Settings/WarningsDashboardSettings.cs
using System;
using System.Collections.Generic;
using BA.Warnings.Models;

namespace BA.Warnings.Settings
{
    // ASSUMPTION FLAGGED, unchanged from earlier: AppSettingsBase.Load<T>() / .Save()
    // signature still unconfirmed against real source.
    public sealed class WarningsDashboardSettings : BA.Settings.AppSettingsBase
    {
        public List<JoinFailureResolutionRule> JoinResolutionRules { get; set; } = new List<JoinFailureResolutionRule>();

        // Guards the one time default seed below so it never re runs and stomps
        // rules you've since edited or deliberately set back to Ignore.
        public bool HasSeededDefaultJoinRules { get; set; } = false;

        protected override string SubFolder => "Warnings";
        protected override string FileName => "WarningsDashboardSettings.json";

        // Confirmed against BuiltInFailures.JoinElementsFailures in Revit 2026,
        // reflected 2026-08-17 against a live install, not guessed:
        //   JoiningDisjointWarn (1b9dacf3-db22-45d5-b071-42516278ffb1)
        //     confirmed live, description "Highlighted elements are joined but
        //     do not intersect." Unjoin clears the stale relationship.
        //   JoiningDisjoint (8e360a35-f8c2-40b1-9655-00b3d5041ea0)
        //     same name pattern, Error severity twin, not seen live but the
        //     name is unambiguous enough to seed the same way.
        // CannotKeepJoined (fe859f1c-28f4-4550-830a-292c56f52baf) deliberately
        // NOT seeded, semantically plausible but unconfirmed, stays Ignore
        // until you've either seen it fire live or verified what it means.
        public void SeedDefaultJoinRulesIfNeeded()
        {
            if (HasSeededDefaultJoinRules) return;

            void AddIfMissing(Guid guid, string displayName)
            {
                if (JoinResolutionRules.Exists(r => r.FailureDefinitionGuid == guid)) return;

                JoinResolutionRules.Add(new JoinFailureResolutionRule
                {
                    FailureDefinitionGuid = guid,
                    DisplayName = displayName,
                    Action = JoinResolutionAction.Unjoin
                });
            }

            AddIfMissing(new Guid("1b9dacf3-db22-45d5-b071-42516278ffb1"), "Highlighted elements are joined but do not intersect.");
            AddIfMissing(new Guid("8e360a35-f8c2-40b1-9655-00b3d5041ea0"), "JoiningDisjoint (Error severity)");

            HasSeededDefaultJoinRules = true;
            Save();
        }
    }
}