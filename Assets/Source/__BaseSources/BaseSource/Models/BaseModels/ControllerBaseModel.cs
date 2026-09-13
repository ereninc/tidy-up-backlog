public class ControllerBaseModel : ObjectModel
{
    protected GameStateController GameController => GameStateController.Instance;
    protected CameraController CameraController => CameraController.Instance;
    protected PoolFactory PoolFactory => PoolFactory.Instance;
    protected LevelController LevelController => LevelController.Instance;
    protected AudioController AudioController => AudioController.Instance;

    protected void Reset()
    {
        transform.name = GetType().Name;
        transform.ResetLocal();
    }
}