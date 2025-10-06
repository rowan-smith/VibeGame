using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeGame.Core
{
    public static class JsonModelLoader
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // Make sure numbers like 0 or 1 are read as floats fine
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public static T LoadFile<T>(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Required JSON file not found: {path}");

            try
            {
                var json = File.ReadAllText(path);
                var model = JsonSerializer.Deserialize<T>(json, Options);
                if (model == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize JSON file '{path}' to {typeof(T).Name}.");
                }

                return model;
            }
            catch (JsonException jex)
            {
                throw new InvalidOperationException($"Invalid JSON in '{path}': {jex.Message}", jex);
            }
        }
    }
}
