using System;
using UnityEngine;

namespace EXW.SaveSystem
{
    public abstract class SaveContextBehaviour<TData> : MonoBehaviour, ISaveContextProvider
        where TData : class, new()
    {
        public abstract string Key { get; }
        protected abstract TData Capture();

        object ISaveContextProvider.CaptureContext()
        {
            return Capture() ?? throw new InvalidOperationException(
                $"Save context '{Key}' returned null while capturing.");
        }

        protected virtual void Awake()
        {
            SaveProviderRegistry.Register(this);
        }

        protected virtual void OnEnable()
        {
            SaveProviderRegistry.Register(this);
        }

        protected virtual void OnDestroy()
        {
            SaveProviderRegistry.Unregister(this);
        }
    }
}
