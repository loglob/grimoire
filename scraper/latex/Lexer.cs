using Grimoire.Util;
using System.Diagnostics;
using System.Text;

namespace Grimoire.Latex;

public readonly record struct Lexer(Log Log)
{
	private Token? lex(string input, int row, string filename, ref int off, bool lastWS)
	{
		redo:
		if(off >= input.Length)
			return null;

		var pos = new Position(filename, row, off + 1);

		switch(input[off])
		{
			case '\\':
			{
				var len = input.Skip(off + 1).TakeWhile(char.IsLetterOrDigit).Count();

				if(len == 0)
					len = 1;

				if(off + 1 >= input.Length)
				{
					++off;
					Log.Warn($"Trailing, unmatched, unescaped '\\' at {pos}");
					return new Character('\\', pos);
				}
				else if(len == 1 && input[off + 1] == '<')
				{
					// HTML chunk
					int matching = input.IndexOf("\\>", off + 2);

					if(matching == -1)
					{
						// swallow the illegal symbol and continue
						Log.Warn("Unmatched HTML chunk start. Multiline chunks not allowed.");
						off += 2;
						goto redo;
					}

					var tk = new HtmlChunk(input.Substring(off + 2, matching - off - 2), pos);
					off = matching + 2;
					return tk;
				}
				else if(len == 1 && input[off + 1] == '\\')
				{
					off += 2;
					return new BackBack(pos);
				}
				else
				{
					var tk = new MacroName(input.Substring(off + 1, len == 0 ? 1 : len), pos);
					off += len + 1;
					return tk;
				}
			}

			case '{':
			{
				++off;
				var n = new OpenBrace(pos);
				return n;
			}

			case '}':
			{
				++off;
				return new CloseBrace(pos);
			}

			case '%':
				return null;

			case '#':
			{
				var len = input.Skip(off + 1).TakeWhile(char.IsDigit).Count();
				var o0 = off;
				off += 1 + len;

				if(len == 0)
				{
					Log.Warn("Orphaned, unescaped '#'");
					return new Character('#', pos);
				}
				else
					return new ArgumentRef(int.Parse(input.Substring(o0 + 1, len)), pos);
			}

			case '&':
			{
				++off;
				return new AlignTab(pos);
			}

			case '$':
				++off;
			goto redo;

			default:
			{
				var c = input[off++];

				if(char.IsWhiteSpace(c))
				{
					if(lastWS)
						goto redo;
					else
						return new WhiteSpace(pos);
				}
				else
					return new Character(c, pos);
			}
		}
	}

	/// <summary>
	///  Post-processes a token list to recognize environments
	/// </summary>
	private Chain<Token> postLex(Chain<Token> input)
	{
		var stack = new Stack<BeginEnv>();
		var builder = new ChainBuilder<Token>();
		// marks the last position that was written back. `input` is always a suffix of `mark`
		var mark = input;

		while(input.IsNotEmpty)
		{
			var head = input.pop()[0];

			if(head is not MacroName m || (m.Macro != "begin" && m.Macro != "end"))
				continue;

			// perform writeback (without head)
			var w = mark.Length - input.Length - 1;

			if(w > 0)
				builder.Append(mark.Slice(0, w));

			var arg = input.popArg(false);
			mark = input;

			if(arg is null)
			{
				Log.Warn($"Expected an environment name after {m.At}, discarding it");
				continue;
			}

			var name = Untokenize(arg.Value);

			if(arg.Value.Items().Any(t => t is not Character) || arg.Value.IsEmpty)
				Log.Warn($"Suspicious environment name '{name}' at {m.Pos}");

			if(m.Macro == "begin")
			{
				var b = new BeginEnv(name, m.Pos);
				builder.Append(b);
				stack.Push(b);
			}
			else // m.Macro == "end"
			{
				var e = new EndEnv(name, m.Pos);

				if(stack.TryPeek(out var b) && b.Env == name)
				{
					stack.Pop();
					builder.Append(e);
				}
				else
					Log.Warn($"{e.At} has no matching \\begin, discarding it");
			}
		}

		builder.Append(mark);

		foreach(var b in stack)
		{
			builder.Append( new EndEnv(b.Env, new("builtin/postLex", 0, 0)) );
			Log.Warn($"{b.At} has no matching \\end, inserting one");
		}

		return builder.Build();
	}

	/// <summary>
	///  Lexes a latex program. Ensures that OpenBrace and CloseBrace perfectly balance another.
	/// </summary>
	public Chain<Token> Tokenize(IEnumerable<string> lines, string filename)
	{
		var tks = new List<Token>();
		int row = 1;
		var stack = new Stack<int>();

		foreach (var l in lines)
		{
			if(string.IsNullOrWhiteSpace(l))
				tks.Add(new Character('\n', new Position(filename, row, 1)));

			int off = 0;
			bool ws = false;

			for(;;)
			{
				var tk = lex(l, row, filename, ref off, ws);

				if(tk is null)
					break;

				// ensure braces are matched
				if(tk is OpenBrace o)
					stack.Push(tks.Count);
				else if(tk is CloseBrace && !stack.TryPop(out var _))
				{
					Log.Warn($"Orphaned '}}' at {tk.Pos}");
					tk = new Character('}', tk.Pos);
				}

				tks.Add(tk);
				ws = tk is WhiteSpace;
			}

			++row;
		}

		foreach(var l in stack.Reverse())
		{
			var tk = tks[l];
			Log.Warn($"Unmatched and unescaped '{{' at {tk.Pos}");
			tks[l] = new Character('{', tk.Pos);
		}

		return postLex( new( tks.ToArray() ) );
	}

	/// <summary>
	///  Variant of Tokenize() that doesn't do any scope checking.
	///  In particular, no BeginEnv or EndEnv are emitted,
	///     and all braces are given via Open- or CloseBrace
	/// </summary>
	public Token[] TokenizeUnchecked(IEnumerable<string> lines, string filename)
	{
		var tks = new List<Token>();
		int row = 1;

		foreach (var l in lines)
		{
			if(string.IsNullOrWhiteSpace(l))
				tks.Add(new Character('\n', new Position(filename, row, 1)));

			int off = 0;
			bool ws = false;

			for(;;)
			{
				var tk = lex(l, row, filename, ref off, ws);

				if(tk is null)
					break;

				tks.Add(tk);
				ws = tk is WhiteSpace;
			}

			++row;
		}

		return tks.ToArray();
	}

	/// <summary>
	///  Reverts tokens to (an approximation of) the producing source code
	/// </summary>
	/// <param name="tk"></param>
	/// <returns></returns>
	public static string Untokenize(IEnumerable<Token> tk)
	{
		var builder = new StringBuilder();
		bool putSep = false;

		foreach (var t in tk)
		{
			if(putSep && t is Character c && char.IsLetter(c.Char))
				builder.Append(' ');

			builder.Append(t);
			putSep = t is MacroName;
		}

		return builder.ToString();
	}

	public static string Untokenize(Chain<Token> tk)
		=> Untokenize(tk.Items());
}