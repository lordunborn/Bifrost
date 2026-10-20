using System.Text.Json.Serialization;

namespace OmegaLauncher.Updates.Models
{
    [JsonSerializable(typeof(ClientManifest))]
    [JsonSerializable(typeof(ClientBaseline))]
    [JsonSerializable(typeof(BaselineSizes))]
    [JsonSerializable(typeof(HashCache))]
    [JsonSerializable(typeof(LauncherState))]
    [JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    public partial class UpdatesJsonContext : JsonSerializerContext
    {
    }
}
