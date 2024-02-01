using System.Text.Json.Nodes;
using Util;
using Olspy;
using System.Text.Json;

public static class Config
{
	private static string[] strArray(JsonNode? n)
		=> n!.AsArray().Select(x => (string)x!).ToArray();

	public static readonly JsonSerializerOptions JsonOpt = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public record Game(string Shorthand, Dictionary<string, Book> Books, Source[] Sources)
	{
		public static Game Parse(string shorthand, JsonObject o)
			=> new(
				shorthand,
				o["books"]!.AsObject().ToDictionary(kvp => kvp.Key, kvp => Book.Parse(kvp.Key, kvp.Value!)),
				o["sources"]!.AsArray().Select(n => Source.Parse(n!)).ToArray()
			);

		public override string ToString()
			=> $"Game( Shorthand = {Shorthand}, Books = {Books.Show()}, Sources = {Sources.Show()} )";
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

	public abstract record Source(float CacheLifetime)
	{
		public static Source Parse(JsonNode n)
			=> (n is JsonObject o ? (string)o["type"]! : (string)n!) switch {
				"dndwiki" => DndWikiSource.Parse(n),
				"overleaf" => OverleafSource.Parse(n.AsObject()!),
				"latex" => LatexSource.Parse(n.AsObject()!),
				"copy" => CopySource.Parse(n.AsObject()!),
				var x => throw new FormatException($"Invalid source type {x.Show()}")
			};
	}

	public sealed record DndWikiSource(TimeSpan? RateLimit, float CacheLifetime)
		: Source(CacheLifetime)
	{
		public override string ToString()
			=> $"DndWikiSource";

		new internal static DndWikiSource Parse(JsonNode n)
		{
			var o = n as JsonObject;
			return new(
				(o is not null && o["rateLimit"] is JsonValue v)
					? TimeSpan.FromMilliseconds((int)v)
					: null ,
				(float?)o?["cacheLifetime"] ?? float.PositiveInfinity
			);
		}
	}

	public abstract record OverleafAuth()
	{
		/// <summary>
		///  A string that identifies a project for cache matching
		/// </summary>
		public abstract string CacheID { get; }

		public abstract Task<Project> Instantiate();

		public static OverleafAuth Parse(JsonNode o)
			=> (o is JsonValue v)
				? new OverleafLinkAuth(((string?)v.AsValue())!)
				: o.AsObject().Deserialize<OverleafUserAuth>(JsonOpt)!;
	}

	public record OverleafLinkAuth(string Link) : OverleafAuth()
	{
		public override string CacheID
		{
			get
			{
				var spl = Link.Split('/');

				return spl[^1].Length == 0 ? spl[^2] : spl[^1];
			}
		}

		public override Task<Project> Instantiate()
			=> Project.Open(new Uri(Link));
	}

	public record OverleafUserAuth(string Host, string ID, string Email, string Password) : OverleafAuth()
	{
		public override string CacheID
			=> ID;

		public override Task<Project> Instantiate()
			=> Project.Open(new Uri(Host), ID, Email, Password);
	}

	/// <summary>
	/// The configuration for the overleaf scraper
	/// </summary>
	/// <param name="Auth"> How to connect to the overleaf instance </param>
	/// <param name="LocalMacros"> List of local file paths to load macros from </param>
	/// <param name="Latex"> The latex configuration to use.</param>
	public sealed record OverleafSource(OverleafAuth Auth, float CacheLifetime, string[] LocalMacros, LatexSource Latex)
		: Source(CacheLifetime)
	{
		internal static OverleafSource Parse(JsonObject o)
			=> new(
				OverleafAuth.Parse(o["auth"]!),
				((float?)o["cacheLifetime"]) ?? float.PositiveInfinity,
				o["localMacros"]?.AsArray()?.Select(x => (string)x!)?.ToArray() ?? Array.Empty<string>(),
				LatexSource.Parse(o["latex"]!.AsObject())
			);
	}

	public sealed record LatexManifest(string[]? MacroFiles = null, Dictionary<string, string[]>? Files = null);

	public sealed record LatexSource(LatexOptions Options, string[] MacroFiles, Dictionary<string, string[]> Files, float CacheLifetime, string? LocalManifest = null)
		: Source(CacheLifetime)
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
				LatexOptions.Parse(o) ,
				strArray(o["macroFiles"]) ,
				parseFiles(o["files"]!.AsObject()) ,
				((float?)o["cacheLifetime"]) ?? float.PositiveInfinity ,
				(string?)o["localManifest"]
			);

		public override string ToString()
			=> $"LatexSource( {nameof(Options)} = {Options}, {nameof(MacroFiles)} = {MacroFiles.Show()}, {nameof(Files)} = {Files.Show()}, {nameof(CacheLifetime)} = {CacheLifetime}s, {nameof(LocalManifest)} = {LocalManifest.Show()} )";
	}

	public sealed record CopySource(string[] From) : Source(0.0f)
	{
		internal static CopySource Parse(JsonObject o)
			=> new( strArray(o["from"]) );

		public override string ToString()
			=> $"CopySource( {nameof(From)} = {From.Show()} )";
	}

	/// <param name="SpellAnchor"> A latex command that initializes a spell description </param>
	/// <param name="UpcastAnchor"> A latex command that initiates an upcast section </param>
	/// <param name="Environments"> Maps latex environments onto equivalent HTML tags</param>
	/// <param name="Images"> Raw HTML text to replace instances of specific images with </param>
	/// <param name="MaximumExpansions"> The maximum number of expansions to perform for one expand() call </param>
	public sealed record LatexOptions(
		string SpellAnchor,
		string? UpcastAnchor,
		Dictionary<string, string> Environments,
		Dictionary<string, string> Images,
		int MaximumExpansions = LatexOptions.DEFAULT_MAXIMUM_EXPANSIONS)
	{
		public const int DEFAULT_MAXIMUM_EXPANSIONS = 1_000_000;

		public const string MACROS_SOURCE_NAME = "macros";

		internal static LatexOptions Parse(JsonObject o)
			=> new(
				(string)o["spellAnchor"]!,
				(string?)o["upcastAnchor"],
				o["environments"]?.AsObject()?.ToDictionary(kvp => kvp.Key, kvp => (string)kvp.Value!) ?? new(),
				o["images"]?.AsObject()?.ToDictionary(kvp => kvp.Key, kvp => (string)kvp.Value!) ?? new() ,
				((int?)o["maximumExpansions"]) ?? DEFAULT_MAXIMUM_EXPANSIONS
			);

		public override string ToString()
			=> $"{nameof(LatexOptions)}( {nameof(SpellAnchor)} = {SpellAnchor}, {nameof(UpcastAnchor)} = {UpcastAnchor}, {nameof(Environments)} = {Environments.Show()}, {nameof(Images)} = {Images.Show()}, {nameof(MaximumExpansions)} = {MaximumExpansions} )";
	}

	public static Dictionary<string, Game> Parse(JsonObject o)
		=> o.ToDictionary(kvp => kvp.Key, kvp => Game.Parse(kvp.Key, kvp.Value!.AsObject()));

	public static Dictionary<string, Game> Parse(string str)
		=> Parse(JsonNode.Parse(str)!.AsObject());
}