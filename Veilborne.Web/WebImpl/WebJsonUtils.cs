using System.Text.Json;
using System.Text.Json.Serialization;

namespace Veilborne.Web.WebImpl
{
    public static class WebJsonUtils
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public static T? LoadString<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (JsonException jex)
            {
                Console.WriteLine($"[WebJsonUtils] Invalid JSON: {jex.Message}");
                return default;
            }
        }
    }
}
