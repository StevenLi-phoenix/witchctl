using Newtonsoft.Json;

namespace WitchCtl
{
    internal static class Json
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.None,
        };

        internal static string Write(object value) => JsonConvert.SerializeObject(value, Settings);

        /// <summary>Empty or whitespace bodies deserialize to a default instance so every request type is optional.</summary>
        internal static T Read<T>(string body) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(body)) return new T();
            return JsonConvert.DeserializeObject<T>(body, Settings) ?? new T();
        }

        internal static string Error(string kind, string message) => Write(new { error = kind, message });
    }
}
