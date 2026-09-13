using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EXW.SaveSystem
{
    public abstract class SaveSectionBehaviour<TData> : MonoBehaviour, ISaveSection
        where TData : class, new()
    {
        private static readonly IReadOnlyList<ISaveMigration> NoMigrations =
            Array.Empty<ISaveMigration>();

        public abstract string Key { get; }
        public virtual int CurrentVersion => 1;
        public virtual int RestoreOrder => 0;
        public virtual IReadOnlyList<ISaveMigration> Migrations => NoMigrations;

        protected abstract TData Capture();
        protected abstract void Restore(TData data);
        protected virtual TData CreateDefault() => new TData();

        object ISaveSection.CaptureState()
        {
            return Capture() ?? throw new InvalidOperationException(
                $"Save section '{Key}' returned null while capturing.");
        }

        object ISaveSection.CreateDefaultState()
        {
            return CreateDefault() ?? throw new InvalidOperationException(
                $"Save section '{Key}' returned a null default state.");
        }

        object ISaveSection.DeserializeState(JToken token, ISaveSerializer serializer)
        {
            string json = token?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
            return serializer.Deserialize(json, typeof(TData));
        }

        void ISaveSection.RestoreState(object state)
        {
            if (state is not TData typedState)
            {
                throw new InvalidOperationException(
                    $"Save section '{Key}' received {state?.GetType().Name ?? "null"}, " +
                    $"expected {typeof(TData).Name}.");
            }

            Restore(typedState);
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
