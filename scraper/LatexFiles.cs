
/// <summary>
/// Scraper for processing LaTeX files
/// </summary>
public record LatexFiles<TSpell>(IGame<TSpell> Game, Config.LatexSource Conf) : ISource<TSpell>
{
	private readonly Latex latex = new(Conf.Options);

	public IAsyncEnumerable<TSpell> Spells()
	{
		foreach(var f in Conf.MacroFiles)
			latex.LearnMacros(File.ReadLines(f));

		var segments = Conf.Files
			.SelectMany(kvp => kvp.Value.Select(file => (kvp.Key, file)))
			.SelectMany(x => Latex.CodeSegments(File.ReadAllLines(x.file), x.Key))
			.ToList();

		foreach (var seg in segments.Where(seg => seg.source == Latex.MACROS_SOURCE_NAME))
			latex.LearnMacros(seg.code);
		
		return segments
			.Where(seg => seg.source != Latex.MACROS_SOURCE_NAME)
			.SelectMany(seg => latex.ExtractSpells(Game, seg.code.ToArray(), seg.source))
			.ToAsyncEnumerable();
	}
}