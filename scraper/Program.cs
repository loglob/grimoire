using Util;

public class Program
{
	private record class GameIndex(string fullName, Dictionary<string, string> books);

	public const string USAGE = @"USAGE: {0} [<config.json>]";

	private static async Task<(Config.Game game, int count)> processGame<TSpell>(IGame<TSpell> game) where TSpell : ISpell
	{
		var spellsByBook = game.Conf.Books.ToDictionary(x => x.Key, x => new List<TSpell>());
		var warnedAbout = new HashSet<string>();

		foreach (var sp in (await Task.WhenAll(game.Conf.Sources.Select(s => game.Instantiate(s).Spells().ToListAsync().AsTask())))
										.SelectMany(x => x))
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

			if(kvp.Value.Any())
				kvp.Value.StoreJson($"db/{game.Conf.Shorthand}/{kvp.Key}.json");
			else
				Log.DEFAULT.Warn($"No spells for source '{game.Conf.Shorthand}/{kvp.Key}'");
		}

		game.Conf.Books.Values
			.Where(b => spellsByBook.TryGetValue(b.Shorthand, out var found) && found.Any())
			.ToDictionary(b => b.Shorthand, b => b.FullName)
			.StoreJson($"db/{game.Conf.Shorthand}/index.json");

		Log.DEFAULT.Emit($"Parsed {total} spells for {game.Conf.Shorthand}.");
		return (game.Conf, total);
	}

	private static Task<(Config.Game game, int count)> processGame(Config.Game conf)
		=> conf.Shorthand switch
		{
			"dnd5e" => processGame(new DnD5e(conf)),
			"gd" => processGame(new Goedendag(conf)),
			var s => throw new ArgumentException($"Invalid game shorthand: {s}")
		};

	public static async Task<int> Main(string[] args)
	{
		if(args.Length > 1 || (args.Length == 1 && (args[0] == "-h" || args[0] == "--help")))
			goto usage;

		var games = Config.Parse(await File.ReadAllTextAsync(args.Length > 0 ? args[0] : "config.json")).Values;

		Log.DEFAULT.Emit($"Processing {games.Count} games with {games.Sum(g => g.Books.Count)} sources...");
		Directory.CreateDirectory("db");
		int total = 0;

		foreach(var (game, count) in await Task.WhenAll(games.Select(processGame)))
		{
			if(count == 0)
				Log.DEFAULT.Warn($"No spells for game '{game.Shorthand}'");

			total += count;
		}

		Log.DEFAULT.Emit($"Done processing {total} spells.");
		return 0;

		usage:
		Console.WriteLine(USAGE, Environment.GetCommandLineArgs()[0]);
		return 1;
	}
}