using System.Text.Json;
using System.Text.Json.Serialization;

namespace CricketManager.Domain.Common;

/// <summary>
/// Phase 17: <c>WorldState.PlannedRosters</c> is keyed by a <c>(Guid CompetitionId, int Year)</c>
/// tuple, which JSON has no native representation for - System.Text.Json can only key a
/// dictionary by a string (or a handful of simple types it can round-trip through
/// <c>ToString</c>/<c>Parse</c>), never a tuple. Converts to/from a JSON array of
/// <c>{CompetitionId, Year, RosterPlayerIds}</c> objects instead of forcing the tuple through a
/// composite string key, which would need its own fragile parse/format round trip.
/// </summary>
public sealed class PlannedRostersJsonConverter
    : JsonConverter<IDictionary<(Guid CompetitionId, int Year), List<Guid>>>
{
    public override IDictionary<(Guid CompetitionId, int Year), List<Guid>> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected a JSON array for PlannedRosters.");

        var result = new Dictionary<(Guid, int), List<Guid>>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected an object entry in PlannedRosters.");

            Guid competitionId = default;
            var year = 0;
            var roster = new List<Guid>();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "CompetitionId": competitionId = reader.GetGuid(); break;
                    case "Year": year = reader.GetInt32(); break;
                    case "RosterPlayerIds":
                        roster = JsonSerializer.Deserialize<List<Guid>>(ref reader, options) ?? new List<Guid>();
                        break;
                    default: reader.Skip(); break;
                }
            }

            result[(competitionId, year)] = roster;
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer, IDictionary<(Guid CompetitionId, int Year), List<Guid>> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var (key, roster) in value)
        {
            writer.WriteStartObject();
            writer.WriteString("CompetitionId", key.CompetitionId);
            writer.WriteNumber("Year", key.Year);
            writer.WritePropertyName("RosterPlayerIds");
            JsonSerializer.Serialize(writer, roster, options);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

/// <summary>
/// Phase 17: <c>WorldState.SeasonContributions</c>' leaf value is a
/// <c>(string Name, double Rating)</c> named ValueTuple, which System.Text.Json cannot serialize
/// by default - a ValueTuple exposes only public fields (<c>Item1</c>/<c>Item2</c>), never
/// properties, and <c>SqliteRepository</c>'s serializer options leave <c>IncludeFields</c> off
/// deliberately (see that class's own doc comment). Converts the nested dictionary to/from plain
/// JSON objects instead, keyed by <c>Guid.ToString()</c> at each level.
/// </summary>
public sealed class SeasonContributionsJsonConverter
    : JsonConverter<IDictionary<Guid, Dictionary<Guid, (string Name, double Rating)>>>
{
    public override IDictionary<Guid, Dictionary<Guid, (string Name, double Rating)>> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected a JSON object for SeasonContributions.");

        var result = new Dictionary<Guid, Dictionary<Guid, (string Name, double Rating)>>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var seasonId = Guid.Parse(reader.GetString()!);
            reader.Read(); // move onto the inner object

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected a JSON object for a season's contributions.");

            var inner = new Dictionary<Guid, (string Name, double Rating)>();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var playerId = Guid.Parse(reader.GetString()!);
                reader.Read(); // move onto the leaf object

                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("Expected a JSON object for a contribution entry.");

                var name = string.Empty;
                var rating = 0.0;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var propertyName = reader.GetString();
                    reader.Read();
                    switch (propertyName)
                    {
                        case "Name": name = reader.GetString() ?? string.Empty; break;
                        case "Rating": rating = reader.GetDouble(); break;
                        default: reader.Skip(); break;
                    }
                }

                inner[playerId] = (name, rating);
            }

            result[seasonId] = inner;
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer, IDictionary<Guid, Dictionary<Guid, (string Name, double Rating)>> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (seasonId, contributions) in value)
        {
            writer.WritePropertyName(seasonId.ToString());
            writer.WriteStartObject();
            foreach (var (playerId, entry) in contributions)
            {
                writer.WritePropertyName(playerId.ToString());
                writer.WriteStartObject();
                writer.WriteString("Name", entry.Name);
                writer.WriteNumber("Rating", entry.Rating);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}
