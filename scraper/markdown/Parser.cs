
namespace Grimoire.Markdown;

/** The markdown parser. Supports:
	- bold/italics with `*`
	- unordered (`*`) lists
	- list nesting with indentation
	- headlines with `#`
	- horizontal rules with `---`
	- links
 */
static class Parser
{
	internal abstract record Token();
	internal record ToggleItalic() : Token(); // *
	internal record ToggleBold() : Token(); // **
	internal record BeginLink() : Token(); // [
	internal record EndLink(Uri Url) : Token(); // ](…)
	internal record Text(string Content) : Token();

	internal abstract record Line();
	internal record EmptyLine() : Line();
	internal record Rule() : Line();
	internal abstract record ContentLine(List<Token> Content) : Line();
	internal record ListItem(int Depth, bool ordered, List<Token> Content) : ContentLine(Content);
	internal record Headline(int Level, List<Token> Content) : ContentLine(Content);
	/// <summary>
	///  A line without special starting operator
	/// </summary>
	/// <param name="LineBreak"> Whether the line ended with two spaces </param>
	/// <returns></returns>
	internal record PlainLine(List<Token> Content, bool LineBreak) : ContentLine(Content);

	private static readonly char[] special = ['*', '_', '[', ']', '!', '\\'];

	private static List<Token> parseContent(ReadOnlySpan<char> text)
	{
		var output = new List<Token>();

		while(! text.IsEmpty)
		{
			int o = text.IndexOfAny(special);

			if(o < 0)
			{
				output.Add(new Text(text.ToString()));
				break;
			}

			if(o > 0)
			{
				output.Add(new Text(text.Slice(0, o).ToString()));
				text = text.Slice(o);
			}

			int cut;
			Token put;

			switch(text[0])
			{
				case '*':
				case '_':
					if(text.Length > 1 && text[1] == text[0])
					{
						cut = 2;
						put = new ToggleBold();
					}
					else
					{
						cut = 1;
						put = new ToggleItalic();
					}
				break;

				case '[':
					cut = 1;
					put = new BeginLink();
				break;

				case ']':
					if(text.Length < 3 || text[1] != '(')
						throw new FormatException($"Orphaned ']' at: {text.Slice(0,50).ToString()}…");

					int r = text.IndexOf(')');

					if(r < 0)
						throw new FormatException("Unterminated link");

					cut = r + 1;
					put = new EndLink(new Uri(text.Slice(0,r).Slice(2).ToString()));
				break;

				case '\\':
					if(text.Length < 2)
						throw new FormatException("Orphaned '\\'");

					cut = 2;
					put = new Text(text[1].ToString());
				break;

				default:
					throw new InvalidOperationException();
			}

			output.Add(put);
			text = text.Slice(cut);
		}

		return output;
	}

	private static int indentDepth(ReadOnlySpan<char> line)
	{
		int t = 0;
		int s = 0;

		for (int i = 0; i < line.Length; i++)
		{
			if(line[i] == '\t')
				++t;
			else if(char.IsWhiteSpace(line[i]))
				++s;
			else
				break;
		}

		return 4*t + s;
	}

	private static bool hasOrderedIndex(ref ReadOnlySpan<char> span)
	{
		for (int i = 0; i < span.Length; i++)
		{
			char c = span[i];

			if('0' <= c && c <= '9')
			{
				if(c == '0' && i == 0)
					return false;
			}
			else if(c == '.' && i > 0)
			{
				span = span.Slice(i + 1);
				return true;
			}
			else
				return false;
		}

		return false;
	}

	private static bool all<T>(this ReadOnlySpan<T> span, Func<T, bool> pred)
	{
		for(int i = 0; i < span.Length; i++)
		{
			if(! pred(span[i]))
				return false;
		}

		return true;
	}



	/// <summary>
	///  Lexes a single line
	/// </summary>
	/// <param name="span">
	/// 	A span over the line's entire content. WITHOUT trailing \n BUT INCLUDING leading/trailing space
	/// </param>
	private static Line parseLine(ReadOnlySpan<char> _span)
	{
		var span = _span.Trim();

		if(span.IsEmpty)
			return new EmptyLine();
		else if(span.Length >= 3 && span.all(x => x == '-'))
			return new Rule();
		else if(span.StartsWith("* ") || span.StartsWith("- "))
			return new ListItem(indentDepth(_span), false, parseContent(span.Slice(2)).ToList());
		else if(hasOrderedIndex(ref span))
			return new ListItem(indentDepth(_span), true, parseContent(span).ToList());
		else if(span[0] == '#')
		{
			int nBak = span.Length;
			span = span.TrimStart('#');
			return new Headline(nBak - span.Length, parseContent(span).ToList());
		}
		else
			return new PlainLine(parseContent(span).ToList(), _span.EndsWith("  "));
	}

	public static List<Line> ParseLines(ReadOnlySpan<char> text)
	{
		var output = new List<Line>();

		while(text.Length > 0)
		{
			int n = text.IndexOf('\n');

			if(n < 0)
			{
				output.Add(parseLine(text));
				break;
			}
			else
			{
				output.Add(parseLine(text.Slice(0, n)));
				text = text.Slice(n + 1);
			}
		}

		return output;
	}
}