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
	private void postLex(List<Token> tokens)
	{
		var stack = new Stack<int>();

		for (int i = 0; i < tokens.Count; ++i)
		{
			if(tokens[i] is MacroName m && m.Macro is "begin" or "end")
			{
				var (args, end) = tokens.LocateArgs(i + 1, 0, 1);

				if(args.Length != 1)
					throw new UnreachableException("LocateArgs() returned wrong arity");

				var (index, len) = args[0];

				if(index < 0)
				{
					Log.Warn($"Expected an environment name after {m}, discarding it");
					// discard the macro
					tokens.RemoveAt(i);
					--i; // undo ++i
					continue;
				}

				var slice = tokens.Skip(index).Take(len);
				var str = Untokenize(slice).Trim();

				if(slice.Any(t => t is not WhiteSpace && t is not Character) || !slice.Any())
					Log.Warn($"Suspicious environment name '{str}' at {m.Pos}");

				Token envTk = m.Macro switch {
					"begin" => new BeginEnv(str, m.Pos) ,
					"end" => new EndEnv(str, m.Pos) ,
					_ => throw new UnreachableException()
				};

				tokens.RemoveRange(i, end - i);
				tokens.Insert(i, envTk);
			}

			if(tokens[i] is BeginEnv b)
				stack.Push(i);
			else if(tokens[i] is EndEnv e)
			{
				if(stack.TryPeek(out var t) && ((BeginEnv)tokens[t]).Env == e.Env)
					stack.Pop();
				else
				{
					Log.Warn($"{e} has no matching \\begin, discarding it");
					tokens.RemoveAt(i);
					--i; // undoes following ++i
				}
			}
		}

		foreach (var i in stack)
		{
			var o = (BeginEnv)tokens[i];
			Log.Warn($"{o} has no matching \\end, discarding it");
			tokens.RemoveAt(i);
		}
	}

	/// <summary>
	///  Lexes a latex program. Ensures that OpenBrace and CloseBrace perfectly balance another.
	/// </summary>
	public Token[] Tokenize(IEnumerable<string> lines, string filename)
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

		postLex(tks);

		return tks.ToArray();
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