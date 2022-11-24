using Olspy;

public class Overleaf
{
	/// <summary>
	/// The configuration for the overleaf scraper
	/// </summary>
	/// <param name="projectID"> The project ID to scrape for spells. Mandatory </param>
	/// <param name="password"> The password to connect to overleaf with. See olspy's documentation for details.</param>
	/// <param name="latex"> The latex configuration to use.</param>
	/// <param name="user"> The username to connect to overleaf with. See olspy's documentation for details.</param>
	/// <param name="host">The hostname of the overleaf server. If blank, determined automatically</param>
	public readonly record struct Config(string projectID, string password, Latex.Config latex, string? user = null, string? host = null);

	private readonly Olspy.Overleaf overleaf;
	private readonly Olspy.Project project;
	private readonly Latex latex;

	public Overleaf(Config config)
	{
		this.overleaf = (config.host is string s) ?
			this.overleaf = new Olspy.Overleaf(s) :
			Olspy.Overleaf.RunningInstance;

		this.project = this.overleaf.Open(config.projectID);
		this.latex = new Latex(config.latex);

		if(config.user is string u)
			this.overleaf.SetCredentials(config.password, u);
		else
			this.overleaf.SetCredentials(config.password);
	}

	/// <summary>
	/// When encountered in the first 10 lines of an overleaf document, scrapes that file for spells.
	/// Optionally followed by a source name.
	/// </summary>
	const string ANCHOR = "%% grimoire include";

	/// <summary>
	/// Marks the following lines to be included.
	/// Accepts a following source name.
	/// When present in a file, only marked code is included
	/// </summary>
	const string SECTION_START_ANCHOR = "%% grimoire begin";

	/// <summary>
	/// Terminates a section opened with SECTION_START_ANCHOR
	/// </summary>
	const string SECTION_END_ANCHOR = "%% grimoire end";

	const string DOC_START = @"\begin{document}";
	const string DOC_END = @"\end{document}";

	/// <summary>
	/// If this is given as source, use that code snippet to learn macros instead of extracting spells
	/// </summary>
	const string MACROS_SOURCE_NAME = "macros";

	internal static IEnumerable<(string source, IEnumerable<string> code)> GetCode(IEnumerable<Document> docs)
	{
		foreach(var d in docs)
		{
			var src = d.Lines.Take(10).FirstOrDefault(x => x.StartsWith(ANCHOR))?.Substring(ANCHOR.Length)?.Trim();

			if(src is null)
				continue;

			var opens = d.Lines.Indexed()
				.Where(xi => xi.value.StartsWith(SECTION_START_ANCHOR))
				.Select(xi => xi.index);

			if(opens.Any()) foreach(var o in opens)
			{
				var nSrc = d.Lines[o].Substring(SECTION_START_ANCHOR.Length).Trim();

				if(string.IsNullOrWhiteSpace(nSrc))
					nSrc = src;

				yield return (nSrc, d.Lines
					.Skip(o + 1)
					.TakeWhile(x => !x.StartsWith(SECTION_END_ANCHOR)));
			}
			else
				yield return (src, d.Lines
					.StartedWith(DOC_START)
					.EndedBy(DOC_END)
					.EndedBy(SECTION_END_ANCHOR));
		}
	}

	public async Task<IEnumerable<Spell>> Spells()
	{
		var docs = await Util.Cached("cache/overleaf_documents", async() => {
			if(!await overleaf.Available)
				throw new Exception($"Overleaf instance at {overleaf.Host} isn't ready");

			return await project.GetDocuments();
		});

		var snippets = GetCode(docs).ToList();

		foreach (var m in snippets.Where(s => s.source == MACROS_SOURCE_NAME))
			latex.LearnMacros(m.code);

		return snippets
			.Where(s => s.source != MACROS_SOURCE_NAME)
			.SelectMany(s => latex.ExtractSpells(s.code, s.source));
	}

}
