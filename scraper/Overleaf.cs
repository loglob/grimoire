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
	/// When encountered in the first 10 lines of an overleaf document, scrapes that file for spells
	/// </summary>
	const string SPELL_ANCHOR = "%% GRIMOIRE include";

	/// <summary>
	/// When encountered in the first 10 lines of an overleaf document, scrapes that file for macros
	/// </summary>
	const string MACRO_ANCHOR = "%% GRIMOIRE macros";

	internal static IEnumerable<string> WithAnchor(IEnumerable<Document> docs, string anchor)
		=> docs.Select(d => d.Lines).Where(l => l.Take(10).Any(l => l == anchor)).Select(Latex.JoinLines);

	public async Task<IEnumerable<Spell>> Spells(string source)
	{
		var docs = await Util.Cached("cache/overleaf_documents", async() => {
			if(!await overleaf.Available)
				throw new Exception($"Overleaf instance at {overleaf.Host} isn't ready");

			return await project.GetDocuments();
		});

		foreach(var f in WithAnchor(docs, MACRO_ANCHOR))
			latex.LearnMacros(f);

		return WithAnchor(docs, SPELL_ANCHOR).SelectMany(f => latex.ExtractSpells(f, source));
	}

}
