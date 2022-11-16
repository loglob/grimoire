using System.Text;

/// <summary>
/// Scraper for processing LaTeX documents
/// </summary>
public class Latex
{
	/// <summary>
	///
	/// </summary>
	/// <param name="spellAnchor"> A latex command that initializes a spell description </param>
	/// <param name="upcastAnchor"> A latex command that initiates an upcast section </param>
	public readonly record struct Config(string spellAnchor, string upcastAnchor);

	private readonly Config config;

	public Latex(Config config)
	{
		this.config = config;
	}

	/// <summary>
	/// Turns latex line breaks into paragraph line breaks
	/// </summary>
	internal List<string> splLines(IEnumerable<string> lines)
		=> lines.SplitBy(string.IsNullOrWhiteSpace, true)
			.Select(x => string.Join(' ', x))
			.ToList();

	/// <summary>
	/// Extracts the mandatory arguments of a latex command.
	/// Discards []-arguments
	/// </summary>
	internal List<string> latexCmd(ref string line)
	{
		throw new Exception("not implemented");
		var parens = new Stack<bool>();
		var args = new List<string>();
		var arg = new StringBuilder();

		for (int i = 0; i < line.Length; i++)
		{
			switch(line[i])
			{
				case '[':
					parens.Push(false);
				continue;

				case '{':
					parens.Push(true);
				continue;

				case ']':
				{
					if (!parens.TryPop(out bool nec) || nec)
						throw new FormatException("Bad Parenthesis");
				}
				continue;

				case '}':
					break;
			}

//			if(parens.)
//				arg.Append(line[i]);
		}
	}

	internal Spell ExtractSpell(List<string> lines)
	{

		throw new Exception("not implemented");
	}

	public IEnumerable<Spell> ExtractSpells(string[] lines, string source)
	{
		var indices = Enumerable.Range(0, lines.Length)
			.Where(i => lines[i].TrimStart().StartsWith(config.spellAnchor))
			.ToArray();

		for (int i = 0; i < indices.Length - 1; i++)
		{
			Spell spell;

			try
			{
				var ll = splLines(new ArraySegment<string>(lines, indices[i], indices[i + 1] - indices[i]));
				ll[0].Substring(config.spellAnchor.Length);
				spell = ExtractSpell(ll);
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"At {lines[indices[i]]}: {ex.Message}");
				continue;
			}

			yield return spell;
		}

		yield break;
	}
}
