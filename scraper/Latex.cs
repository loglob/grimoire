using System.Text;
using System.Net;

/// <summary>
/// Scraper for processing LaTeX snippets
/// </summary>
public record Latex(Config.LatexOptions Conf)
{
#region TeX Lexer
	public abstract record Token();

	/// <summary>
	/// A reference to a macro, of the form \<name>.
	/// </summary>
	/// <param name="macro">The name of the referenced macro, without backslash</param>
	public sealed record MacroName(string macro) : Token
	{ public override string ToString() => $"\\{macro}"; }

	/// <summary>
	/// Any regular character. \n indicates a paragraph break, not a (source) line break
	/// </summary>
	public sealed record Character(char chr) : Token
	{ public override string ToString() => chr.ToString(); }

	/// <summary>
	/// TeX whitespace, which is discarded when searching for function arguments
	/// </summary>
	public sealed record WhiteSpace : Token
	{ public override string ToString() => " "; }

	/// <summary>
	/// Tokens enclosed by { and }
	/// </summary>
	private sealed record Braced(Token[] inner) : Token
	{ public override string ToString() => "{"+string.Join("", inner as object[])+"}"; }

	/// <summary>
	/// Reference to an argument, of the form #<number>
	/// </summary>
	public sealed record ArgumentRef(int number) : Token
	{ public override string ToString() => $"#{number}"; }

	/// <summary>
	/// A chunk that should not be escaped when translating to HTML
	/// </summary>
	public sealed record HtmlChunk(string data) : Token
	{ public override string ToString() => data; }

	public sealed record Environment(string env, Token[] inner) : Token
	{ public override string ToString() => $"\\begin{{{env}}} "+string.Join("", inner as object[])+$" \\end{{{env}}}"; }

	/// <summary>
	///  Special token for "\\" since it's treated differently depending on context
	/// </summary>
	public sealed record BackBack() : Token
	{ public override string ToString() => "\n"; }


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
					else if(len == 1 && input[off + 1] == '<')
					{
						// HTML chunk
						int matching = input.IndexOf("\\>", off + 2);

						if(matching == -1)
						{
							// swallow the illegal symbol and continue
							Console.Error.WriteLine("[WARN] Unmatched HTML chunk start. Multiline chunks not allowed.");
							off += 2;
							continue;
						}

						tk = new HtmlChunk(input.Substring(off + 2, matching - off - 2));
						off = matching + 1;
					}
					else
					{
						tk = new MacroName(input.Substring(off + 1, len == 0 ? 1 : len));
						off += len;
					}
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

	public static string Untokenize(IEnumerable<Token> tks)
		=> string.Join("", tks).Trim();

#endregion TeX Lexer

#region TeX Compiler
	/** A regular macro. */
	private sealed record Macro(int argc, Token[]? opt, Token[] replacement);

	private static Macro tagWrap(string tag)
		=> new(1, null, new Token[]{ new HtmlChunk($"<{tag}>"), new ArgumentRef(1), new HtmlChunk($"</{tag}>") });

	private static Macro translate(char c)
		=> new(0, null, new Token[] { new Character(c) });

	private static Macro constant(string html)
		=> new(0, null, new Token[]{ new HtmlChunk(html) });

	/// <summary>
	/// The known macros.
	/// </summary>
	private readonly Dictionary<string, Macro> macros = new Dictionary<string, Macro>
	{
		{ "\\", new(0, null, new Token[]{ new BackBack() }) },
		{ "{", translate('{') },
		{ "}", translate('}') },
		{ " ", translate(' ') },
		{ ",", translate(' ') },
		{ "%", translate('%') },
		{ "#", translate('#') },
		{ "$", constant("$") },
		{ "&", constant("&") },
		{ "textbf",			tagWrap("b") },
		{ "textit",			tagWrap("i") },
		{ "chapter",		tagWrap("h1") },
		{ "section",		tagWrap("h2") },
		{ "subsection",		tagWrap("h3") },
		{ "subsubsection",	tagWrap("h4")},
		{ "paragraph",		tagWrap("h5")},
		{ "subparagraph",	tagWrap("h6")},
		{ "[", translate(' ')},
		{ "]", translate(' ')}
	};

	/// <summary>
	/// A list of special macros that should not be expanded and not warned about
	/// </summary>
	private static readonly HashSet<string> specialMacros = new HashSet<string>{
		"begin", "end", "item"
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
					Console.Error.WriteLine($"[WARN] Discarding out-of-bound argument number #{a.number}");
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
	public static bool SkipWS(IEnumerator<Token> tks)
	{
		while(tks.Current is WhiteSpace)
		{
			if(!tks.MoveNext())
				return false;
		}

		return true;
	}

	public static ArraySegment<Token> SkipWS(ArraySegment<Token> tks)
	{
		int s;

		for (s = 0; s < tks.Count && tks[s] is WhiteSpace; ++s);
		
		return tks.Slice(s);
	}

	/// <summary>
	///  Skips an optional argument. Stops on the first token after closing ].
	/// </summary>
	/// <param name="arg">
	///  The read optional argument.
	///  Null if no optional argument is given.
	///  If the closing ']' is missing, reads all available tokens.
	/// </param>
	/// <returns>
	///  True if there are any tokens after the optional argument
	/// </returns>
	public static bool SkipOpt(IEnumerator<Token> tks, out List<Token>? arg)
	{
		arg = null;

		if(!SkipWS(tks))
			return false;

		if(tks.Current is Character c && c.chr == '[')
		{
			arg = new List<Token>();

			while(tks.MoveNext())
			{
				if(tks.Current is Character e && e.chr == ']')
					return tks.MoveNext();

				arg.Add(tks.Current);
			}

			return false;
		}
		else
			return true;
	}

	public static ArraySegment<Token> SkipOpt(ArraySegment<Token> tks, out ArraySegment<Token>? arg)
	{
		arg = null;

		tks = SkipWS(tks);

		if(tks.Count > 0 && tks[0] is Character o && o.chr == '[')
		{
			int len;

			for (len = 0; 1 + len < tks.Count; ++len)
			{
				if(tks[1 + len] is Character c && c.chr == ']')
				{
					arg = tks.Slice(1, len);
					return tks.Slice(len + 2);
				}
			}
		}

		return tks;
	}

	public static bool SkipOpt(IEnumerator<Token> tks)
		=> SkipOpt(tks, out var _);

	/// <summary>
	/// Retrieves an amount of arguments and advances the token position to their last value
	/// </summary>
	/// <param name="tks">
	///  The token position, positioned on the first possible argument token.
	///  Advances until the last argument token.
	/// </param>
	/// <param name="argc">The amount of arguments to retrieve</param>
	/// <returns>THe argument vectors. braced tokens are unpacked automatically</returns>
	public static Token[][] GetArgs(IEnumerator<Token> tks, int argc, Token[]? opt = null)
	{
		var args = new Token[argc][];

		for (int i = 0; i < argc; i++)
		{
			if((i > 0 && !tks.MoveNext()) || !SkipWS(tks) || (tks.Current is Character c && c.chr == '\n'))
			{
				Console.Error.WriteLine($"[WARN] Incomplete call");

				for (int j = i; j < argc; j++)
					args[j] = new Token[0];

				break;
			}
			else if(i == 0 && !(opt is null))
			{
				SkipOpt(tks, out var optVal);
				args[0] = optVal?.ToArray() ?? opt;
			}
			else if(tks.Current is Braced b)
				args[i] = b.inner.ToArray();
			else
				args[i] = new[]{ tks.Current };
		}

		return args;
	}

	/// <summary>
	///  Grabs mandatory(!) arguments from a slice
	/// </summary>
	public static ArraySegment<Token> GetArgs(ArraySegment<Token> tks, ArraySegment<Token>[] argv)
	{
		for (int i = 0; i < argv.Length; ++i)
		{
			tks = SkipWS(tks);

			if(tks.Count == 0)
			{
				Console.Error.WriteLine($"[WARN] Incomplete call");
				return tks;
			}

			if(tks[0] is Braced b)
				argv[i] = b.inner;
			else
				argv[i] = tks.Slice(0, 1);
			
			tks = tks.Slice(1);			
		}

		return tks;
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

				var name = GetArgs(tks, 1)[0].Where(t => !(t is WhiteSpace)).ToList();
				Util.AssertEqual(1, name.Count, "Multiple names in command definition");

				if(!(name[0] is MacroName mn))
					throw new FormatException("Expected a macro name in command definition");

				if(!tks.MoveNext() || !SkipWS(tks))
					throw new Exception("No definition after macro name");

				if(!SkipOpt(tks, out var argcSpec))
					throw new FormatException("Bad arity specification");
				int argc = argcSpec is null ? 0 : int.Parse(Untokenize(argcSpec));

				List<Token>? optSpec = null;

				if(argc > 0 && !SkipOpt(tks, out optSpec))
					throw new FormatException("Bad arity specification");

				macros[mn.macro] = new Macro(argc, optSpec?.ToArray(), GetArgs(tks, 1)[0]);
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
						args = GetArgs(tks, m.argc, m.opt);
					//Console.Error.WriteLine($"Expanding {mn.macro} -> {untokenize(m.replacement)}");
					//Console.Error.WriteLine($"With argv: {string.Join(' ', args.Select(a => '{' + untokenize(a) + '}'))}");

					tks = replaceArgs(m.replacement, args).FollowedBy(tks);
				}
				else if(mn.macro == "includegraphics")
				{
					if(!tks.MoveNext() || !SkipOpt(tks))
					{
						Console.Error.WriteLine($"[WARN] No filename after \\includegraphics");
						continue;
					}

					var file = Untokenize(GetArgs(tks, 1)[0]);

					if(Conf.Images is null || !(Conf.Images.TryGetValue(file, out var replace)
						|| Conf.Images.TryGetValue(Path.GetFileName(file), out replace)))
					{
						Console.Error.WriteLine($"[WARN] Discarding use of unknown image '{file}' ");
						continue;
					}

					tks = tokenize(replace, new()).FollowedBy(tks);
				}
				else
				{
					if(!specialMacros.Contains(mn.macro))
						Console.WriteLine($"[WARN] Unknown macro '\\{mn.macro}'");

					yield return tks.Current;
				}
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
	/// Groups tokens together into environments segments via Environment tokens.
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

				var env = Untokenize(GetArgs(tks, 1)[0]);

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
	/// Processes a fully expanded and collected token stream into a HTML string
	/// </summary>
	private void latexToHtml(IEnumerator<Token> tks, StringBuilder sb)
	{
		while(tks.MoveNext())
		{
			switch(tks.Current)
			{
				case MacroName mn:
					Console.Error.WriteLine($"[WARN] Discarding unknown macro: \\{mn.macro}");
				break;

				case BackBack:
					sb.Append("<br/>");
				break;

				case Character cr:
					// ignore math-mode
					if(cr.chr == '$')
						continue;

					sb.Append(WebUtility.HtmlEncode(cr.chr.ToString()));
				break;

				case WhiteSpace:
					sb.Append(' ');
				break;

				case Braced br:
					latexToHtml((br.inner as IEnumerable<Token>).GetEnumerator(), sb);
				break;

				case ArgumentRef:
					Console.Error.WriteLine("[WARN] Discarding orphaned argument reference");
				break;

				case HtmlChunk ch:
					sb.Append(ch.data);
				break;

				case Environment env:
				{
					string name = Conf.Environments.GetValueOrDefault(env.env, env.env);

					switch(name)
					{
						case "itemize":
						{
							sb.Append((env.env != name) ? $"<ul class=\"{WebUtility.HtmlEncode(env.env)}\">" : "<ul>");

							foreach (var point in env.inner
									.SplitBy(tk => tk is MacroName m && m.macro == "item")
									.ToArray()
									.Trim(xs => xs.All(x => x is WhiteSpace)))
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
							var contents = Tabular(env.inner);
							sb.Append((env.env != name) ? $"<table  class=\"{WebUtility.HtmlEncode(env.env)}\">" : "<table>");
							bool header = true;

							foreach(var row in contents)
							{
								sb.Append("<tr>");

								foreach (var cell in row)
								{
									sb.Append(header ? "<th>" : "<td>");
									latexToHtml(cell.GetEnumerator(), sb);
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
				break;

				default:
					throw new FormatException("Unhandled token kind");
			}
		}
	}

	public string LatexToHtml(IEnumerator<Token> tks)
	{
		var sb = new StringBuilder();
		latexToHtml(tks, sb);
		return sb.ToString();
	}

	/// <summary>
	///  Parses tabular contents, including leading arguments.
	/// </summary>
	public static List<ArraySegment<Token>[]> Tabular(ArraySegment<Token> tks, out ArraySegment<Token>? optArgs)
	{
		optArgs = null;
		tks = SkipWS(tks);
		tks = SkipOpt(tks, out var _);

		var fmtArgs = new ArraySegment<Token>[1] { ArraySegment<Token>.Empty };
		tks = GetArgs(tks, fmtArgs);

		if(fmtArgs[0].Count > 0)
			optArgs = fmtArgs[0];

		string fmt = Untokenize(fmtArgs[0]);
		int width = 0;
		int braces = 0;

		foreach (var c in fmt)
		{
			if(Char.IsWhiteSpace(c))
				continue;
			else if(c == '{')
				++braces;
			else if(braces == 0)
				++width;
			else if(c == '}')
				--braces;
		}

		if(braces != 0)
			throw new FormatException($"Malformed table format: '{fmt}'");
		if(width == 0)
			throw new FormatException($"Bad table without format spec");

		var table = new List<ArraySegment<Token>[]>();

		return tks.SplitBy(t => t is BackBack)
			.SkipLastIf(x => x.All(c => c is WhiteSpace))
			.Select(r => r.SplitBy(t => t is Character s && s.chr == '&', width))
			.ToList();
	}
	
	public static List<ArraySegment<Token>[]> Tabular(ArraySegment<Token> tks)
		=> Tabular(tks, out var _);

	public string LatexToHtml(IEnumerable<Token> tks)
		=> LatexToHtml(tks.GetEnumerator()).Trim();

	public string LatexToHtml(ArraySegment<Token> tks)
		=> LatexToHtml(tks.GetEnumerator()).Trim();

#endregion TeX Compiler

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

	internal TSpell ExtractSpell<TSpell>(IGame<TSpell> game, IEnumerable<string> lines, string source)
	{
		var sect = Conf.UpcastAnchor == null
			? new[]{ lines.ToArray() }
			: lines.Split(Conf.UpcastAnchor).ToArray();

		if(sect.Length > 2)
			throw new FormatException($"Too many occurrences of {Conf.UpcastAnchor}, got {sect.Length}");
		
		return game.ExtractLatexSpell(this, source,
			collect(expand(tokenize(sect[0]))),
			(sect.Length > 1)
				? LatexToHtml(collect(expand(tokenize(sect[1]))))
				: null);
	}

	public IEnumerable<TSpell> ExtractSpells<TSpell>(IGame<TSpell> game, IEnumerable<string> lines, string source)
	{
		Console.WriteLine($"Extracting LATEX spells for {source}....");
		const string DOC_BEGIN = @"\begin{document}", DOC_END = @"\end{document}";

		if(lines.Any(l => l == DOC_BEGIN))
			lines = lines.SkipWhile(l => l != DOC_BEGIN).Skip(1).TakeWhile(l => l != DOC_END);

		foreach(var snip in lines.Spans(Conf.SpellAnchor))
		{
			TSpell spell;

			try
			{
				spell = ExtractSpell(game, snip.Trim(string.IsNullOrWhiteSpace), source);
			}
			catch(NotASpellException)
			{
				continue;
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
