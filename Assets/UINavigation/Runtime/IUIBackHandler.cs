namespace EXW.UI.Navigation
{
    /// <summary>Optional escape hatch for custom controls that should consume Back before their panel closes.</summary>
    public interface IUIBackHandler
    {
        bool TryHandleBack();
    }
}
