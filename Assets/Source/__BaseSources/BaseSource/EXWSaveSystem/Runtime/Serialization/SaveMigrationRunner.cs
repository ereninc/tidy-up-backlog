using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace EXW.SaveSystem
{
    internal static class SaveMigrationRunner
    {
        public static JToken Run(ISaveSection provider, SaveSectionRecord record)
        {
            if (record.Version > provider.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Section '{provider.Key}' was saved with version {record.Version}, " +
                    $"but this build supports version {provider.CurrentVersion}.");
            }

            JToken current = record.Data?.DeepClone() ?? JValue.CreateNull();
            int version = record.Version;

            while (version < provider.CurrentVersion)
            {
                ISaveMigration migration = FindMigration(provider.Migrations, version);

                if (migration == null || migration.ToVersion != version + 1)
                {
                    throw new InvalidOperationException(
                        $"Section '{provider.Key}' needs a {version} -> {version + 1} migration.");
                }

                current = migration.Migrate(current) ??
                          throw new InvalidOperationException(
                              $"Migration {version} -> {version + 1} for '{provider.Key}' returned null.");

                version = migration.ToVersion;
            }

            return current;
        }

        private static ISaveMigration FindMigration(
            IReadOnlyList<ISaveMigration> migrations,
            int fromVersion)
        {
            if (migrations == null)
            {
                return null;
            }

            ISaveMigration match = null;

            for (int i = 0; i < migrations.Count; i++)
            {
                ISaveMigration migration = migrations[i];

                if (migration == null || migration.FromVersion != fromVersion)
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Multiple migrations start at version {fromVersion}.");
                }

                match = migration;
            }

            return match;
        }
    }
}
