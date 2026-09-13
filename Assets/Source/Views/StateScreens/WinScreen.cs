using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WinScreen : ScreenElement, IGameStateObserver
{
    [SerializeField] private TextMeshProUGUI totalEarnedText;
    [SerializeField] private Button nextLevel;

    public override void Initialize()
    {
        base.Initialize();
        AddToGameObserverList();
    }

    private void SetTotalEarnedText()
    {
        var current = 0;
        // DOTween.To(() => current, x => current = x, UserPrefs.GetTotalEarned(), 0.3f).OnUpdate(() =>
        // {
        //     totalEarnedText.text = current.ToString();
        // });
        // totalEarnedText.transform.DOScale(1.2f, 0.15f).From(1f).OnComplete(() =>
        // {
        //     totalEarnedText.transform.DOScale(1f, 0.15f);
        // });
    }

    public void AddToGameObserverList()
    {
        GameStateController.Instance.AddListener(this);
    }

    public void OnGameStateChanged()
    {
        switch (GameStateController.Instance.currentGameState)
        {
            case GameStates.Win:
                SetTotalEarnedText();
                break;
        }
    }
}