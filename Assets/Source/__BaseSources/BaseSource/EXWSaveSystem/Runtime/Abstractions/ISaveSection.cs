using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace EXW.SaveSystem
{
    public interface ISaveSection
    {
        string Key { get; }
        int CurrentVersion { get; }
        int RestoreOrder { get; }
        IReadOnlyList<ISaveMigration> Migrations { get; }

        object CaptureState();
        object CreateDefaultState();
        object DeserializeState(JToken token, ISaveSerializer serializer);
        void RestoreState(object state);
    }
}
