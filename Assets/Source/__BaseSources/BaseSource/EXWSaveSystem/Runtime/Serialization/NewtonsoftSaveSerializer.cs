using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace EXW.SaveSystem
{
    public sealed class NewtonsoftSaveSerializer : ISaveSerializer
    {
        private readonly JsonSerializerSettings _compactSettings;
        private readonly JsonSerializerSettings _prettySettings;

        public NewtonsoftSaveSerializer()
        {
            var contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };

            _compactSettings = CreateSettings(contractResolver, Formatting.None);
            _prettySettings = CreateSettings(contractResolver, Formatting.Indented);
        }

        public string Serialize(object value, bool pretty)
        {
            return JsonConvert.SerializeObject(value, pretty ? _prettySettings : _compactSettings);
        }

        public TData Deserialize<TData>(string json)
        {
            TData value = JsonConvert.DeserializeObject<TData>(json, _compactSettings);

            if (ReferenceEquals(value, null))
            {
                throw new JsonSerializationException($"JSON produced a null {typeof(TData).Name}.");
            }

            return value;
        }

        public object Deserialize(string json, Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            object value = JsonConvert.DeserializeObject(json, type, _compactSettings);

            if (value == null)
            {
                throw new JsonSerializationException($"JSON produced a null {type.Name}.");
            }

            return value;
        }

        public JToken ToToken(object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            JsonSerializer serializer = JsonSerializer.Create(_compactSettings);
            return JToken.FromObject(value, serializer);
        }

        private static JsonSerializerSettings CreateSettings(
            IContractResolver contractResolver,
            Formatting formatting)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = formatting,
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                TypeNameHandling = TypeNameHandling.None,
                ContractResolver = contractResolver
            };

            settings.Converters.Add(new UnityValueTypeJsonConverter());
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }
}
