using System;
using Newtonsoft.Json.Linq;

namespace EXW.SaveSystem
{
    public interface ISaveSerializer
    {
        string Serialize(object value, bool pretty);
        TData Deserialize<TData>(string json);
        object Deserialize(string json, Type type);
        JToken ToToken(object value);
    }
}
