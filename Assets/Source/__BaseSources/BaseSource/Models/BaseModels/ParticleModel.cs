using System;
using System.Collections;
using System.Collections.Generic;
using MEC;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ParticleModel : TransformObject
{
    public ParticleSystem Particle;
    [SerializeField] private float deactivateTime;

    public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
    {
        SetActive();
        Transform.position = position;
        Transform.rotation = rotation;
        Particle.Play();
        Timing.CallDelayed(deactivateTime, OnDeactivate);
    }

    private void OnDeactivate()
    {
        Particle.Stop();
        gameObject.SetActive(false);
    }

    private void Reset()
    {
        if (Particle == null)
        {
            Particle = GetComponent<ParticleSystem>();
            var main = Particle.main;
            main.stopAction = ParticleSystemStopAction.Callback;
        }
    }
}
