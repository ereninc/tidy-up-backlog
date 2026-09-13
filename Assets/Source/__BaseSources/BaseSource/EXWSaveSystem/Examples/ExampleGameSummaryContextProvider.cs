using UnityEngine;

namespace EXW.SaveSystem.Examples
{
    public sealed class ExampleGameSummaryContextProvider :
        SaveContextBehaviour<ExampleGameSummaryContext>
    {
        [SerializeField] private ExampleGameStateController gameState;

        public override string Key => "game.summary";

        protected override ExampleGameSummaryContext Capture()
        {
            return new ExampleGameSummaryContext
            {
                Day = gameState.Day,
                Money = gameState.Money,
                LocationId = gameState.LocationId
            };
        }
    }
}
