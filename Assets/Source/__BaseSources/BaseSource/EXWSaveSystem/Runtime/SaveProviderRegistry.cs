using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EXW.SaveSystem
{
    public static class SaveProviderRegistry
    {
        private static readonly Dictionary<string, ISaveSection> Sections =
            new Dictionary<string, ISaveSection>(StringComparer.Ordinal);

        private static readonly Dictionary<string, ISaveContextProvider> ContextProviders =
            new Dictionary<string, ISaveContextProvider>(StringComparer.Ordinal);

        public static IReadOnlyList<ISaveSection> GetSections()
        {
            RemoveDestroyedProviders();
            return Sections.Values.OrderBy(section => section.Key, StringComparer.Ordinal).ToArray();
        }

        public static IReadOnlyList<ISaveContextProvider> GetContextProviders()
        {
            RemoveDestroyedProviders();
            return ContextProviders.Values.OrderBy(provider => provider.Key, StringComparer.Ordinal).ToArray();
        }

        public static void Register(ISaveSection section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            SaveKeyValidator.RequireProviderKey(section.Key);

            if (section.CurrentVersion < 1)
            {
                throw new InvalidOperationException($"Section '{section.Key}' has an invalid version.");
            }

            if (Sections.TryGetValue(section.Key, out ISaveSection existing) &&
                !ReferenceEquals(existing, section))
            {
                throw new InvalidOperationException(
                    $"Two save sections use the key '{section.Key}': " +
                    $"{existing.GetType().Name} and {section.GetType().Name}.");
            }

            Sections[section.Key] = section;
        }

        public static void Unregister(ISaveSection section)
        {
            if (section != null &&
                Sections.TryGetValue(section.Key, out ISaveSection existing) &&
                ReferenceEquals(existing, section))
            {
                Sections.Remove(section.Key);
            }
        }

        public static void Register(ISaveContextProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            SaveKeyValidator.RequireProviderKey(provider.Key);

            if (ContextProviders.TryGetValue(provider.Key, out ISaveContextProvider existing) &&
                !ReferenceEquals(existing, provider))
            {
                throw new InvalidOperationException(
                    $"Two save context providers use the key '{provider.Key}': " +
                    $"{existing.GetType().Name} and {provider.GetType().Name}.");
            }

            ContextProviders[provider.Key] = provider;
        }

        public static void Unregister(ISaveContextProvider provider)
        {
            if (provider != null &&
                ContextProviders.TryGetValue(provider.Key, out ISaveContextProvider existing) &&
                ReferenceEquals(existing, provider))
            {
                ContextProviders.Remove(provider.Key);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Sections.Clear();
            ContextProviders.Clear();
        }

        private static void RemoveDestroyedProviders()
        {
            string[] deadSectionKeys = Sections
                .Where(pair => IsDestroyedUnityObject(pair.Value))
                .Select(pair => pair.Key)
                .ToArray();

            for (int i = 0; i < deadSectionKeys.Length; i++)
            {
                Sections.Remove(deadSectionKeys[i]);
            }

            string[] deadContextKeys = ContextProviders
                .Where(pair => IsDestroyedUnityObject(pair.Value))
                .Select(pair => pair.Key)
                .ToArray();

            for (int i = 0; i < deadContextKeys.Length; i++)
            {
                ContextProviders.Remove(deadContextKeys[i]);
            }
        }

        private static bool IsDestroyedUnityObject(object value)
        {
            return value is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}
