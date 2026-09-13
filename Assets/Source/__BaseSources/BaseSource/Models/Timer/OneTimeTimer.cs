using System;

public class OneTimeTimer : BaseTimer
{
    public event Action OnTimerFinished;
        
    public OneTimeTimer(float total) : base(total)
    {
    }

    public override void Tick(float deltaTime)
    {
        if (Elapsed >= Total) return;

        Elapsed += deltaTime;
            
        if (Elapsed >= Total)
        {
            OnTimerFinished?.Invoke();
        }
    }
}