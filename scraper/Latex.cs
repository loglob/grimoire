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

    internal record Command(string name, (bool,string)[] arguments);

	/// <summary>
	/// Parses a latex command invocation.
	/// </summary>
	internal Command latexCmd(ref string line)
	{
		var parens = new Stack<char>();
		var args = new List<(bool, string)>();
		var arg = new StringBuilder();

        Util.AssertEqual('\\', line[0], "Expected a latex command");
        int off = line.Skip(1).TakeWhile(char.IsLetterOrDigit).Count();

        if(off == 0)
            throw new FormatException($"Expected name after '\\', got '{line.Substring(1,10)}'");

        string name = line.Substring(1, off);

		for (int i = 1+off; i <= line.Length; i++)
		{
            if(i == line.Length)
            {
                line = "";
                break;
            }

            char c = line[i];

            if(parens.Count == 0)
            {
                if(char.IsWhiteSpace(c))
                    continue;
                
                line = line.Substring(i);
                break;
            }

            if(c == ']' || c == '}')
            {
                Util.AssertEqual(parens.Pop(), c, "Bad parenthesis");

                if(parens.Count == 0)
                {
                    args.Add((c == '}', arg.ToString()));
                    arg.Clear();
                    continue;
                }
            }
            
            if(parens.Count > 0)
                arg.Append(c);
            
            if(c == '[')
                parens.Push(']');
            else if(c == '{')
                parens.Push('}');
		}

        Util.AssertEqual(0, parens.Count, "Unmatched parenthesis");
        
        return new Command(name, args.ToArray());

	}

	internal Spell ExtractSpell(List<string> lines)
	{
        string l0 = lines[0];
        var spel = latexCmd(ref l0);

        if(string.IsNullOrWhiteSpace(l0))
            lines.RemoveAt(0);
        else
            lines[0] = l0;

		throw new Exception($": {spel.name}: not implemented");
	}

	public IEnumerable<Spell> ExtractSpells(string[] lines, string source)
	{
		var indices = Enumerable.Range(0, lines.Length)
			.Where(i => lines[i].TrimStart().StartsWith(config.spellAnchor))
			.ToArray();

		for (int i = 0; i < indices.Length; i++)
		{
			Spell spell;

			try
			{
                int until = (i == indices.Length - 1) ? lines.Length : indices[i + 1];
				var ll = splLines(new ArraySegment<string>(lines, indices[i], until - indices[i]));
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
