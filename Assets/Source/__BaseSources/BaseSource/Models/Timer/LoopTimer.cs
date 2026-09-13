using System;

public class LoopTimer : BaseTimer
{
    public event Action OnSingleLoopFinished;
        
    public LoopTimer(float total) : base(total)
    {
    }

    public override void Tick(float deltaTime)
    {
        if (Elapsed <= Total)
        {
            Elapsed += deltaTime;
        }
        else
        {
            Elapsed = 0f;
            OnSingleLoopFinished?.Invoke();
        }
    }
}