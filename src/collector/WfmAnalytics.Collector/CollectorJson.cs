using System.Text.Json;
using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public static class CollectorJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ActivityStateJsonConverter());
        return options;
    }
}

public sealed class ActivityStateJsonConverter : JsonConverter<ActivityState>
{
    public override ActivityState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "active" => ActivityState.Active,
            "inactive" => ActivityState.Inactive,
            "locked" => ActivityState.Locked,
            "detail_unavailable" => ActivityState.DetailUnavailable,
            _ => throw new JsonException($"Unknown activity state '{value}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ActivityState value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ActivityState.Active => "active",
            ActivityState.Inactive => "inactive",
            ActivityState.Locked => "locked",
            ActivityState.DetailUnavailable => "detail_unavailable",
            _ => throw new JsonException($"Unknown activity state '{value}'.")
        });
    }
}
