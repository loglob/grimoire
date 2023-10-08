
/// <summary>
/// Scraper for processing LaTeX files
/// </summary>
public record LatexFiles(Config.LatexSource Cfg) : ISource
{
	private readonly Latex latex = new(Cfg.Options);

	public IAsyncEnumerable<Spell> Spells()
	{
		foreach(var f in Cfg.MacroFiles)
			latex.LearnMacros(File.ReadLines(f));

		var segments = Cfg.Files
			.SelectMany(kvp => kvp.Value.Select(file => (kvp.Key, file)))
			.SelectMany(x => Latex.CodeSegments(File.ReadAllLines(x.file), x.Key))
			.ToList();

		foreach (var seg in segments.Where(seg => seg.source == Latex.MACROS_SOURCE_NAME))
			latex.LearnMacros(seg.code);
		
		return segments
			.Where(seg => seg.source != Latex.MACROS_SOURCE_NAME)
			.SelectMany(seg => latex.ExtractSpells(seg.code.ToArray(), seg.source))
			.ToAsyncEnumerable();
	}
}