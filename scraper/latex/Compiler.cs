using Grimoire.Util;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Grimoire.Latex;

using CodeSegment = Chain<Token>;

public record Compiler(Config.LatexOptions Conf, Log Log)
{
	/// <summary>
	///  A regular macro
	/// </summary>
	/// <param name="argc"> Total number of arguments (including the optional arg) </param>
	/// <param name="opt"> A default value for the first argument, if present </param>
	/// <param name="replacement"> The code to insert on expansion </param>
	/// <param name="force"> If true, this macro cannot be overwritten with \renewcommand </param>
	/// <returns></returns>
	internal sealed record Macro(int argc, ArraySegment<Token>? opt, ArraySegment<Token> replacement, bool force = false);

	public enum KnownEnvironments
	{
		Itemize,
		Tabular,
		Div
	}

	private static Macro tagWrap(string tag)
	{
		var pos = new Position("builtin/tagWrap", 0, 0);
		return new(1, null, new Token[]{ new HtmlChunk($"<{tag}>", pos), new ArgumentRef(1, pos), new HtmlChunk($"</{tag}>", pos) });
	}

	private static Macro translate(char c)
		=> new(0, null, new Token[] { new Character(c, new("builtin/translate", 0, 0)) });

	private static Macro constant(string html)
		=> new(0, null, new Token[]{ new HtmlChunk(html, new("builtin/constant", 0, 0)) });

	private static Macro discard()
		=> new(0, null, Array.Empty<Token>());

	/// <summary>
	/// The known macros.
	/// </summary>
	internal readonly Dictionary<string, Macro> macros = new()
	{
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
		{ "[", translate('[')},
		{ "]", translate(']')},
		{ "item", discard() },
		{ "newpage", discard() },
		{ "rowstyle", discard() }
	};

	internal readonly Token[]? upcastAnchor = Conf.UpcastAnchor is string ua ? new Lexer(Log).TokenizeUnchecked(new[]{ ua }, "builtin/upcast anchor") : null;
	internal readonly Token[] spellAnchor = new Lexer(Log).TokenizeUnchecked(new[]{ Conf.SpellAnchor }, "builtin/spell anchor");

	/// <summary>
	///  If true, record stack traces on latex errors and include line markers in HTML output.
	/// </summary>
	public bool Debug { get; init; } = false;

	/// <summary>
	///  Maps an environment token onto its corresponding enum entry.
	///  Utilizes the Config environment map.
	/// </summary>
	private KnownEnvironments envKind(EnvToken tk)
	{
		string env;
		switch(env = Conf.Environments.GetValueOrDefault(tk.Env, tk.Env))
		{
			case "tabular": return KnownEnvironments.Tabular;
			case "itemize": return KnownEnvironments.Itemize;
			case "div": return KnownEnvironments.Div;

			default:
				Log.Warn($"Unknown environment '{env}' referenced by {tk.At}");
			return KnownEnvironments.Div;
		}
	}

	/// <summary>
	///  Extracts a macro definition
	/// </summary>
	/// <param name="offset"> The index of the initial \(re)newcommand </param>
	private (string? name, Macro? m, int e) extractMacro(ArraySegment<Token> chain, int offset, bool pin)
	{
		var tk0 = chain[offset];

		int s, l, e;
		(s, l, e) = chain.LocateArg(offset + 1);

		if(s < 0)
		{
			Log.Warn($"No macro name after {tk0.At}, ignoring it");
			return (null, null, offset + 1);
		}

		var nameTk = chain.Slice(s,l);

		if(nameTk.Count(x => x is not WhiteSpace) != 1 || nameTk.FirstOrDefault(x => x is not WhiteSpace, null) is not MacroName name)
		{
			Log.Warn($"Invalid macro name '{Lexer.Untokenize(nameTk)}' at {tk0.Pos}, ignoring this definition");
			return (null, null, e);
		}

		var (args, ee) = chain.LocateArgs(e, 2, 3);

		int arity = 0;
		ArraySegment<Token>? opt = null;

		if(args[0].index >= 0)
		{
			var str = Lexer.Untokenize(chain.Slice(args[0].index, args[0].len));

			if(! int.TryParse(str, out arity) || arity < 0)
			{
				arity = 0;
				Log.Warn($"Ignoring invalid arity spec '{str}' at {tk0.Pos}");
			}
		}

		if(args[1].index >= 0)
		{
			opt = chain.Slice(args[1].index, args[1].len);

			if(arity == 0)
			{
				arity = 1;
				Log.Warn($"Optional argument with incorrect arity at {tk0.Pos}");
			}
		}

		if(args[2].index < 0)
		{
			Log.Warn($"Macro definition for {name} without body at {tk0.Pos}, ignoring it");
			return (null, null, ee);
		}

		return (name.Macro, new Macro(arity, opt, chain.Slice(args[2].index, args[2].len), pin), ee);
	}

	private static void putTrace(Stack<MacroName> trace)
		=> Console.Error.WriteLine("	stack trace: " + string.Join(" < ", trace.Select(x => x.At)));

	private CodeSegment insertArgs(ArraySegment<Token> inp, CodeSegment[] argv, Stack<MacroName> trace)
	{
		var builder = new ChainBuilder<Token>();
		int last = 0;

		foreach (var i in inp.FindIndices(x => x is ArgumentRef))
		{
			builder.Append(inp.Slice(last, i - last));

			var ar = (ArgumentRef)inp[i];

			if(ar.Number > 0 && ar.Number <= argv.Length)
			{
				if(Debug)
					Console.Error.WriteLine($"[TRACE] Expanding {ar.At} to '{Lexer.Untokenize(argv[ar.Number - 1])}'");

				builder.Append(argv[ar.Number - 1]);
			}
			else
			{
				Log.Warn($"Discarding out-of-bounds argument specifier {ar.At} (current expansion has only {argv.Length} arguments)");
				putTrace(trace);
			}

			last = i + 1;
		}

		builder.Append(inp.Slice(last));

		return builder.Build();
	}

	private void expand(ChainBuilder<Token> builder, CodeSegment inp, ref int gas, Stack<MacroName> trace)
	{
		if(--gas <= 0)
			throw new TimeoutException("Maximum expansion count exceeded");

		// index of the first entry requiring write-back
		int w = 0;

		for(int i = 0; i < inp.Length;)
		{
			if(inp[i] is ArgumentRef)
			{
				Log.Warn($"Unexpanded argument ref {inp[i].At}");
				putTrace(trace!);
			}

			if(inp[i] is not MacroName m)
			{
				++i;
				continue;
			}

			if(i > w) // write-back
				builder.Append(inp.Slice(w, i - w));

			if(m.Macro == "includegraphics")
			{
				var (imgArgs, end) = inp.LocateArgs(i + 1, 1, 2);
				w = i = end;
				var (s, l) = imgArgs[1];

				if(s < 0)
				{
					Log.Warn($"Discarding invalid {m.At}");
					continue;
				}

				var file = Lexer.Untokenize(inp.Items().Skip(s).Take(l)).Trim();

				if(Conf.Images is null || !(Conf.Images.TryGetValue(file, out var replace) || Conf.Images.TryGetValue(Path.GetFileName(file), out replace)))
				{
					Log.Warn($"Discarding use of unknown image '{file}' at {m.Pos}");
					continue;
				}

				builder.Append( new HtmlChunk(replace, new("builtin/images", 0, 0)) );
				continue;
			}
			if(! macros.TryGetValue(m.Macro, out var def))
			{
				Log.Warn($"Unknown macro {m.At}, discarding it");
				++w;
				++i;
				continue;
			}

			var (args, e) = inp.LocateArgs(i + 1, def.opt is null ? 0 : 1, def.argc);
			bool warned = false;
			var argVals = args.Select((il,j) => {
				if(il.index >= 0)
					return inp.Slice(il.index, il.len);
				else if(j == 0 && def.opt is ArraySegment<Token> opt)
					return new Chain<Token>(opt);
				else
				{
					if(! warned)
					{
						Log.Warn($"Partial call to {m.At} missing argument #{j+1}.");
						putTrace(trace!);
					}

					warned = true;
					return CodeSegment.Empty;
				}
			}).ToArray();

			if(Debug)
			{
				Console.Error.WriteLine($"[TRACE] Expanding '{Lexer.Untokenize(inp.Items().Take(e).Skip(i))}' at {m.Pos} to '{Lexer.Untokenize(def.replacement)}'");
				int j = 0;

				foreach (var x in argVals)
					Console.Error.WriteLine($"[TRACE]     Argument #{++j}: {Lexer.Untokenize(x)}");
			}

			trace?.Push(m);
			expand(builder, insertArgs(def.replacement, argVals, trace!), ref gas, trace!);
			trace?.Pop();

			i = w = e;
		}

		if(w < inp.Length)
			builder.Append(inp.Slice(w));
	}

	/// <summary>
	///  Loads macros from the given text into the active context
	/// </summary>
	public void LearnMacrosFrom(IEnumerable<string> lines, string filename)
		=> LearnMacrosFrom(new Lexer(Log).Tokenize(lines, filename ?? "<unknown>"));

	public void LearnMacrosFrom(ArraySegment<Token> code)
	{
		for (int i = 0; i < code.Count;)
		{
			if(code[i] is MacroName mn && mn.Macro is "newcommand" or "renewcommand" or "forcenewcommand")
			{
				var (n, m, e) = extractMacro(code, i, mn.Macro is "forcenewcommand");
				i = e;

				if(n is null || m is null)
					continue;

				if(macros.TryGetValue(n, out var old))
				{
					if(old.force)
						continue;

					if(mn.Macro is "newcommand")
						Log.Warn($"Overwriting definition for {n} without using \\renewcommand at {mn.Pos}"
								+ (old.replacement.FirstOrDefault() is Token tk ? $", previous definition at {tk.Pos}" : ""));
				}

				macros[n] = m;
			}
			else
				++i;
		}
	}

	public CodeSegment Expand(CodeSegment chain)
	{
		int gas = Conf.MaximumExpansions;
		var builder = new ChainBuilder<Token>();
		var st = new Stack<MacroName>();

		st.Push( new("compile", new("Expand()", 0, 0)) );

		expand(builder, chain, ref gas, st);

		return builder.Build();
	}

	/// <summary>
	///  Extracts the segments that define spells and invokes the game's extractor for them
	/// </summary>
	public IEnumerable<TSpell> ExtractSpells<TSpell>(IGame<TSpell> game, ArraySegment<Token> code, string source)
	{
		foreach (var (c,n) in code.FindIndices(spellAnchor, (x,y) => x.IsSame(y), false).Pairs())
		{
			var cc = c + spellAnchor.Length;
			var seg = n.HasValue ? code.Slice(cc, n.Value - cc) : code.Slice(cc);
			TSpell spell;

			try
			{
				spell = game.ExtractLatexSpell(this, source, new(seg));
			}
			catch(NotASpellException)
			{
				continue;
			}
			catch(Exception ex)
			{
				Log.Warn($"In {seg.PosRange()}: {ex.Message}");
				continue;
			}

			yield return spell;
		}
	}


	/// <summary>
	///  Compiles a token stream to a HTML document
	/// </summary>
	/// <param name="chain"></param>
	/// <param name="positionInfo"> If true, embed comments containing line numbers </param>
	/// <returns></returns>
	public string ToHTML(IEnumerable<Token> chain, bool trackRow = false)
	{
		StringBuilder doc = new();
		Stack<KnownEnvironments> envs = new();
		int row = 0;

		foreach (var tk in chain)
		{
			if(trackRow && tk.Pos.Row > row)
			{
				doc.Append($"\n<!-- Row {tk.Pos.Row} -->");

				row = tk.Pos.Row;
			}

			if(tk is MacroName)
				Log.Warn($"Discarding unexpanded macro {tk.At}");
			else if(tk is Character c)
			{
				// silently drop math mode
				if(c.Char == '$')
					continue;

				doc.Append(WebUtility.HtmlEncode(c.Char.ToString()));
			}
			else if(tk is OpenBrace or CloseBrace)
				continue;
			else if(tk is WhiteSpace)
				doc.Append(' ');
			else if(tk is ArgumentRef)
			{ }
			else if(tk is HtmlChunk hc)
				doc.Append(hc.Data);
			else if(tk is BackBack b)
			{
				doc.Append((envs.TryPeek(out var x) ? x : KnownEnvironments.Div) switch {
					KnownEnvironments.Itemize => "</li><li>" ,
					KnownEnvironments.Tabular => "</td></tr><tr><td>" ,
					_ => "<br/>"
				});
			}
			else if(tk is BeginEnv be)
			{
				var x = envKind(be);
				envs.Push(x);

				doc.Append(x switch {
					KnownEnvironments.Itemize => $"<ul class=\"{WebUtility.HtmlEncode(be.Env)}\"> <li>",
					KnownEnvironments.Tabular => $"<table class=\"{WebUtility.HtmlEncode(be.Env)}\"> <tr> <td>",
					KnownEnvironments.Div => $"<div class=\"{WebUtility.HtmlEncode(be.Env)}\">" ,
					_ => throw new UnreachableException()
				});
			}
			else if(tk is EndEnv)
			{
				doc.Append(envs.Pop() switch {
					KnownEnvironments.Itemize => "</li> </ul>",
					KnownEnvironments.Tabular => "</td> </tr> </table>",
					KnownEnvironments.Div     => "</div>" ,
					_ => throw new UnreachableException()
				});
			}
			else if(tk is AlignTab)
			{
				if(envs.TryPeek(out var x) && x == KnownEnvironments.Tabular)
					doc.Append("</td> <td>");
				else
					Log.Warn($"Dropping superfluous alignment tab at {tk.Pos}");
			}
			else
				throw new UnreachableException($"Incomplete pattern match: {tk}");
		}

		return doc.ToString().Trim();
	}

	public string ToHTML(CodeSegment code)
	{
		int gas = Conf.MaximumExpansions;
		var builder = new ChainBuilder<Token>();
		var st = new Stack<MacroName>();

		st.Push( new("compile", new("ToHTML()", 0, 0)) );


		expand(builder, code, ref gas, st);

		return ToHTML(builder.Items());
	}

	/// <summary>
	///  Expands and displays a segment
	/// </summary>
	public string ToString(CodeSegment seg)
	{
		int gas = Conf.MaximumExpansions;
		var builder = new ChainBuilder<Token>();
		var st = new Stack<MacroName>();

		st.Push( new("compile", new("ToString()", 0, 0)) );

		expand(builder, seg, ref gas, st);

		return Lexer.Display(builder.Items());
	}

	public string ToString(ArraySegment<Token> seg)
		=> ToString(new CodeSegment(seg));

}