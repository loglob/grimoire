using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire;

public class Program
{
	private record class GameIndex(string fullName, Dictionary<string, string> books);

	public const string USAGE = @"USAGE: {0} [<-n|--noprogress>] [<config.json>]";

	public static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = {
			new JsonStringEnumConverter()
		},
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		RespectNullableAnnotations = true,
		RespectRequiredConstructorParameters = true
	};

	static Program()
	{
		JsonOptions.Converters.Add(new FlatCollectionConverter());
	}

	private static async Task store<T>(string path, T value)
	{
		await using var f = File.Create(path);
		await JsonSerializer.SerializeAsync(f, value, JsonOptions);
	}

	private static string conjugate(int count)
		=> count == 1 ? "" : "s";

	private static string conjugate<T>(IReadOnlyCollection<T> xs)
		=> conjugate(xs.Count);


	private static async Task<(Config.Game game, int count)> processGame<TSpell>(IGame<TSpell> game) where TSpell : ISpell
	{
		var spellsByBook = game.Conf.Books.ToDictionary(x => x.Key, x => new List<TSpell>());
		var warnedAbout = new HashSet<string>();
		var sources = game.Conf.Sources.Select(game.Instantiate).ToImmutableList();

		// load materials
		await Task.WhenAll(sources.GroupBy(s => s.Game).Select(async g => {
			foreach(var s in g)
				await s.LoadMaterials();

			var mc = g.Key.Manifest.Materials.Count;
			var uc = g.Key.Manifest.Units.Count;

			if(mc > 0 || uc > 1)
				g.Key.Log.Emit($"Loaded {mc} material{conjugate(mc)} and {uc} unit{conjugate(uc)}");
		}));

		// actually parse spells
		foreach (var sp in (await Task.WhenAll(sources.Select(s => s.Spells().ToListAsync().AsTask()))).SelectMany(x => x))
		{
			if(spellsByBook.TryGetValue(sp.Source, out var spells))
				spells.Add(sp);
			else if(warnedAbout.Add(sp.Source))
				Log.DEFAULT.Warn($"Discarding unknown source '{game.Conf.Shorthand}/{sp.Source}'");
		}

		Directory.CreateDirectory($"db/{game.Conf.Shorthand}");

		int total = 0;

		foreach (var kvp in spellsByBook)
		{
			total += kvp.Value.Count;
			await store($"db/{game.Conf.Shorthand}/{kvp.Key}.json", kvp.Value);

			if(kvp.Value.Count == 0)
				Log.DEFAULT.Warn($"No spells for source '{game.Conf.Shorthand}/{kvp.Key}'");
		}

		Log.DEFAULT.Emit($"Parsed {total} spell{conjugate(total)} for {game.Conf.Shorthand}.");
		return (game.Conf, total);
	}

	private static Task<(Config.Game game, int count)> processGame(Config.Game conf)
		=> conf.Shorthand switch
		{
			"dnd5e" => processGame(new DnD5e(conf)),
			"gd" => processGame(new Goedendag(conf)),
			"pf2e" => processGame(new Pf2e(conf)),
			var s => throw new ArgumentException($"Invalid game shorthand: {s}")
		};

	public static async Task<int> Main(string[] allArgs)
	{
		var args = new ArraySegment<string>(allArgs);
		bool moreArgs = true;

		while(args.Count >= 1 && moreArgs)
		{
			switch(args[0])
			{
				case "-n":
				case "--noprogress":
					Log.disablePins();
					args = args.Slice(1);
				continue;


				case "-h":
				case "--help":
					goto usage;

				default:
					moreArgs = false;
				break;
			}
		}
		if(args.Count > 1)
			goto usage;

		var games = Config.Parse(await File.ReadAllTextAsync(args.Count > 0 ? args[0] : "config.json")).Values;

		var sourceCount = games.Sum(g => g.Books.Count);
		Log.DEFAULT.Emit($"Processing {games.Count} game{conjugate(games)} with {sourceCount} source{conjugate(sourceCount)}...");
		Directory.CreateDirectory("db");
		int total = 0;

		foreach(var (game, count) in await Task.WhenAll(games.Select(processGame)))
		{
			if(count == 0)
				Log.DEFAULT.Warn($"No spells for game '{game.Shorthand}'");

			total += count;
		}

		await store($"db/index.json",
			games.ToDictionary(
				g => g.Shorthand,
				g => g.Books.Values.ToDictionary(b => b.Shorthand, b => b.FullName)
			)
		);

		Log.DEFAULT.Emit($"Done processing {total} spell{conjugate(total)}.");
		return 0;

		usage:
		Console.WriteLine(USAGE, Environment.GetCommandLineArgs()[0]);
		return 1;
	}
}