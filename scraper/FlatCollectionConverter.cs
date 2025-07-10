using Grimoire;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;


/// <summary>
/// JSON converter that treats single values as singleton lists
/// </summary>
class FlatCollectionConverter : JsonConverter<ImmutableList<string>>
{
	public override ImmutableList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if(reader.TokenType == JsonTokenType.String)
			return [ reader.GetString()! ];
		else if(reader.TokenType == JsonTokenType.StartArray)
		{
			var buf = ImmutableList.CreateBuilder<string>();

			while(true)
			{
				if(! reader.Read())
					throw new JsonException();

				if(reader.TokenType == JsonTokenType.EndArray)
					break;
				else if(reader.TokenType == JsonTokenType.String)
					buf.Add(reader.GetString()!);
				else
					throw new JsonException("Expected ] or a string");
			}

			return buf.ToImmutable();
		}
		else
			throw new JsonException("Expected [ or a string");
	}

	public override void Write(Utf8JsonWriter writer, ImmutableList<string> value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach(var x in value)
			writer.WriteStringValue(x);

		writer.WriteEndArray();
	}
}
