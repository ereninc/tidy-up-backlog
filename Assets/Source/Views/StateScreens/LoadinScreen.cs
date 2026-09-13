using UnityEngine;

public class LoadinScreen : ScreenElement
{
    public override void Show()
    {
        base.Show();
        Invoke(nameof(OnFakeLoadComplete), Random.Range(1, 2));
    }

    private void OnFakeLoadComplete()
    {
        GameStateController.Instance.SetGameState(GameStates.Main);
    }
}
