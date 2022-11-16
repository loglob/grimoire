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
	const string ANCHOR = "%% GRIMOIRE include";

	/// <summary>
	/// Finds all documents that include the ANCHOR to mark them as spell files
	/// </summary>
	private async Task<IEnumerable<Document>> spellFiles()
	{
		if(!await overleaf.Available)
			throw new Exception($"Overleaf instance at {overleaf.Host} isn't ready");

		return (await Util.Cached("cache/overleaf_documents", project.GetDocuments))
			.Where(d => d.Lines.Take(10).Any(s => s == ANCHOR));
	}

	public async Task<IEnumerable<Spell>> Spells(string source)
		=> (await spellFiles()).SelectMany(f => latex.ExtractSpells(f.Lines, source));

}
