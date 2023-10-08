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

	public static async Task<int> Main(string[] args)
	{
		if(args.Length > 1 || (args.Length == 1 && (args[0] == "-h" || args[0] == "--help")))
			goto usage;

		var cfg = Config.Parse(await File.ReadAllTextAsync(args.Length > 0 ? args[0] : "config.json"));
		
		Console.WriteLine($"Processing {cfg.Sources.Length} sources...");

		var spellsByBook = cfg.Books.ToDictionary(x => x.Key, x => new List<Spell>());
		var warnedAbout = new HashSet<string>();

		foreach (var sp in (await Task.WhenAll(cfg.Sources.Select(s => s.Instantiate(cfg).Spells().ToListAsync().AsTask())))
										.SelectMany(x => x))
		{
			if(spellsByBook.TryGetValue(sp.source, out var spells))
				spells.Add(sp);
			else if(warnedAbout.Add(sp.source))
				Console.Error.WriteLine($"[WARN] Discarding unknown source '{sp.source}'");
		}

		int total = 0;
		Directory.CreateDirectory("db");

		foreach (var kvp in spellsByBook)
		{
			total += kvp.Value.Count;

			if(kvp.Value.Any())
				kvp.Value.StoreJson($"db/{kvp.Key}.json");
			else
				Console.Error.WriteLine($"[Warn] No spells for source '{kvp.Key}'");
		}

		cfg.Books.Values
			.Where(b => spellsByBook.TryGetValue(b.Shorthand, out var found) && found.Any())
			.ToDictionary(b => b.Shorthand, b => b.FullName)
			.StoreJson("db/index.json");
		Console.WriteLine($"Done. Found {total} spells.");

		return 0;

		usage:
		Console.WriteLine(USAGE, Environment.GetCommandLineArgs()[0]);
		return 1;
	}
}