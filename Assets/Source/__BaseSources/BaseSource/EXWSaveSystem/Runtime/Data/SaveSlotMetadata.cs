using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EXW.SaveSystem
{
    [Serializable]
    public sealed class SaveSlotMetadata
    {
        [JsonProperty("slotId", Order = 0)]
        public string SlotId { get; set; } = string.Empty;

        [JsonProperty("displayName", Order = 1)]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("sequence", Order = 2)]
        public long Sequence { get; set; }

        [JsonProperty("createdAtUnixMs", Order = 3)]
        public long CreatedAtUnixMs { get; set; }

        [JsonProperty("lastSavedAtUnixMs", Order = 4)]
        public long LastSavedAtUnixMs { get; set; }

        [JsonProperty("playTimeSeconds", Order = 5)]
        public double PlayTimeSeconds { get; set; }

        [JsonProperty("sceneName", Order = 6)]
        public string SceneName { get; set; } = string.Empty;

        [JsonProperty("gameVersion", Order = 7)]
        public string GameVersion { get; set; } = string.Empty;

        [JsonProperty("reason", Order = 8)]
        public SaveReason Reason { get; set; }

        [JsonProperty("context", Order = 9)]
        public Dictionary<string, JToken> Context { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);

        [JsonIgnore]
        public DateTimeOffset CreatedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUnixMs);

        [JsonIgnore]
        public DateTimeOffset LastSavedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(LastSavedAtUnixMs);

        public bool TryGetContext<TData>(string key, out TData value)
        {
            value = default;

            if (string.IsNullOrWhiteSpace(key) ||
                Context == null ||
                !Context.TryGetValue(key, out JToken token) ||
                token == null ||
                token.Type == JTokenType.Null)
            {
                return false;
            }

            try
            {
                value = token.ToObject<TData>();
                return true;
            }
            catch (Exception)
            {
                value = default;
                return false;
            }
        }

        internal SaveSlotMetadata DeepClone()
        {
            var clone = (SaveSlotMetadata)MemberwiseClone();
            clone.Context = new Dictionary<string, JToken>(StringComparer.Ordinal);

            if (Context == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, JToken> pair in Context)
            {
                clone.Context[pair.Key] = pair.Value?.DeepClone() ?? JValue.CreateNull();
            }

            return clone;
        }
    }
}
