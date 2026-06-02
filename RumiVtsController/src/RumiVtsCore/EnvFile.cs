namespace RumiVtsController
{
    internal static class EnvFile
    {
        public const string TokenVar = "VTS_AUTH_TOKEN";

        public static void Load(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            foreach (var (key, value) in ReadEntries(path))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public static string? GetToken()
        {
            return Environment.GetEnvironmentVariable(TokenVar);
        }

        public static void SaveToken(string path, string token)
        {
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in ReadEntries(path))
            {
                entries[key] = value;
            }

            entries[TokenVar] = token;
            Environment.SetEnvironmentVariable(TokenVar, null); // clear before writing
            WriteEntries(path, entries);
            Environment.SetEnvironmentVariable(TokenVar, token);
        }

        private static IEnumerable<(string Key, string Value)> ReadEntries(string path)
        {
            if (!File.Exists(path))
            {
                yield break;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var sep = trimmed.IndexOf('=');
                if (sep <= 0)
                {
                    continue;
                }

                var key = trimmed[..sep].Trim();
                var value = trimmed[(sep + 1)..].Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                yield return (key, value);
            }
        }

        private static void WriteEntries(string path, IDictionary<string, string> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using var writer = new StreamWriter(path, false);
            foreach (var entry in entries)
            {
                writer.WriteLine($"{entry.Key}={entry.Value}");
            }
        }
    }
}
