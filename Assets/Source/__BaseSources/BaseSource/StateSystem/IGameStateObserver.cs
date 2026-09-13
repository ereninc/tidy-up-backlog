public interface IGameStateObserver
{
    void AddToGameObserverList();
    void OnGameStateChanged();
}