using Latex;

/// <summary>
/// Scraper for processing LaTeX files
/// </summary>
public record LatexFiles<TSpell>(IGame<TSpell> Game, Config.LatexSource Conf) : ISource<TSpell>
{
	private readonly Compiler comp = new(Conf.Options);

	public IAsyncEnumerable<TSpell> Spells()
	{
		foreach(var f in Conf.MacroFiles)
			comp.LearnMacrosFrom(File.ReadLines(f), f);

		var segments = Conf.Files
			.SelectMany(kvp => kvp.Value.Select(file => (kvp.Key, file)))
			.Select(x => (source: x.Key, code: Lexer.Tokenize(File.ReadLines(x.file), x.file) ))
			.ToList();

		foreach (var (_, code) in segments.Where(seg => seg.source == Config.LatexOptions.MACROS_SOURCE_NAME))
			comp.LearnMacrosFrom(code);

		return segments
			.Where(seg => seg.source != Config.LatexOptions.MACROS_SOURCE_NAME)
			.SelectMany(seg => comp.ExtractSpells(Game, seg.code.ToArray(), seg.source))
			.ToAsyncEnumerable();
	}
}