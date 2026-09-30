using System.Text.Json.Serialization;

namespace SourceGit
{
    /// <summary>
    ///     Serialization for the files this fork keeps beside upstream's. A context of its own
    ///     rather than two more lines in JsonCodeGen, which is upstream's; the options are the
    ///     same, so an account written here reads exactly as it did in preference.json.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        IgnoreReadOnlyFields = true,
        IgnoreReadOnlyProperties = true,
        Converters = [
            typeof(DateTimeConverter),
            typeof(ColorConverter),
            typeof(GridLengthConverter),
        ]
    )]
    [JsonSerializable(typeof(Models.ForkPreferences))]
    [JsonSerializable(typeof(Models.ForkRepositoryStates))]
    internal partial class ForkJsonCodeGen : JsonSerializerContext { }
}
