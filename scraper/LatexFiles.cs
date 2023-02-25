
/// <summary>
/// Scraper for processing LaTeX files
/// </summary>
public class LatexFiles : ISource
{
	private readonly Latex latex;
	private readonly (string src, string file)[] files;

	public LatexFiles(Latex.Config cfg, IEnumerable<(string src, string file)> files)
	{
		this.latex = new Latex(cfg);
		this.files = files.ToArray();
	}

	public IAsyncEnumerable<Spell> Spells()
	{
		foreach(var f in files.Where(f => f.src == Latex.MACROS_SOURCE_NAME))
			latex.LearnMacros(File.ReadLines(f.file));

		var segments = files
			.Where(f => f.src != Latex.MACROS_SOURCE_NAME)
			.SelectMany(f => Latex.CodeSegments(File.ReadAllLines(f.file), f.src))
			.ToList();

		foreach (var seg in segments.Where(seg => seg.source == Latex.MACROS_SOURCE_NAME))
			latex.LearnMacros(seg.code);
		
		return segments
			.Where(seg => seg.source != Latex.MACROS_SOURCE_NAME)
			.SelectMany(seg => latex.ExtractSpells(seg.code.ToArray(), seg.source))
			.ToAsyncEnumerable();
	}
}