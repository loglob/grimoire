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
	/// <param name="environments"> Maps latex environments onto equivalent HTML tags</param>
	public readonly record struct Config(string spellAnchor, string upcastAnchor, Dictionary<string, string> environments);

	private readonly Config config;
	private readonly Dictionary<string, string> macros = new Dictionary<string, string>{
		{ @"\\", "\n" },
		{ @"\{", "{" },
		{ @"\}", "}" },
		{ @"\[", "[" },
		{ @"\]", "]" },
		{ @"\,", " " },
		{ @"\ ", " " }
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
	internal Command latexCmd(string line, out int read, int? arity = null)
	{
		var parens = new Stack<char>();
		var args = new List<(bool, string)>();
		var arg = new StringBuilder();

        Util.AssertEqual('\\', line[0], "Expected a latex command");
        int len = 1 + Math.Max(1, line.Skip(1).TakeWhile(char.IsLetterOrDigit).Count());
        string name = line.Substring(0, len);
		bool esc = false;

		for (read = len; read < line.Length; read++)
		{
			if(line[read] == '\n')
				break;

			char c = line[read];

            if(!esc && (c == ']' || c == '}'))
            {
            	if(parens.Count == 0)
					break;

				Util.AssertEqual(parens.Pop(), c, "Bad parenthesis");

                if(parens.Count == 0)
                {
					args.Add((c == '}', arg.ToString()));
                    arg.Clear();

					if(arity is int a && args.Count(x => x.Item1) >= a)
					{
						read++;
						break;
					}

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
				if(c == '\n')
					break;
				else if(char.IsWhiteSpace(c))
                    continue;
				else
	                break;
            }

			if(c == '\\')
				esc = !esc;
			else
				esc = false;
		}

		line = line.Substring(read);
        Util.AssertEqual(0, parens.Count, "Unmatched parenthesis");

        return new Command(name, args.ToArray());

	}

	internal (string inner, Command closing) closeEnvironment(string code, out int skip)
	{
		var bs = new Queue<int>(code.Indices(@"\begin"));
		var es = new Queue<int>(code.Indices(@"\end"));

		while(es.Any())
		{
			if(!bs.Any() || es.Peek() < bs.Peek())
			{
				int ind = es.Dequeue();
				var cmd = latexCmd(code.Substring(ind), out skip, 1);
				skip += ind;

				return (code.Substring(0, ind), cmd);
			}
			else
			{
				bs.Dequeue();
				es.Dequeue();
			}
		}

		throw new FormatException("Missing closing \\end");
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
			var cmd = latexCmd(code.Substring(i), out var _, 2);

			if(cmd.arguments.Count() < 2)
				continue;
			if(cmd.arguments.Any(a => !a.mandatory))
				continue;

			macros[cmd.arguments[0].value.Trim()] = cmd.arguments[1].value;
		}
	}

	private void expand(StringBuilder str, string code)
	{
		int w = 0, l;
		for (int i = 0; (i = code.IndexOf('\\', i)) >= 0; i += l)
		{
			var cmd = latexCmd(code.Substring(i), out l);
			str.Append(code, w, i - w);
			redo:

			if(macros.TryGetValue(cmd.name, out string subst))
			{
				if(new Regex(@"^\\(.|\w+)$").IsMatch(subst) && subst != cmd.name)
				{// special case to handle aliasing. NOT PROPER LATEX
					cmd = cmd with {name = subst};
					goto redo;
				}
				if(cmd.arguments.Length > 0)
					Console.Error.WriteLine($"[Warn] discarding {cmd.arguments.Length} argument(s) to {cmd.name}");

				expand(str, subst);
			}
			else switch(cmd.name)
			{
				case @"\end":
					Console.Error.WriteLine($"Orphaned \\end{{{cmd.arguments.FirstOrDefault(a => a.mandatory).value}}}");
				break;

				case @"\begin":
				{
					var env = cmd.arguments.FirstOrDefault(a => a.mandatory).value;

					if(config.environments.TryGetValue(env, out string tag))
					{
						var match = closeEnvironment(code.Substring(i + l), out int ll);
						l += ll;

						str.Append($"<{tag} class=\"{env}\">");

						switch(tag)
						{
							case "table":
							{
								bool header = true;
								foreach(var row in match.inner.Split('\n'))
								{
									str.Append("<tr>");

									foreach (var cell in row.Split('&'))
									{
										str.Append(header ? "<th>" : "<td>");
										expand(str, cell);
										str.Append(header ? "</th>" : "</td>");
									}

									str.Append("</tr>");
									header = false;
								}
							}
							break;

							case "ul":
							case "il":
							{
								foreach(var il in match.inner.Split(@"\Item"))
								{
									str.Append("<il>");
									expand(str, il);
									str.Append("</il>");
								}
							}
							break;

							default:
								expand(str, match.inner);
							break;
						}

						str.Append($"</{tag}>");
					}
					else if(env is null)
						Console.Error.WriteLine("[Warn] skipping malformed \\begin");
					else
						Console.Error.WriteLine($"[Warn] skipping unknown environment {env}");
				}
				break;

				case @"\textit":
				{
					if(!cmd.arguments.Any(a => a.mandatory))
					{
						Console.Error.WriteLine($"[Warn] skipping malformed \\textit");
						continue;
					}
					if(cmd.arguments.Count() > 1)
						Console.Error.WriteLine($"[Warn] discarding superfluous arguments to \\textit");

					str.Append("<i>");
					expand(str, cmd.arguments.First(a => a.mandatory).value);
					str.Append("</i>");
				}
				break;

				case @"\textbf":
				{
					if(!cmd.arguments.Any(a => a.mandatory))
					{
						Console.Error.WriteLine($"[Warn] skipping malformed \\textit");
						continue;
					}
					if(cmd.arguments.Count() > 1)
						Console.Error.WriteLine($"[Warn] discarding superfluous arguments to \\textit");

					str.Append("<b>");
					expand(str, cmd.arguments.First(a => a.mandatory).value);
					str.Append("</b>");
				}
				break;

				default:
					Console.Error.WriteLine($"[Warn] Discarding unknown macro: {cmd.name}");
				// don't increment w, leading to larger writeback
				break;
			}

			w = i + l;
		}

		str.Append(code.Substring(w));
	}

	/// <summary>
	/// Fully expands a code snippet
	/// Applies learned macros recursively
	/// </summary>
	/// <param name="code">The latex source code to expand</param>
	/// <returns></returns>
	public string Expand(string code)
	{
		var str = new StringBuilder();
		expand(str, code);
		return str.ToString();
	}

	internal Spell ExtractSpell(string code, string source)
	{
        var spel = latexCmd(code, out int len, 7);
		var spl = code.Substring(len).Split(config.upcastAnchor, 2, StringSplitOptions.TrimEntries);

		var props = spel.arguments.Where(a => a.mandatory).Select(p => Expand(p.value)).ToArray();
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
