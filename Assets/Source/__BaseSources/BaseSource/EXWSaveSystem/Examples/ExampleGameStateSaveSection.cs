using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EXW.SaveSystem.Examples
{
    public sealed class ExampleGameStateSaveSection : SaveSectionBehaviour<ExampleGameStateData>
    {
        private static readonly IReadOnlyList<ISaveMigration> SectionMigrations =
            new ISaveMigration[] { new VersionOneToTwoMigration() };

        [SerializeField] private ExampleGameStateController gameState;

        public override string Key => "game.state";
        public override int CurrentVersion => 2;
        public override IReadOnlyList<ISaveMigration> Migrations => SectionMigrations;

        protected override ExampleGameStateData Capture()
        {
            return gameState.CaptureState();
        }

        protected override void Restore(ExampleGameStateData data)
        {
            gameState.RestoreState(data);
        }

        private sealed class VersionOneToTwoMigration : ISaveMigration
        {
            public int FromVersion => 1;
            public int ToVersion => 2;

            public JToken Migrate(JToken source)
            {
                var data = (JObject)source.DeepClone();

                if (data["locationId"] == null)
                {
                    data["locationId"] = "laundromat";
                }

                return data;
            }
        }
    }
}
