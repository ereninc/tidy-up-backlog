using System;
using UnityEngine;

public class OneTimeCountdown : BaseTimer
{
    public event Action OnTimerFinished;
    public OneTimeCountdown(float total) : base(total)
    {
        SetTotal(total);
    }

    public void NotifyOnTimerFinished()
    {
        OnTimerFinished?.Invoke();
    }

    public override void Tick(float deltaTime)
    {
        Elapsed -= Time.deltaTime;
    }

    public void SetTotal(float t)
    {
        Elapsed = t;
    }

    public void IncreaseTotal(float t)
    {
        Elapsed += t;
    }

    public bool Completed()
    {
        return Elapsed <= 0;
    }
}