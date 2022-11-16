using System.Text;
using System.Text.RegularExpressions;

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
	private readonly Dictionary<string, string> macros = new Dictionary<string, string>{
		{ @"\\", "\n" },
		{ @"\{", "{" },
		{ @"\}", "}" },
		{ @"\[", "[" },
		{ @"\]", "]" }
	};

	public Latex(Config config)
	{
		this.config = config;
	}

    internal record Command(string name, (bool mandatory, string value)[] arguments);

	/// <summary>
	/// Joins lines, handles comments, ensures that newlines = paragraph breaks.
	/// De-escapes \% and nothing else
	/// </summary>
	/// <param name="lines"></param>
	/// <returns></returns>
	public static string JoinLines(string[] lines)
	{
		var sb = new StringBuilder();
		bool wasEmpty = true;

		foreach (var l in lines.Select(l => l.Trim()))
		{
			if(l.Length == 0)
			{
				if(!wasEmpty)
					sb.Append('\n');

				wasEmpty = true;
				continue;
			}
			else if(l[0] == '%') // comment-lines are completely ignored
				continue;
			else
				wasEmpty = false;

			int w = 0;
			bool esc = false;

			for (int i = 0; i <= l.Length; i++)
			{
				if(i == l.Length)
				{
					sb.Append(l.Substring(w));
					break;
				}
				if(l[i] == '%')
				{
					sb.Append(l, w, esc ? i - w - 1 : i - w);
					w = i;

					if(!esc)
						break;
				}
				else if(l[i] == '\\')
					esc = !esc;
				else
					esc = false;
			}

			sb.Append(' ');
		}

		return sb.ToString();
	}

	/// <summary>
	/// Parses a latex command invocation.
	/// </summary>
	internal Command latexCmd(ref string line, bool expandArgs = false)
	{
		var parens = new Stack<char>();
		var args = new List<(bool, string)>();
		var arg = new StringBuilder();

        Util.AssertEqual('\\', line[0], "Expected a latex command");
        int off = line.Skip(1).TakeWhile(char.IsLetterOrDigit).Count();

        if(off == 0)
            throw new FormatException($"Expected name after '\\', got '{line.Substring(1,10)}'");

        string name = line.Substring(1, off);
		bool esc = false;

		for (int i = 1+off; i <= line.Length; i++)
		{
            if(i == line.Length)
            {
                line = "";
                break;
            }
			if(line[i] == '\n')
			{
				line = line.Substring(i + 1);
				break;
			}

			char c = line[i];

            if(!esc && (c == ']' || c == '}'))
            {
            	if(parens.Count == 0)
					break;

				Util.AssertEqual(parens.Pop(), c, "Bad parenthesis");

                if(parens.Count == 0)
                {
					string a = arg.ToString();
					args.Add((c == '}', expandArgs ? Expand(a) : a ));
                    arg.Clear();
                    continue;
                }
            }

            if(parens.Count > 0)
                arg.Append(c);

			if(!esc)
			{
				if(c == '[')
					parens.Push(']');
				else if(c == '{')
					parens.Push('}');
			}

            if(parens.Count == 0)
            {
				if(c == '\n' && i + 1 < line.Length && line[i + 1] == '\n')
					break;
				if(char.IsWhiteSpace(c))
                    continue;

                line = line.Substring(i);
                break;
            }

			if(c == '\\')
				esc = !esc;
			else
				esc = false;
		}

        Util.AssertEqual(0, parens.Count, "Unmatched parenthesis");

        return new Command(name, args.ToArray());

	}

	/// <summary>
	/// Extracts simple \newcommand definitions from the given latex source code
	/// Skips commands with arguments
	/// </summary>
	/// <param name="lines"></param>
	public void LearnMacros(string code)
	{
		var r = new Regex(@"\\(re)?newcommand");

		foreach(var i in r.Matches(code).Select(m => m.Index))
		{
			string ln = code.Substring(i);
			var cmd = latexCmd(ref ln);

			if(cmd.arguments.Count() < 2)
				continue;
			if(cmd.arguments.Take(2).Any(a => !a.mandatory))
				continue;

			macros[cmd.arguments[0].value.Trim()] = cmd.arguments[1].value;
		}
	}

	private void expand(StringBuilder str, string code)
	{
		var cmd = new Regex(@"\\(\\|\w+)\*?");
		int w = 0;

		while(true)
		{
			var m = cmd.Match(code, w);

			if(!m.Success)
			{
				str.Append(code.Substring(w));
				break;
			}


			if(macros.TryGetValue(m.Value, out string subst))
			{
				str.Append(code, w, m.Index - w);
				expand(str, subst);
			}
			else
			{
				str.Append(code, w, m.Index + m.Length - w);
				Console.Error.WriteLine($"Skipping unknown macro: {m.Value}");
			}

			w = m.Index + m.Length;
		}
	}

	/// <summary>
	/// Fully expands a code snippet
	/// Applies learned macros recursively
	/// </summary>
	/// <param name="code"></param>
	/// <returns></returns>
	public string Expand(string code)
	{
		var str = new StringBuilder();
		expand(str, code);
		return str.ToString();
	}

	internal Spell ExtractSpell(string code, string source)
	{
        var spel = latexCmd(ref code, true);
		var spl = code.Split(config.upcastAnchor, 2, StringSplitOptions.TrimEntries);

		var props = spel.arguments.Where(a => a.mandatory).Select(p => p.value).Take(7).ToArray();
		Util.AssertEqual(7, props.Length, "Bad arity of spell-defining function");

		var name = props[0];
		var lsr = Common.parseLevel(props[1]);
		var time = props[2];
		var range = props[3];

		string comp; string? mat;
		(comp, mat) = Common.parseParen(props[4]);

		var cd = Common.parseDuration(props[5]);
		var classes = props[6].Split(new[]{' ', '\t', ','}, StringSplitOptions.RemoveEmptyEntries).ToArray();

		return new Spell(
			name, source,
			lsr.school, lsr.level,
			time, lsr.ritual,
			range,
			comp, mat,
			cd.concentration, cd.duration,
			Expand(spl[0]), spl.Length > 1 ? Expand(spl[1]) : null,
			classes, null
		);
	}

	public IEnumerable<Spell> ExtractSpells(string doc, string source)
	{
		var code = doc.Split(@"\begin{document}", 2)[1].Split(@"\end{document}",2)[0];

		foreach(var snip in code.Spans(config.spellAnchor))
		{
			Spell spell;

			try
			{
				spell = ExtractSpell(snip, source);
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"At '{snip.Substring(0,25)}{(snip.Length > 25 ? "..." : "")}': {ex.Message}");
				continue;
			}

			yield return spell;
		}
	}
}
