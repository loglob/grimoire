using Latex;

/// <summary>
/// Scraper for processing LaTeX files
/// </summary>
public record LatexFiles<TSpell>(IGame<TSpell> Game, Config.LatexSource Conf) : ISource<TSpell>
{
	public IAsyncEnumerable<TSpell> Spells()
	{
		var log = Log.DEFAULT.AddTags(Game.Conf.Shorthand, "local files");
		var comp = new Compiler(Conf.Options, log);
		var lex = new Lexer(log);

		foreach(var f in Conf.MacroFiles)
			comp.LearnMacrosFrom(File.ReadLines(f), f);

		var segments = Conf.Files
			.SelectMany(kvp => kvp.Value.Select(file => (kvp.Key, file)))
			.Select(x => (source: x.Key, code: lex.Tokenize(File.ReadLines(x.file), x.file) ))
			.ToList();

		foreach (var (_, code) in segments.Where(seg => seg.source == Config.LatexOptions.MACROS_SOURCE_NAME))
			comp.LearnMacrosFrom(code);

		return segments
			.Where(seg => seg.source != Config.LatexOptions.MACROS_SOURCE_NAME)
			.SelectMany(seg => comp.ExtractSpells(Game, seg.code.ToArray(), seg.source))
			.ToAsyncEnumerable();
	}
}