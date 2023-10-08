using System.Text.Json.Nodes;

public static class Config
{
	private static string[] strArray(JsonNode? n)
		=> n!.AsArray().Select(x => (string)x!).ToArray();

	public record Game(string Shorthand, string FullName, Dictionary<string, Book> Books, Source[] Sources)
	{
		public static Game Parse(string shorthand, JsonObject o)
			=> new(
				shorthand,
				o["fullName"]!.GetValue<string>(),
				o["books"]!.AsObject().ToDictionary(kvp => kvp.Key, kvp => Book.Parse(kvp.Key, kvp.Value!)),
				o["sources"]!.AsArray().Select(n => Source.Parse(n!)).ToArray()
			);

        public override string ToString()
			=> $"Game( Shorthand = {Shorthand}, FullName = {FullName}, Books = {Books.Show()}, Sources = {Sources.Show()} )";
    }

	public record Book(string Shorthand, string FullName, string[] Alts)
	{
		internal static Book Parse(string shortHand, JsonNode n)
			=> n is JsonObject o
				? new(shortHand, (string)o["fullName"]!, strArray(o["alts"]))
				: new(shortHand, (string)n!, Array.Empty<string>());

		public bool Matches(string src)
		{
			var stripped = (string x) => x.Where(c => char.IsWhiteSpace(c) || char.IsLetter(c));
			var spaced = (string x) => x.Select(c => char.IsLetter(c) ? c : ' ').Squeeze();

			src = src.ToLower();

			if(src == Shorthand.ToLower())
				return true;

			foreach(var name in (Alts ?? Enumerable.Empty<string>()).Prepend(FullName))
			{
				var ln = name.ToLower();

				if(src == ln
					|| stripped(src).SequenceEqual(stripped(ln))
					|| spaced(src).SequenceEqual(spaced(ln)))
					return true;
			}

			return false;
		}

		public override string ToString()
			=> $"Book( Shorthand = {Shorthand}, FullName = {FullName}, Alts = {Alts.Show()} )";
	}

	public abstract record Source
	{
		public static Source Parse(JsonNode n)
			=> (n is JsonObject o ? (string)o["type"]! : (string)n!) switch {
				"dndwiki" => DndWikiSource.Parse(n),
				"overleaf" => OverleafSource.Parse(n.AsObject()!),
				"latex" => LatexSource.Parse(n.AsObject()!),
				"copy" => CopySource.Parse(n.AsObject()!),
				var x => throw new FormatException($"Invalid source type '{x}'")
			};
	}

	public record DndWikiSource(TimeSpan? RateLimit = default) : Source
	{
		public override string ToString()
			=> $"DndWikiSource";

		new internal static DndWikiSource Parse(JsonNode n)
			=> new(
				(n is JsonObject o && o["rateLimit"] is JsonValue v)
					? TimeSpan.FromMilliseconds((int)v)
					: null
			);

	}

	/// <summary>
	/// The configuration for the overleaf scraper
	/// </summary>
	/// <param name="ProjectID"> The project ID to scrape for spells. Mandatory </param>
	/// <param name="Password"> The password to connect to overleaf with. See olspy's documentation for details.</param>
	/// <param name="User"> The username to connect to overleaf with. See olspy's documentation for details.</param>
	/// <param name="Host">The hostname of the overleaf server. If blank, determined automatically</param>
	/// <param name="Latex"> The latex configuration to use.</param>
	public record OverleafSource(string ProjectID, string Password, string? User, string? Host, LatexOptions Latex) : Source
	{
		internal static OverleafSource Parse(JsonObject o)
			=> new(
				(string)o["projectID"]!,
				(string)o["password"]!,
				(string?)o["user"],
				(string?)o["host"],
				LatexOptions.Parse(o["latex"]!.AsObject())
			);

		public override string ToString()
			=> $"OverleafSource( ProjectID = {ProjectID}, Password = {Password}, User = {User}, Host = {Host}, Latex = {Latex} )";
	}

	public record LatexSource(LatexOptions Options, string[] MacroFiles, Dictionary<string, string[]> Files) : Source
	{
		private static Dictionary<string, string[]> parseFiles(JsonObject o)
		{
			Dictionary<string, List<string>> acc = new();

			foreach (var kvp in o)
			{
				var fs = kvp.Value is JsonValue f ? new[]{ (string)f! } : strArray(kvp.Value);

				if(acc.TryGetValue(kvp.Key, out var v))
					v.AddRange(fs);
				else
					acc[kvp.Key] = new List<string>(fs);
			}

			return acc.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
		}

		internal static LatexSource Parse(JsonObject o)
			=> new(
				LatexOptions.Parse(o),
				strArray(o["macroFiles"]),
				parseFiles(o["files"]!.AsObject())
			);

		public override string ToString()
			=> $"LatexSource( Options = {Options}, MacroFiles = {MacroFiles.Show()}, Files = {Files.Show()} )";
	}

	public record CopySource(string[] From) : Source
	{
		internal static CopySource Parse(JsonObject o)
			=> new( strArray(o["from"]) );

		public override string ToString()
			=> $"CopySource( From = {From.Show()} )";
	}

	/// <param name="SpellAnchor"> A latex command that initializes a spell description </param>
	/// <param name="UpcastAnchor"> A latex command that initiates an upcast section </param>
	/// <param name="Environments"> Maps latex environments onto equivalent HTML tags</param>
	/// <param name="Images"> Text to replace instances of specific images with </param>
	public record LatexOptions(
		string SpellAnchor,
		string? UpcastAnchor,
		Dictionary<string, string> Environments,
		Dictionary<string, string> Images)
	{
		internal static LatexOptions Parse(JsonObject o)
			=> new(
				(string)o["spellAnchor"]!,
				(string?)o["upcastAnchor"],
				o["environments"]!.AsObject().ToDictionary(kvp => kvp.Key, kvp => (string)kvp.Value!),
				o["images"]!.AsObject().ToDictionary(kvp => kvp.Key, kvp => (string)kvp.Value!)
			);

		public override string ToString()
			=> $"LatexOptions( SpellAnchor = {SpellAnchor}, UpcastAnchor = {UpcastAnchor}, Environments = {Environments.Show()}, Images = {Images.Show()} )";
	}

	public static Dictionary<string, Game> Parse(JsonObject o)
		=> o.ToDictionary(kvp => kvp.Key, kvp => Game.Parse(kvp.Key, kvp.Value!.AsObject()));

	public static Dictionary<string, Game> Parse(string str)
		=> Parse(JsonNode.Parse(str)!.AsObject());
}