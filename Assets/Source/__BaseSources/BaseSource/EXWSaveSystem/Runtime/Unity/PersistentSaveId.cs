using System;
using UnityEngine;

namespace EXW.SaveSystem
{
    [DisallowMultipleComponent]
    public sealed class PersistentSaveId : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string value;

        public string Value
        {
            get
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"PersistentSaveId on '{name}' has not been assigned.");
                }

                return value;
            }
        }

        public void AssignRuntimeValue(string runtimeValue)
        {
            if (string.IsNullOrWhiteSpace(runtimeValue))
            {
                throw new ArgumentException("Runtime persistent id cannot be empty.", nameof(runtimeValue));
            }

            value = runtimeValue;
        }

        public void GenerateRuntimeValue()
        {
            value = Guid.NewGuid().ToString("N");
        }

#if UNITY_EDITOR
        private void Reset()
        {
            RegenerateEditorValue();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RegenerateEditorValue();
            }
        }

        internal void RegenerateEditorValue()
        {
            value = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
