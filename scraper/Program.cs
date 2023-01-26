using System;
using System.Linq;
using static Util;

public class Program
{
	public const string USAGE =
@"USAGE: {0} [<books.json>] [sources...]
Where a source is one of:
	latex [<latex.json>] [[book id] [input.tex ...] ...]
	overleaf [<overleaf.json>]
	dnd-wiki
If a json config file is not specified as an argument, the working directory is searched for the listed filename.
Each of the listed sources is searched for DnD spells and the compiled databases are outputted in ./dbs/
";

	public static async Task<int> Main(string[] args)
	{
		var keywords = new HashSet<string>{ "latex", "overleaf", "dnd-wiki" };
		var offs = Enumerable.Range(0, args.Length)
			.Where(i => keywords.Contains(args[i]))
			.ToArray();

		if(offs.Length == 0 || offs[0] > 1)
			goto usage;

		SourceBook[] books = LoadJson<SourceBook[]>(offs[0] == 1 ? args[0] : "books.json");
		var bookNames = books.Select(b => b.shorthand).ToHashSet();
		var sources = new List<ISource>();

		// load sources from arguments
		for (int io = 0; io < offs.Length; io++)
		{
			var v = new ArraySegment<string>(args, offs[io],
				((io + 1 >= offs.Length) ? offs.Length : offs[io + 1]) - offs[io]);

			switch(v[0])
			{
				case "latex":
				{
					if(v.Count < 2)
						goto usage;

					var cfgFile = "latex.json";

					if(!bookNames.Contains(v[1]))
					{
						cfgFile = v[1];
						v = v.Slice(2);
					}
					else
						v = v.Slice(1);

					if(v.Count == 0 || !bookNames.Contains(v[0]))
						goto usage;

					var book = v[0];
					var files = new List<(string src, string file)>();

					v = v.Slice(1);

					foreach (var a in v)
					{
						if(bookNames.Contains(a))
							book = a;
						else
							files.Add((book, a));
					}

					sources.Add(new LatexFiles( LoadJson<Latex.Config>(cfgFile), files));
				}
				continue;

				case "overleaf": switch(v.Count)
				{
					case 1:
						sources.Add(new Overleaf(LoadJson<Overleaf.Config>("overleaf.json")));
					continue;

					case 2:
						sources.Add(new Overleaf(LoadJson<Overleaf.Config>(v[1])));
					continue;

					default: goto usage;
				}

				case "dnd-wiki": switch(v.Count)
				{
					case 1:
						sources.Add(new DndWiki(books));
					continue;

					default: goto usage;
				}

				default: break; // <- impossible
			}
		}

		Console.WriteLine($"Processing {sources.Count} sources...");

		var spellsByBook = bookNames.ToDictionary(x => x, x => new List<Spell>());
		var warnedAbout = new HashSet<string>();

		foreach (var sp in (await Task.WhenAll(sources.Select(s => s.Spells().ToListAsync().AsTask())))
										.SelectMany(x => x))
		{
			if(spellsByBook.TryGetValue(sp.source, out var spells))
				spells.Add(sp);
			else if(warnedAbout.Add(sp.source))
				Console.Error.WriteLine($"[WARN] Discarding unknown source '{sp.source}'");
		}

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

		return 0;

		usage:
		Console.WriteLine(USAGE, Environment.GetCommandLineArgs()[0]);
		return 1;
	}
}