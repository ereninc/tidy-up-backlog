using System;

public abstract class BaseTimer
{
    private float _elapsed = 0f;
    protected float Total;

    public Action<float> OnElapsedChanged;

    protected BaseTimer(float total)
    {
        Total = total;
    }

    public float Elapsed
    {
        get => _elapsed;
        protected set
        {
            _elapsed = value;
            OnElapsedChanged?.Invoke(_elapsed);
        }
    }

    public abstract void Tick(float deltaTime);

    public virtual void ResetElapsed()
    {
        _elapsed = 0f;
    }
}