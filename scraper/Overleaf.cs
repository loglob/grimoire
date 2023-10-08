using Olspy;

public class Overleaf : ISource
{
	private readonly Olspy.Overleaf overleaf;
	private readonly Olspy.Project project;
	private readonly Latex latex;

	public Overleaf(Config.OverleafSource config)
	{
		this.overleaf = (config.Host is string s) ?
			this.overleaf = new Olspy.Overleaf(s) :
			Olspy.Overleaf.RunningInstance;

		this.project = this.overleaf.Open(config.ProjectID);
		this.latex = new Latex(config.Latex);

		if(config.User is string u)
			this.overleaf.SetCredentials(config.Password, u);
		else
			this.overleaf.SetCredentials(config.Password);
	}

	/// <summary>
	/// When encountered in the first 10 lines of an overleaf document, scrapes that file for spells.
	/// Optionally followed by a source name.
	/// </summary>
	const string INCLUDE_ANCHOR = "%% grimoire include";

	/// <summary>
	/// Retrieves all code segments from multiple documents.
	/// Filters out documents without an INCLUDE_ANCHOR
	/// </summary>
	internal static IEnumerable<(string source, IEnumerable<string> code)> GetCode(IEnumerable<Document> docs)
		=> docs.SelectWith(d => d.Lines
				.Take(10)
				.FirstOrDefault(x => x.StartsWith(INCLUDE_ANCHOR))
				?.Substring(INCLUDE_ANCHOR.Length)
				?.Trim())
			.Where(x => !(x.Item2 is null))
			.SelectMany(x => (x.Item2 is string src)
				? Latex.CodeSegments(x.Item1.Lines, src)
				: Enumerable.Empty<(string,IEnumerable<string>)>());

	public async IAsyncEnumerable<Spell> Spells()
	{
		var docs = await Util.Cached("cache/overleaf_documents", async() => {
			if(!await overleaf.Available)
				throw new Exception($"Overleaf instance at {overleaf.Host} isn't ready");

			return await project.GetDocuments();
		});

		var snippets = GetCode(docs).ToList();

		foreach (var m in snippets.Where(s => s.source == Latex.MACROS_SOURCE_NAME))
			latex.LearnMacros(m.code);

		foreach(var s in snippets
			.Where(s => s.source != Latex.MACROS_SOURCE_NAME)
			.SelectMany(s => latex.ExtractSpells(s.code, s.source)))
			yield return s;

	}
}
