using Grimoire.Util;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace Grimoire.Latex;

using Code = Chain<Token>;

public record Compiler(Config.LatexOptions Conf, Log Log)
{
	/// <summary>
	///  A regular macro
	/// </summary>
	/// <param name="argc"> Total number of arguments (including the optional arg) </param>
	/// <param name="opt"> A default value for the first argument, if present </param>
	/// <param name="Replacement"> The code to insert on expansion </param>
	/// <param name="Force"> If true, this macro cannot be overwritten with \renewcommand </param>
	/// <returns></returns>
	internal sealed record Macro(ArgType[] Args, Code Replacement, bool Force = false)
	{
		public Macro(ArgType[] args, Token[] replacement, bool force = false) : this(args, new Code(replacement), force)
		{}
	}

	/// <summary>
	///  A decrement-only shared integer
	/// </summary>
	/// <param name="initial"> The maximum amount of times `decrement()` may return `true` </param>
	private record Gas(int initial)
	{
		private int value = initial;

		public bool decrement()
		{
			if(value <= 0)
				return false;
			else
			{
				--value;
				return true;
			}
		}
	}

	/// <summary>
	///  Context for macro expansions
	/// </summary>
	/// <param name="Builder"> The buffer to write expanded tokens to </param>
	/// <param name="Trace"> Optional trace of expansions for debugging </param>
	/// <param name="HtmlMode">If true, we're currently generating full-fledged HTML. Observable with `\IFHTML` </param>
	/// <returns></returns>
	private readonly record struct ExpandContext(ChainBuilder<Token> Builder, Gas Gas, ImmutableStack<MacroName>? Trace, bool HtmlMode)
	{
		public ExpandContext WithExpand(MacroName mn)
			=> new( Builder, Gas, Trace?.Push(mn), HtmlMode );

		public ExpandContext WithBuilder(ChainBuilder<Token> newBuilder)
			=> new( newBuilder, Gas, Trace, HtmlMode );

		public void PutTrace()
		{
			if(Trace is not null)
				Console.Error.WriteLine("	stack trace: " + string.Join(" < ", Trace.Select(x => x.At)));
		}
	}

	public enum KnownEnvironment
	{
		Itemize,
		Tabular,
		Div
	}

	/// <summary>
	///  A \CASE application that implements conditional compilation.
	/// </summary>
	/// <param name="Expanded"> Whether `subject` and case labels should be expanded before matching </param>
	/// <param name="Subject"> Tokens to match against </param>
	/// <param name="Cases"> List of cases, consisting of possible values of `Subject` and result code </param>
	/// <param name="Fallback"> Code to expand if no case matched </param>
	private readonly record struct Case(bool Expanded, Code Subject, List<(Code when, Code then)> Cases, Code Fallback);

	private static Macro tagWrap(string tag)
	{
		var pos = new Position("builtin/tagWrap", 0, 0);
		return new([ new MandatoryArg() ], [
			new ToggleMode(OutputMode.HTML_ONLY, pos),
			new HtmlChunk($"<{tag}>", pos),
			new ToggleMode(OutputMode.NORMAL, pos),
			new ArgumentRef(1, pos),
			new ToggleMode(OutputMode.HTML_ONLY, pos),
			new HtmlChunk($"</{tag}>", pos),
			new ToggleMode(OutputMode.NORMAL, pos)
		]);
	}

	private static Macro translate(char c)
		=> new([], [ new Character(c, new("builtin/translate", 0, 0)) ]);

	private static Macro constant(string html)
		=> new([], [ new HtmlChunk(html, new("builtin/constant", 0, 0)) ]);

	private static Macro discard()
		=> new([], []);

	private static Macro hyperref(Config.LatexOptions conf)
	{
		var p0 = new Position("builtin/hyperref", 0, 0);

		if(conf.Pdf is null)
			return new([ new OptionalArg(), new MandatoryArg() ], [ new ArgumentRef(2, p0) ]);

		return new([ new OptionalArg(), new MandatoryArg() ], [
			new ToggleMode(OutputMode.HTML_ONLY, p0),
			new HtmlChunk("<a href=\"" + conf.Pdf + "#nameddest=", p0),
			new ArgumentRef(1, p0),
			new HtmlChunk("\">", p0),
			new ToggleMode(OutputMode.NORMAL, p0),
			new ArgumentRef(2, p0),
			new ToggleMode(OutputMode.HTML_ONLY, p0),
			new HtmlChunk("</a>", p0),
			new ToggleMode(OutputMode.NORMAL, p0)
		]);
	}

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
		{ "rowstyle", discard() },
		{ "hyperref", hyperref(Conf) },
		{ "newline", new([], [ new BackBack(new("builtin/newline", 0, 0)) ]) }
	};

	internal readonly Token[]? upcastAnchor = Conf.UpcastAnchor is string ua ? new Lexer(Log).TokenizeUnchecked([ua], "builtin/upcast anchor") : null;
	internal readonly Token[] spellAnchor = new Lexer(Log).TokenizeUnchecked([Conf.SpellAnchor], "builtin/spell anchor");

	/// <summary>
	///  If true, record stack traces on latex errors and include line markers in HTML output.
	/// </summary>
	public bool Debug { get; init; } = false;
	/// <summary>
	///  Maps an environment token onto its corresponding enum entry.
	///  Utilizes the Config environment map.
	/// </summary>
	private KnownEnvironment envKind(EnvToken tk)
	{
		if(! TryGetEnvironment(tk.Env, out var kind))
			Log.Warn($"Unknown environment '{tk.Env}' referenced by {tk.At}");

		return kind;
	}

	public bool TryGetEnvironment(string name, [MaybeNullWhen(false)] out KnownEnvironment kind)
	{
		switch(Conf.Environments.GetValueOrDefault(name, name))
		{
			case "tabular":
				kind = KnownEnvironment.Tabular;
				return true;
			case "itemize":
				kind = KnownEnvironment.Itemize;
				return true;
			case "div":
				kind = KnownEnvironment.Div;
				return true;

			default:
				kind = KnownEnvironment.Div;
				return false;
		}

	}


	/// <summary>
	///  Extracts a macro definition from a \(re)newcommand
	/// </summary>
	/// <param name="pos"> Reference position for error logging </param>
	/// <param name="code"> Positioned exactly AFTER the initial \*newcommand </param>
	private (string name, Macro macro)? extractMacro(Position pos, ref Code code, bool force)
	{
		var nameArg = code.popArg();

		if(! nameArg.HasValue)
		{
			Log.Warn($"Ignoring incomplete macro definition at {pos}");
			return null;
		}

		if(nameArg.Value.Items().SingleOrNull() is not MacroName name)
		{
			Log.Warn($"Invalid macro name '{Lexer.Untokenize(nameArg.Value)}' at {pos}, ignoring this definition");
			return null;
		}

		var arityArg = code.popOptArg();
		int arity;

		if(! arityArg.HasValue)
			arity = 0;
		else if(!int.TryParse(Lexer.Untokenize(arityArg.Value).Trim(), out arity) || arity < 0)
		{
			Log.Warn($"Ignoring invalid arity spec '{arityArg}' at {pos}");
			arity = 0;
		}

		var defaultVal = code.popOptArg();

		if(defaultVal.HasValue && arity == 0)
		{
			Log.Warn($"Macro defined at {pos} takes no arguments but has a default argument.");
			defaultVal = null;
		}

		// only accept braced definitions
		var body = code.popArg(false);

		if(! body.HasValue)
		{
			Log.Warn($"Ignoring incomplete macro definition at {pos}");
			return null;
		}

		return (name.Macro, new Macro( ArgType.SimpleSignature( arity, defaultVal ), body.Value, force ));
	}

	/// <summary>
	///  Extracts a macro definition from a \*DocumentCommand declaration
	/// </summary>
	/// <param name="pos"> Reference position for error logging </param>
	/// <param name="code"> Positioned exactly AFTER the initial \*DocumentCommand </param>
	private (string name, Macro macro)? extractXParseMacro(Position pos, ref Code code, bool force)
	{
		var nameArg = code.popArg();

		if(! nameArg.HasValue)
		{
			Log.Warn($"Ignoring incomplete macro definition at {pos}");
			return null;
		}
		if(nameArg.Value.SingleOrNull() is not MacroName name)
		{
			Log.Warn($"Invalid macro name '{Lexer.Untokenize(nameArg.Value)}' at {pos}, ignoring this definition");
			return null;
		}

		var spec = code.popArg(false);
		var body = code.popArg(false);

		if(!spec.HasValue || !body.HasValue)
		{
			Log.Warn($"Ignoring incomplete macro at ${pos}");
			return null;
		}

		var args = new List<ArgType>();
		var s = spec.Value;

		while(s.IsNotEmpty)
		{
			var head = s.pop()[0];

			if(head is WhiteSpace)
				continue;
			if(head is Character c) switch(c.Char)
			{
				case 'm':
					args.Add(new MandatoryArg());
				continue;

				case 'o':
					args.Add(new OptionalArg());
				continue;

				case 'O':
				{
					var inner = s.popArg(false);

					if(! inner.HasValue)
					{
						Log.Warn($"Incomplete O argument spec at {head.Pos}");
						return null;
					}

					args.Add(new OptionalArg(inner.Value));
				}
				continue;

				case 's':
					args.Add(new StarArg());
				continue;
			}

			Log.Warn($"Unsupported argument spec: {head} at {head.Pos}");
			return null;
		}

		return (name.Macro, new Macro( args.ToArray(), body.Value, force ));
	}

	/// <summary>
	///  Expands ArgumentRef (#1, #2, ...) tokens
	/// </summary>
	private Code insertArgs(Code inp, Code[] argv, ExpandContext ctx)
	{
		var builder = new ChainBuilder<Token>();
		int last = 0;

		foreach (var i in inp.Items().FindIndices(x => x is ArgumentRef))
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
				ctx.PutTrace();
			}

			last = i + 1;
		}

		builder.Append(inp.Slice(last));

		return builder.Build();
	}

	/// <summary>
	///  Parses the \CASE dynamic compilation macro.
	/// </summary>
	private static Case? parseCase(ref Code code)
	{
		var expanded = new StarArg().parse(ref code)!.Value.IsNotEmpty;
		var subject = code.popArg();

		if(! subject.HasValue)
			return null;

		var cases = new List<(Code, Code)>();

		while(true)
		{
			var w = code.popOptArg();

			if(! w.HasValue)
				break;

			var t = code.popArg();

			if(! t.HasValue)
				return null;

			cases.Add((w.Value, t.Value));
		}

		var fallback = code.popArg();

		if(! fallback.HasValue)
			return null;

		return new( expanded, subject.Value, cases, fallback.Value );
	}

	/// <summary>
	///  Evaluates a \CASE macro
	/// </summary>
	private Code evalCase(Case data, ExpandContext ctx)
	{
		if(data.Expanded)
		{
			var subject = new ChainBuilder<Token>();
			expand(data.Subject, ctx.WithBuilder(subject));

			foreach(var (when, then) in data.Cases)
			{
				var cur = new ChainBuilder<Token>();
				expand(when, ctx.WithBuilder(cur));

				if(subject.Items().like( cur.Items() ))
					return then;
			}
		}
		else
		{
			foreach(var (when, then) in data.Cases)
			{
				if(data.Subject.Items().like( when.Items() ))
					return then;
			}
		}

		return data.Fallback;
	}

	private void expand(Code code, ExpandContext ctx)
	{
		if(! ctx.Gas.decrement())
			throw new TimeoutException("Maximum expansion count exceeded");

		// slice that is fully written back. Always shared a right edge with the current `inp`
		var mark = code;

		while(code.IsNotEmpty)
		{
			var head = code.pop()[0];

			if(head is ArgumentRef)
			{
				Log.Warn($"Unexpanded argument ref {head.At}");
				ctx.PutTrace();

				// the token is discarded later during HTML generation
			}

			// tokens will be written back later
			if(head is not MacroName m)
				continue;

			// perform writeback (without head) (overwrite rest later)
			var w = mark.Length - code.Length - 1;

			if(w > 0)
				ctx.Builder.Append(mark.Slice(0, w));

			if(m.Macro == "includegraphics")
			{
				// optional arg indicates LaTeX-side image size, we just ignore it
				var args = code.parseArguments([ new OptionalArg(), new MandatoryArg() ]);
				mark = code;

				if(args is null)
				{
					Log.Warn($"Discarding incomplete {m.At}");
					continue;
				}

				var file = Lexer.Untokenize(args[1]);

				if(Conf.Images is null || !(Conf.Images.TryGetValue(file, out var replace) || Conf.Images.TryGetValue(Path.GetFileName(file), out replace)))
					Log.Warn($"Discarding use of unknown image '{file}' at {m.Pos}");
				else
					ctx.Builder.Append( new HtmlChunk(replace, new("builtin/images", 0, 0)) );
			}
			else if(m.Macro == "CASE")
			{ // signature like `s m (M[] m)* m`
				var data = parseCase(ref code);
				mark = code;

				if(! data.HasValue)
				{
					Log.Warn($"Discarding incomplete {m.At}");
					continue;
				}

				ctx.Trace?.Push(m);
				var ev = evalCase(data.Value, ctx);

				expand(ev, ctx);
				ctx.Trace?.Pop();
			}
			else if(m.Macro == "IFHTML")
			{
				var then = code.popArg();
				var els = code.popOptArg();
				mark = code;

				if(! then.HasValue)
				{
					Log.Warn($"Discarding incomplete {m.At}");
					continue;
				}

				if(ctx.HtmlMode)
					expand(then.Value, ctx.WithExpand(m));
				else if(els.HasValue)
					expand(els.Value, ctx.WithExpand(m));
			}
			else if(! macros.TryGetValue(m.Macro, out var def))
			{
				Log.Warn($"Unknown macro {m.At}, discarding it");
				mark = code;
			}
			else
			{
				var args = code.parseArguments(def.Args);
				mark = code;

				if(args is null)
				{
					Log.Warn($"Discarding incomplete use of {m.At}");
					continue;
				}

				if(Debug)
				{
					Console.Error.WriteLine($"[TRACE] Expanding {m.At} to '{Lexer.Untokenize(def.Replacement)}'");
					int j = 0;

					foreach(var x in args)
						Console.Error.WriteLine($"[TRACE]     Argument #{++j}: {Lexer.Untokenize(x)}");
				}

				var sub = ctx.WithExpand(m);
				expand(insertArgs(def.Replacement, args, sub), sub);
			}
		}

		ctx.Builder.Append(mark);
	}

	/// <summary>
	///  Loads macros from the given text into the active context
	/// </summary>
	public void LearnMacrosFrom(IEnumerable<string> lines, string filename)
		=> LearnMacrosFrom( new Lexer(Log).Tokenize(lines, filename ?? "<unknown>") );

	/// <summary>
	///  Actions a declaration command may take
	/// </summary>
	private enum DeclarationAction
	{
		OVERWRITE,
		NOOP,
		ERROR
	}

	/// <summary>
	///  Learns every macro definition in a file.
	///  Does not know about \if or quoting environments.
	/// </summary>
	public void LearnMacrosFrom(Code code)
	{
		while(code.IsNotEmpty)
		{
			var head = code.pop()[0];

			if(head is not MacroName macro)
				continue;

			bool force = false;
			DeclarationAction ifPresent = DeclarationAction.ERROR;
			DeclarationAction ifAbsent = DeclarationAction.OVERWRITE;
			(string name, Macro macro) definition;

			switch(macro.Macro)
			{
				case "renewcommand":
					ifPresent = DeclarationAction.OVERWRITE;
					ifAbsent = DeclarationAction.ERROR;
				goto newcommand;
				case "providenewcommand":
					ifPresent = DeclarationAction.NOOP;
				goto newcommand;
				case "forcenewcommand":
					force = true;
				goto newcommand;
				case "newcommand":
				newcommand:
				{
					var d = extractMacro(head.Pos, ref code, force);

					if(! d.HasValue)
						continue;

					definition = d.Value;
				}
				break;

				case "RenewDocumentCommand":
					ifPresent = DeclarationAction.OVERWRITE;
					ifAbsent = DeclarationAction.ERROR;
				goto NewDocumentCommand;
				case "ProvideDocumentCommand":
					ifPresent = DeclarationAction.NOOP;
				goto NewDocumentCommand;
				case "DeclareDocumentCommand":
					ifPresent = DeclarationAction.OVERWRITE;
				goto NewDocumentCommand;
				case "ForceDocumentCommand":
					force = true;
				goto NewDocumentCommand;
				case "NewDocumentCommand":
				NewDocumentCommand:
				{
					var d = extractXParseMacro(head.Pos, ref code, force);

					if(! d.HasValue)
						continue;

					definition = d.Value;
				}
				break;

				default:
					continue;
			}

			if(macros.TryGetValue(definition.name, out var previous))
			{
				if(previous.Force)
					continue;

				switch(ifPresent)
				{
					case DeclarationAction.NOOP:
						continue;
					case DeclarationAction.OVERWRITE:
						break;
					case DeclarationAction.ERROR:
						Log.Warn($"Overwriting definition for \\{definition.name} without using a redefine at {head.Pos}");
					break;
				}
			}
			else switch(ifAbsent)
			{
				case DeclarationAction.NOOP:
					continue;
				case DeclarationAction.OVERWRITE:
					break;
				case DeclarationAction.ERROR:
					Log.Warn($"Redefining undeclared macro \\{definition.name} at {head.Pos}");
				break;
			}

			macros[definition.name] = definition.macro;
		}
	}

	/// <summary>
	///  Extracts the segments that define spells and invokes the game's extractor for them
	/// </summary>
	public IEnumerable<TSpell> ExtractSpells<TSpell>(IGame<TSpell> game, Code code, string source)
	{
		foreach (var (c,n) in code.Items().FindIndices(spellAnchor, (x,y) => x.IsSame(y), false).Pairs())
		{
			var cc = c + spellAnchor.Length;
			var seg = n.HasValue ? code.Slice(cc, n.Value - cc) : code.Slice(cc);

			if(! game.Conf.Books.TryGetValue(source, out var book))
			{
				Log.Warn($"In {seg.PosRange()}: Got unknown source '{source.Show()}'");
				continue;
			}

			TSpell spell;

			try
			{
				spell = game.ExtractLatexSpell(this, book, seg);
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
		Stack<KnownEnvironment> envs = new();
		int row = 0;
		// if true, swallow the next macro argument
		bool skipNextArg = false;
		// if skipNextArg is true, tracks how many open braces were processed
		int skipDepth = 0;

		foreach(var tk in chain)
		{
			if(skipNextArg)
			{
				if(tk is OpenBrace)
					++skipDepth;
				else if(tk is CloseBrace)
				{
					if(skipDepth == 0)
					{
						skipNextArg = false;
						goto noSkip;
					}
					else
						--skipDepth;
				}
				else if(tk is WhiteSpace)
					continue;

				if(skipDepth == 0)
					skipNextArg = false;

				continue;
			}

			noSkip:

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
			else if(tk is OpenBrace or CloseBrace or ToggleMode)
				continue;
			else if(tk is WhiteSpace)
				doc.Append(' ');
			else if(tk is ArgumentRef)
			{ }
			else if(tk is HtmlChunk hc)
				doc.Append(hc.Data);
			else if(tk is BackBack b)
			{
				doc.Append((envs.TryPeek(out var x) ? x : KnownEnvironment.Div) switch {
					KnownEnvironment.Itemize => "</li><li>" ,
					KnownEnvironment.Tabular => "</td></tr><tr><td>" ,
					_ => "<br/>"
				});
			}
			else if(tk is BeginEnv be)
			{
				var x = envKind(be);
				envs.Push(x);

				if(x == KnownEnvironment.Tabular)
				{
					skipNextArg = true;
					skipDepth = 0;
				}

				doc.Append(x switch {
					KnownEnvironment.Itemize => $"<ul class=\"{WebUtility.HtmlEncode(be.Env)}\"> <li>",
					KnownEnvironment.Tabular => $"<table class=\"{WebUtility.HtmlEncode(be.Env)}\"> <tr> <td>",
					KnownEnvironment.Div => $"<div class=\"{WebUtility.HtmlEncode(be.Env)}\">" ,
					_ => throw new UnreachableException()
				});
			}
			else if(tk is EndEnv)
			{
				doc.Append(envs.Pop() switch {
					KnownEnvironment.Itemize => "</li> </ul>",
					KnownEnvironment.Tabular => "</td> </tr> </table>",
					KnownEnvironment.Div     => "</div>" ,
					_ => throw new UnreachableException()
				});
			}
			else if(tk is AlignTab)
			{
				if(envs.TryPeek(out var x) && x == KnownEnvironment.Tabular)
					doc.Append("</td> <td>");
				else
					Log.Warn($"Dropping superfluous alignment tab at {tk.Pos}");
			}
			else
				throw new UnreachableException($"Incomplete pattern match: {tk}");
		}

		return doc.ToString().Trim();
	}

	public string ToHTML(Code code)
	{
		var builder = new ChainBuilder<Token>();

		expand( code, new( builder, new(Conf.MaximumExpansions), [ new MacroName("compile", new("ToHTML()", 0, 0)) ], true ) );

		return ToHTML(builder.Items());
	}

	/// <summary>
	///  Expands and displays a segment
	///
	///  (!) DON'T use for DB-stored values, as those should be HTML-safe
	/// </summary>
	public string ToString(Code code)
	{
		var builder = new ChainBuilder<Token>();

		expand( code, new( builder, new(Conf.MaximumExpansions), [ new MacroName("compile", new("ToString()", 0, 0)) ], false ) );

		var str = new StringBuilder();
		bool outputState = true;

		foreach(var tk in builder.Items())
		{
			if(tk is ToggleMode h)
				outputState = h.Mode == OutputMode.NORMAL;
			else if(outputState)
				str.Append(tk.Display());
		}

		str.Trim();

		return str.ToString();
	}

	/// <summary>
	///  Same as `ToString()` but escapes any HTML characters
	/// </summary>
	public string ToSafeString(Code seg)
		=> WebUtility.HtmlEncode(ToString(seg));

	public string ToString(ArraySegment<Token> seg)
		=> ToString(new Code(seg));

}