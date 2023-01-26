
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

	public async IAsyncEnumerable<Spell> Spells()
	{
		var content = new List<(string src, string[] code)>();

		foreach (var f in files)
		{
			if(f.src == Latex.MACROS_SOURCE_NAME)
				latex.LearnMacros(File.ReadLines(f.file));
			else foreach(var seg in Latex.CodeSegments(await File.ReadAllLinesAsync(f.file), f.src))
			{
				if(seg.source == Latex.MACROS_SOURCE_NAME)
					latex.LearnMacros(seg.code);
				else
					content.Add((seg.source, seg.code.ToArray()));
			}
		}

		foreach (var sp in content.SelectMany(seg => latex.ExtractSpells(seg.code, seg.src)))
			yield return sp;
	}
}