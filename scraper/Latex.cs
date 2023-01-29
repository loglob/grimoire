using System.Text;
using System.Net;

/// <summary>
/// Scraper for processing LaTeX snippets
/// </summary>
public class Latex
{
#region TeX Lexer
	public abstract record Token();

	/// <summary>
	/// A reference to a macro, of the form \<name>.
	/// </summary>
	/// <param name="macro">The name of the referenced macro, without backslash</param>
	private sealed record MacroName(string macro) : Token
	{ public override string ToString() => $"\\{macro}"; }

	/// <summary>
	/// Any regular character. \n indicates a paragraph break, not a (source) line break
	/// </summary>
	private sealed record Character(char chr) : Token
	{ public override string ToString() => chr.ToString(); }

	/// <summary>
	/// TeX whitespace, which is discarded when searching for function arguments
	/// </summary>
	private sealed record WhiteSpace : Token
	{ public override string ToString() => " "; }

	/// <summary>
	/// Tokens enclosed by { and }
	/// </summary>
	private sealed record Braced(Token[] inner) : Token
	{ public override string ToString() => "{"+string.Join("", inner as object[])+"}"; }

	/// <summary>
	/// Reference to an argument, of the form #<number>
	/// </summary>
	private sealed record ArgumentRef(int number) : Token
	{ public override string ToString() => $"#{number}"; }

	/// <summary>
	/// A chunk that should not be escaped when translating to HTML
	/// </summary>
	private sealed record HtmlChunk(string data) : Token
	{ public override string ToString() => data; }

	private sealed record Environment(string env, Token[] inner) : Token
	{ public override string ToString() => $"\\begin{{{env}}} "+string.Join("", inner as object[])+$" \\end{{{env}}}"; }


	/// <summary>
	/// Tokenizes a single line
	/// </summary>
	/// <param name="input">A line without line breaks</param>
	/// <param name="context">The context</param>
	/// <returns></returns>
	private static IEnumerable<Token> tokenize(string input, Stack<List<Token>> context)
	{
		bool lastWS = false;

		for(int off = 0; off < input.Length; off++)
		{
			Token tk;

			switch(input[off])
			{
				case '\\':
				{
					var len = input.Skip(off + 1).TakeWhile(char.IsLetterOrDigit).Count();

					if(len == 0)
						len = 1;

					if(off + 1 >= input.Length)
					{
						Console.Error.WriteLine("[WARN] trailing, unmatched, unescaped '\\'");
						tk = new Character('\\');
					}
					else
						tk = new MacroName(input.Substring(off + 1, len == 0 ? 1 : len));

					off += len;
				}
				break;

				case '{':
					context.Push(new List<Token>());
				continue;

				case '}':
					if(context.TryPop(out var inner))
						tk = new Braced(inner.ToArray());
					else
					{
						Console.Error.WriteLine("[WARN] Discarding unmatched, unescaped '}'");
						tk = new Character('}');
					}
				break;

				case '%':
					yield break;

				case '#':
				{
					var len = input.Skip(off + 1).TakeWhile(char.IsDigit).Count();

					if(len == 0)
					{
						Console.Error.WriteLine("[WARN] orphaned, unescaped '#'");
						tk = new Character('#');
					}
					else
						tk = new ArgumentRef(int.Parse(input.Substring(off + 1, len)));

					off += len;
				}
				break;

				default:
					if(char.IsWhiteSpace(input[off]))
					{
						if(lastWS)
							continue;
						else
							tk = new WhiteSpace();
					}
					else
						tk = new Character(input[off]);
				break;
			}

			if(context.TryPeek(out var head))
				head.Add(tk);
			else
				yield return tk;

			lastWS = tk is WhiteSpace;
		}

		if(!lastWS)
			yield return new WhiteSpace();
	}

	private static IEnumerable<Token> tokenize(IEnumerable<string> lines)
	{
		var context = new Stack<List<Token>>();

		foreach (var l in lines)
		{
			if(string.IsNullOrWhiteSpace(l))
				yield return new Character('\n');
			else foreach (var t in tokenize(l, context))
				yield return t;
		}

		foreach(var l in context.Reverse())
		{
			Console.WriteLine("[WARN] unmatched and unescaped '{'");
			yield return new Character('{');

			foreach (var t in l)
				yield return t;
		}
	}

	private static string untokenize(IEnumerable<Token> tks)
		=> string.Join("", tks).Trim();

#endregion TeX Lexer

#region TeX Compiler
	/** A regular macro. */
	private sealed record Macro(int argc, Token[] replacement);

	private static Macro tagWrap(string tag)
		=> new Macro(1, new Token[]{ new HtmlChunk($"<{tag}>"), new ArgumentRef(1), new HtmlChunk($"</{tag}>") });

	private static Macro translate(char c)
		=> new Macro(0, new Token[] { new Character(c) });

	private static Macro constant(string html)
		=> new Macro(0, new Token[]{ new HtmlChunk(html) });

	/// <summary>
	/// The known macros.
	/// </summary>
	private readonly Dictionary<string, Macro> macros = new Dictionary<string, Macro>
	{
		{ "\\", translate('\n') },
		{ "{", translate('{') },
		{ "}", translate('}') },
		{ " ", translate(' ') },
		{ ",", translate(' ') },
		{ "%", translate('%') },
		{ "#", translate('#') },
		{ "textbf",			tagWrap("b") },
		{ "textit",			tagWrap("i") },
		{ "chapter",		tagWrap("h1") },
		{ "section",		tagWrap("h2") },
		{ "subsection",		tagWrap("h3") },
		{ "subsubsection",	tagWrap("h4")},
		{ "paragraph",		tagWrap("h5")},
		{ "subparagraph",	tagWrap("h6")},
		{ "[", constant("[")},
		{ "]", constant("]")}
	};

	/// <summary>
	/// Replaces every argument reference with its value in the given argument vector
	/// </summary>
	/// <param name="tks">A token list</param>
	/// <param name="argv">An argument vector</param>
	/// <returns></returns>
	private IEnumerable<Token> replaceArgs(IEnumerable<Token> tks, IEnumerable<Token>[] argv)
	{
		foreach (var tk in tks)
		{
			if(tk is ArgumentRef a)
			{
				if(a.number < 1 || a.number > argv.Length)
					Console.Error.WriteLine("[WARN] Discarding out-of-bound argument number");
				else foreach(var x in argv[a.number - 1])
					yield return x;
			}
			else if(tk is Braced b)
				yield return new Braced(replaceArgs(b.inner, argv).ToArray());
			else
				yield return tk;
		}
	}

	/// <summary>
	/// Advances until a non-whitespace token is found.
	/// Stream should be positioned on the first potential argument.
	/// </summary>
	/// <param name="tks">A token position</param>
	/// <returns>
	/// Whether a non-whitespace token was found before
	/// </returns>
	private bool skipWS(IEnumerator<Token> tks)
	{
		while(tks.Current is WhiteSpace)
		{
			if(!tks.MoveNext())
				return false;
		}

		return true;
	}

	/// <summary>
	/// Skips an optional argument. Stops on the first token after closing ]
	/// </summary>
	private bool skipOpt(IEnumerator<Token> tks)
	{
		if(!skipWS(tks))
			return false;

		if(tks.Current is Character c && c.chr == '[')
		{
			while(tks.MoveNext())
			{
				if(tks.Current is Character e && e.chr == ']')
					return tks.MoveNext();
			}

			return false;
		}
		else
			return true;
	}

	/// <summary>
	/// Retrieves an amount of arguments and advances the token position to their last value
	/// </summary>
	/// <param name="tks">
	///  The token position, positioned on the first possible argument token.
	///  Advances until the last argument token.
	/// </param>
	/// <param name="argc">The amount of arguments to retrieve</param>
	/// <returns>THe argument vectors. braced tokens are unpacked automatically</returns>
	private Token[][] getArgs(IEnumerator<Token> tks, int argc)
	{
		var args = new Token[argc][];

		for (int i = 0; i < argc; i++)
		{
			if((i > 0 && !tks.MoveNext()) || !skipWS(tks) || (tks.Current is Character c && c.chr == '\n'))
			{
				Console.Error.WriteLine($"[WARN] Incomplete call");

				for (int j = i; j < argc; j++)
					args[j] = new Token[0];

				break;
			}
			else if(tks.Current is Braced b)
				args[i] = b.inner.ToArray();
			else
				args[i] = new[]{ tks.Current };
		}

		return args;
	}

	/// <summary>
	/// Learns every macro definition from a raw token stream
	/// </summary>
	/// <param name="tks">A token stream returned by tokenize()</param>
	private void learnMacros(IEnumerator<Token> tks)
	{
		while(tks.MoveNext())
		{
			if(tks.Current is MacroName m && (m.macro == "newcommand" || m.macro == "renewcommand")) try
			{
				if(!tks.MoveNext())
					throw new Exception("Empty definition");

				var name = getArgs(tks, 1)[0].Where(t => !(t is WhiteSpace)).ToList();
				Util.AssertEqual(1, name.Count, "Multiple names in command definition");

				if(!(name[0] is MacroName mn))
					throw new FormatException("Expected a macro name in command definition");

				if(!tks.MoveNext() || !skipWS(tks))
					throw new Exception("No definition after macro name");

				int argc = 0;
				var argcSpec = new List<Token>();

				if(!skipOpt(tks, argcSpec))
					throw new FormatException("Bad arity specification");
				if(argcSpec.Count > 0)
					argc = int.Parse(untokenize(argcSpec));

				macros[mn.macro] = new Macro(argc, getArgs(tks, 1)[0]);
			}
			catch(Exception ex)
			{
				Console.Error.WriteLine($"Bad {m.macro}: {ex.Message}");
			}
		}
	}

	private void learnMacros(IEnumerable<Token> tks)
		=> learnMacros(tks.GetEnumerator());

	/// <summary>
	/// Expands macros in a raw token stream recursively
	/// </summary>
	/// <param name="tks">A token stream returned by tokenize()</param>
	/// <returns>An equivalent stream with every macro expanded</returns>
	private IEnumerable<Token> expand(IEnumerator<Token> tks)
	{
		while(tks.MoveNext())
		{
			if(tks.Current is MacroName mn)
			{
				if(macros.TryGetValue(mn.macro, out var m))
				{
					Token[][] args;

					if(m.argc == 0)
						args = new Token[0][];
					else if(!tks.MoveNext())
					{
						if(m.argc > 0)
							Console.Error.WriteLine($"No arguments to \\{mn.macro}");

						args = Enumerable.Repeat(new Token[0], m.argc).ToArray();
					}
					else
						args = getArgs(tks, m.argc);
					//Console.Error.WriteLine($"Expanding {mn.macro} -> {untokenize(m.replacement)}");
					//Console.Error.WriteLine($"With argv: {string.Join(' ', args.Select(a => '{' + untokenize(a) + '}'))}");

					tks = replaceArgs(m.replacement, args).FollowedBy(tks);
				}
				else
					yield return tks.Current;
			}
			else if(tks.Current is Braced br)
				yield return new Braced(expand((br.inner as IEnumerable<Token>).GetEnumerator()).ToArray());
			else
				yield return tks.Current;
		}
	}

	private IEnumerable<Token> expand(IEnumerable<Token> tks)
		=> expand(tks.GetEnumerator());

	/// <summary>
	/// Groups tokens together into [...] and environments, via the Bracketed and Environment tokens.
	/// </summary>
	/// <param name="tks">An expanded token stream returned by expand()</param>
	/// <returns>An equivalent stream with every environment translated to HTML primitives</returns>
	private IEnumerable<Token> collect(IEnumerator<Token> tks)
	{
		var state = new Stack<(string environ, List<Token> content)>();

		while(tks.MoveNext())
		{
			var tk = tks.Current;

			if(tk is MacroName mn && (mn.macro == "begin" || mn.macro == "end"))
			{
				if(!tks.MoveNext())
				{
					Console.Error.WriteLine($"Missing section name after {mn.macro}");
					continue;
				}

				var env = untokenize(getArgs(tks, 1)[0]);

				if(mn.macro == "begin")
				{
					state.Push((env, new List<Token>()));
					continue;
				}
				else if(state.TryPeek(out var top) && top.environ == env)
				{
					state.Pop();
					tk = new Environment(env, top.content.ToArray());
				}
				else
				{
					Console.Error.WriteLine($"Discarding unmatched \\end for {env}");
					continue;
				}

			}

			if(state.TryPeek(out var ec))
				ec.content.Add(tk);
			else
				yield return tk;
		}

		foreach (var x in state.Reverse())
		{
			Console.Error.WriteLine($"[WARN] Omitting unmatched \\begin{{{x.environ}}}");

			foreach (var y in x.content)
				yield return y;
		}
	}

	private IEnumerable<Token> collect(IEnumerable<Token> tks)
		=> collect(tks.GetEnumerator());

	/// <summary>
	/// Skips an optional argument. Also skips leading whitespace
	/// </summary>
	/// <param name="tks">
	///  Positioned at the first possible '[' token.
	///  Advances until AFTER the closing ']'
	/// </param>
	/// <returns>
	///  False if token stream is completely whitespace, or ended before ']', true otherwise
	/// </returns>
	private bool skipOpt(IEnumerator<Token> tks, List<Token>? content = null)
	{
		if(!skipWS(tks))
			return false;
		if(tks.Current is Character open && open.chr == '[')
		{
			for(;;)
			{
				if(!tks.MoveNext())
					return false;
				if(tks.Current is Character close && close.chr == ']')
					return tks.MoveNext();
				if(!(content is null))
					content.Add(tks.Current);
			}
		}
		else
			return true;
	}

	/// <summary>
	/// Processes a fully expanded and collected token stream into a HTML string
	/// </summary>
	private void latexToHtml(IEnumerator<Token> tks, StringBuilder sb)
	{
		while(tks.MoveNext())
		{
			if(tks.Current is MacroName mn)
				Console.Error.WriteLine($"[WARN] Discarding unknown macro: \\{mn.macro}");
			else if(tks.Current is Character cr)
				sb.Append(cr.chr == '\n' ? "<br/>" : WebUtility.HtmlEncode(cr.chr.ToString()));
			else if(tks.Current is WhiteSpace)
				sb.Append(' ');
			else if(tks.Current is Braced br)
				latexToHtml((br.inner as IEnumerable<Token>).GetEnumerator(), sb);
			else if(tks.Current is ArgumentRef)
				Console.Error.WriteLine("[WARN] Discarding orphaned argument reference");
			else if(tks.Current is HtmlChunk ch)
				sb.Append(ch.data);
			else if(tks.Current is Environment env)
			{
				string name = config.environments.GetValueOrDefault(env.env, env.env);

				switch(name)
				{
					case "itemize":
					{
						sb.Append("<ul>");

						foreach (var point in env.inner.SplitBy(tk => tk is MacroName m && m.macro == "item"))
						{
							sb.Append("<li>");
							latexToHtml((point as IEnumerable<Token>).GetEnumerator(), sb);
							sb.Append("</li>");
						}

						sb.Append("</ul>");
					}
					break;

					case "tabular":
					{
						sb.Append("<table>");
						bool header = true;
						IEnumerable<Token> tokens = env.inner;

						if(tokens.SkipWhile(x => x is WhiteSpace).First() is Braced)
							tokens = tokens.SkipWhile(x => x is WhiteSpace).Skip(1);
						else
							Console.Error.WriteLine("Expected a format argument after \\begin{tabular}");

						foreach(var row in env.inner.SplitBy(tk => tk is Character c && c.chr == '\n'))
						{
							sb.Append("<tr>");

							foreach (var cell in row.SplitBy(tk => tk is Character c && c.chr == '&'))
							{
								sb.Append(header ? "<th>" : "<td>");
								latexToHtml((cell as IEnumerable<Token>).GetEnumerator(), sb);
								sb.Append(header ? "</th>" : "</td>");
							}

							sb.Append("</tr>");
							header = false;
						}

						sb.Append("</table>");
					}
					break;

					default:
					{
						Console.Error.WriteLine($"[WARN] Unknown environment {name}");
						sb.Append($"<div class=\"{name}\">");
						latexToHtml((env.inner as IEnumerable<Token>).GetEnumerator(), sb);
						sb.Append("</div>");
					}
					break;
				}
			}
			else
				throw new FormatException("Unhandled token kind");
		}
	}

	private string latexToHtml(IEnumerator<Token> tks)
	{
		var sb = new StringBuilder();
		latexToHtml(tks, sb);
		return sb.ToString();
	}

	private string latexToHtml(IEnumerable<Token> tks)
		=> latexToHtml(tks.GetEnumerator());

#endregion TeX Compiler

	/// <summary>
	///
	/// </summary>
	/// <param name="spellAnchor"> A latex command that initializes a spell description </param>
	/// <param name="upcastAnchor"> A latex command that initiates an upcast section </param>
	/// <param name="environments"> Maps latex environments onto equivalent HTML tags</param>
	public record class Config(string spellAnchor, string upcastAnchor, Dictionary<string, string> environments);

	private readonly Config config;


	public Latex(Config config)
	{
		this.config = config;
	}

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
	public const string MACROS_SOURCE_NAME = "macros";

	public void LearnMacros(IEnumerable<string> source)
		=> learnMacros(collect(tokenize(source)));

	/// <summary>
	/// Extracts all code segments in a file.
	/// Code segments are either enclosed by SECTION_START_ANCHOR and SECTION_END_ANCHOR, or DOC_START and DOC_END.
	/// When the anchors are used, multiple segments may be returned
	/// </summary>
	public static IEnumerable<(string source, IEnumerable<string> code)> CodeSegments(string[] lines, string src)
	{
		var opens = lines.Indexed()
			.Where(xi => xi.value.StartsWith(SECTION_START_ANCHOR))
			.Select(xi => xi.index);

		if(opens.Any()) foreach(var o in opens)
		{
			var nSrc = lines[o].Substring(SECTION_START_ANCHOR.Length).Trim();

			if(string.IsNullOrWhiteSpace(nSrc))
				nSrc = src;

			yield return (nSrc, lines
				.Skip(o + 1)
				.TakeWhile(x => !x.StartsWith(SECTION_END_ANCHOR)));
		}
		else
			yield return (src, lines
				.StartedWith(DOC_START)
				.EndedBy(DOC_END)
				.EndedBy(SECTION_END_ANCHOR));
	}

	internal Spell ExtractSpell(IEnumerable<string> lines, string source)
	{
		var sect = lines.Split(config.upcastAnchor).ToArray();

		if(sect.Length > 2)
			throw new FormatException($"Too many occurrences of {config.upcastAnchor}, got {sect.Length}");

		var lPos = collect(expand(tokenize(sect[0]))).GetEnumerator();

		if(!lPos.MoveNext() || !skipOpt(lPos))
			throw new FormatException("Empty spell");

		var props = getArgs(lPos, 7).Select(untokenize).ToArray();

		var name = props[0];
		var lsr = Common.parseLevel(props[1]);
		var tr = Common.maybeSplitOn(props[2], ",");
		var range = props[3];
		var vsm = Common.parseComponents(props[4]);
		var cd = Common.parseDuration(props[5]);
		var classes = props[6].Split(new[]{' ', '\t', ','}, StringSplitOptions.RemoveEmptyEntries).ToArray();

		string desc = latexToHtml(lPos);
		string? upcast = (sect.Length > 1)
			? latexToHtml(collect(expand(tokenize(sect[1]))))
			: null;

		return new Spell(
			name, source,
			lsr.school, lsr.level,
			tr.left, tr.right, lsr.ritual,
			range,
			vsm.verbal, vsm.somatic, vsm.material,
			cd.concentration, cd.duration,
			desc,
			upcast,
			classes, null
		);
	}

	public IEnumerable<Spell> ExtractSpells(IEnumerable<string> lines, string source)
	{
		Console.WriteLine($"Extracting LATEX spells for {source}....");
		const string DOC_BEGIN = @"\begin{document}", DOC_END = @"\end{document}";

		if(lines.Any(l => l == DOC_BEGIN))
			lines = lines.SkipWhile(l => l != DOC_BEGIN).Skip(1).TakeWhile(l => l != DOC_END);

		foreach(var snip in lines.Spans(config.spellAnchor))
		{
			Spell spell;

			try
			{
				spell = ExtractSpell(snip.Trim(string.IsNullOrWhiteSpace), source);
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"At '{snip[0].Substring(0,Math.Min(snip[0].Length, 25))}...': {ex.Message}\n{ex.StackTrace}");
				continue;
			}

			yield return spell;
		}
	}
}
