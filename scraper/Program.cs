public class Program
{
	public const string USAGE =
@"USAGE: {0} [<config.json>] [sources...]
Where a source is one of:
	latex [<latex.json>] [[book id] [input.tex ...] ...]
	overleaf [<overleaf.json>]
	dnd-wiki
	copy [copy.json ...]
If a json config file is not specified as an argument, the working directory is searched for the listed filename.
Each of the listed sources is searched for DnD spells and the compiled databases are outputted in ./db/
";

	/// <summary>
	///  
	/// </summary>
	/// <returns> The number of spells read </returns>
	private static async Task<(string game, List<Config.Book> books, int count)> processGame<TSpell>(IGame<TSpell> game) where TSpell : ISpell
	{
		var spellsByBook = game.Conf.Books.ToDictionary(x => x.Key, x => new List<TSpell>());
		var warnedAbout = new HashSet<string>();

		foreach (var sp in (await Task.WhenAll(game.Conf.Sources.Select(s => game.Instantiate(s).Spells().ToListAsync().AsTask())))
										.SelectMany(x => x))
		{
			if(spellsByBook.TryGetValue(sp.Source, out var spells))
				spells.Add(sp);
			else if(warnedAbout.Add(sp.Source))
				Console.Error.WriteLine($"[WARN] Discarding unknown source '{sp.Source}'");
		}

		Directory.CreateDirectory($"db/{game.Conf.Shorthand}");

		int total = 0;

		foreach (var kvp in spellsByBook)
		{
			total += kvp.Value.Count;

			if(kvp.Value.Any())
				kvp.Value.StoreJson($"db/{game.Conf.Shorthand}/{kvp.Key}.json");
			else
				Console.Error.WriteLine($"[Warn] No spells for source '{game.Conf.Shorthand}/{kvp.Key}'");
		}

		var got = game.Conf.Books.Values
			.Where(b => spellsByBook.TryGetValue(b.Shorthand, out var found) && found.Any())
			.ToList();

		Console.WriteLine($"Parsed {total} spells for {game.Conf.FullName}.");
		return (game.Conf.Shorthand, got ,total);
	}

	private static Task<(string game, List<Config.Book> books, int count)> processGame(Config.Game conf)
		=> conf.Shorthand switch
		{
			"dnd5e" => processGame(new DnD5e(conf)),
			var s => throw new ArgumentException($"Invalid game shorthand: {s}")
		};

	public static async Task<int> Main(string[] args)
	{
		if(args.Length > 1 || (args.Length == 1 && (args[0] == "-h" || args[0] == "--help")))
			goto usage;

		var games = Config.Parse(await File.ReadAllTextAsync(args.Length > 0 ? args[0] : "config.json")).Values;
		
		Console.WriteLine($"Processing {games.Count} games with {games.Sum(g => g.Books.Count)} sources...");
		Directory.CreateDirectory("db");
		int total = 0;
		Dictionary<string, Dictionary<string, string>> index = new();

		foreach(var (game, books, count) in await Task.WhenAll(games.Select(processGame)))
		{
			index[game] = books.ToDictionary(b => b.Shorthand, b => b.FullName);
			total += count;
		}

		index.StoreJson("db/index.json");	

		Console.WriteLine($"Done processing {total} spells.");
		return 0;

		usage:
		Console.WriteLine(USAGE, Environment.GetCommandLineArgs()[0]);
		return 1;
	}
}