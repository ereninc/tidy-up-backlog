using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EXW.SaveSystem
{
    [Serializable]
    public sealed class SaveDocument
    {
        public const int CurrentFormatVersion = 1;

        [JsonProperty("formatVersion", Order = 0)]
        public int FormatVersion { get; set; } = CurrentFormatVersion;

        [JsonProperty("sections", Order = 1)]
        public Dictionary<string, SaveSectionRecord> Sections { get; set; } =
            new Dictionary<string, SaveSectionRecord>(StringComparer.Ordinal);

        internal SaveDocument DeepClone()
        {
            var clone = new SaveDocument
            {
                FormatVersion = FormatVersion,
                Sections = new Dictionary<string, SaveSectionRecord>(StringComparer.Ordinal)
            };

            if (Sections == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, SaveSectionRecord> pair in Sections)
            {
                clone.Sections[pair.Key] = pair.Value?.DeepClone();
            }

            return clone;
        }
    }

    [Serializable]
    public sealed class SaveSectionRecord
    {
        [JsonProperty("version", Order = 0)]
        public int Version { get; set; } = 1;

        [JsonProperty("data", Order = 1)]
        public JToken Data { get; set; } = new JObject();

        internal SaveSectionRecord DeepClone()
        {
            return new SaveSectionRecord
            {
                Version = Version,
                Data = Data?.DeepClone() ?? JValue.CreateNull()
            };
        }
    }
}
