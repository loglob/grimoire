using Olspy;

public class Overleaf : ISource
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
