
public class Program
{
	private readonly SourceBook[] books;
	private readonly Dictionary<string, List<Spell>> spellsByBook;
	private readonly HashSet<string> warnedAbout = new HashSet<string>();

	private Program(SourceBook[] books)
	{
		this.books = books;
		this.spellsByBook = books.ToDictionary(b => b.shorthand, _ => new List<Spell>());
		Console.WriteLine($"Found {books.Length} sources");
	}

	private Program() : this(Util.LoadJson<SourceBook[]>("sources.json"))
	{}

	private void addSpell(Spell sp)
	{
		if(spellsByBook.TryGetValue(sp.source, out var spells))
			spells.Add(sp);
		else if(warnedAbout.Add(sp.source))
			Console.Error.WriteLine($"[WARN] Discarding unknown source '{sp.source}'");
	}

	private async Task dndWiki()
	{
		var wiki = new DndWiki(this);
		var names = await wiki.SpellNames();
		Console.WriteLine($"Processing {names.Length} spells from DnDWiki...");

		await foreach(var s in wiki.Spells(names))
			addSpell(s);
	}

	private async Task overleaf()
	{
		var ol = new Overleaf(Util.LoadJson<Overleaf.Config>("overleaf.json"));

		foreach(var s in await ol.Spells())
			addSpell(s);
	}

	public SourceBook FindSource(string source)
	{
		var b = books.Where(b => b.Matches(source)).ToArray();

		if(b.Length == 0)
			throw new Exception($"Unknown source: '{source}'");
		else if(b.Length > 1)
			throw new Exception($"Unknown source: '{source}': Ambigous between {string.Join(", ", b)}");
		else
			return b[0];
	}



	private async Task main()
	{
//		await dndWiki();
		await overleaf();

		int total = 0;

		Directory.CreateDirectory("./dbs");
		foreach (var kvp in spellsByBook)
		{
			total += kvp.Value.Count;

			if(kvp.Value.Any())
				kvp.Value.StoreJson($"./dbs/{kvp.Key}.json");
			else
				Console.Error.WriteLine($"[Warn] No spells for source '{kvp.Key}'");
		}

		books.ToDictionary(s => s.shorthand, s => s.fullName).StoreJson("./dbs/index.json");
		Console.WriteLine($"Done. Found {total} spells.");

	}

	public static Task Main(string[] args)
		=> new Program().main();
}