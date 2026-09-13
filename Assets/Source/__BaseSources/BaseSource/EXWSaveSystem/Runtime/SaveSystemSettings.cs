using System;
using System.IO;
using UnityEngine;

namespace EXW.SaveSystem
{
    [CreateAssetMenu(
        fileName = "SaveSystemSettings",
        menuName = "EXW/Save System/Settings",
        order = 0)]
    public sealed class SaveSystemSettings : ScriptableObject
    {
        [Header("Storage")]
        [SerializeField] private string folderName = "EXWSaves";
        [SerializeField, Range(1, 5)] private int backupCount = 2;
        [SerializeField] private bool compressPayload = true;

        [Header("Safety Limit")]
        [Tooltip("Rejects unexpectedly large or malicious save payloads before they can exhaust memory.")]
        [SerializeField, Min(8)] private int maximumPayloadMegabytes = 128;

        public string FolderName => folderName;
        public int BackupCount => backupCount;
        public bool CompressPayload => compressPayload;
        public int MaximumPayloadMegabytes => maximumPayloadMegabytes;

        public string GetSaveDirectory()
        {
            ValidateFolderName(folderName);
            return Path.Combine(Application.persistentDataPath, folderName);
        }

        internal SaveStorageOptions CreateStorageOptions()
        {
            return new SaveStorageOptions(
                GetSaveDirectory(),
                backupCount,
                compressPayload,
                checked(maximumPayloadMegabytes * 1024 * 1024));
        }

        internal static SaveSystemSettings CreateRuntimeDefault()
        {
            var instance = CreateInstance<SaveSystemSettings>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        private static void ValidateFolderName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                Path.IsPathRooted(value) ||
                value != Path.GetFileName(value) ||
                value == "." ||
                value == "..")
            {
                throw new InvalidOperationException(
                    "Save folder must be one safe directory name below Application.persistentDataPath.");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            backupCount = Mathf.Clamp(backupCount, 1, 5);
            maximumPayloadMegabytes = Mathf.Max(8, maximumPayloadMegabytes);
        }
#endif
    }

    internal sealed class SaveStorageOptions
    {
        public string DirectoryPath { get; }
        public int BackupCount { get; }
        public bool CompressPayload { get; }
        public int MaximumPayloadBytes { get; }

        public SaveStorageOptions(
            string directoryPath,
            int backupCount,
            bool compressPayload,
            int maximumPayloadBytes)
        {
            DirectoryPath = directoryPath;
            BackupCount = backupCount;
            CompressPayload = compressPayload;
            MaximumPayloadBytes = maximumPayloadBytes;
        }
    }
}
