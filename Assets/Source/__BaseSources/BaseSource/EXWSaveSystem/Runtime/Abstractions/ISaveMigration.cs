using Newtonsoft.Json.Linq;

namespace EXW.SaveSystem
{
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        JToken Migrate(JToken source);
    }
}
