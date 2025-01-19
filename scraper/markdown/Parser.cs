using Grimoire.Util;

namespace Grimoire.Markdown;

/** The markdown parser. Supports:
	- bold/italics with `*`
	- unordered (`*`) lists
	- list nesting with indentation
	- headlines with `#`
	- horizontal rules with `---`
	- links
	- arbitrary HTML tags
 */
static class Parser
{
	internal abstract record Token();
	internal record ToggleItalic() : Token(); // *
	internal record ToggleBold() : Token(); // **
	internal record BeginLink() : Token(); // [
	internal record EndLink(Uri Url) : Token(); // ](…)
	internal record Text(string Content) : Token();
	/// <param name="selfClosing"> If true, this tag closes itself. </param>
	/// <param name="tag"> The specified tag name, in original case </param>
	/// <param name="attributes">
	/// 	The specified parameters
	/// 	(!) maps onto RAW strings WITH escape sequences WITHOUT quotes
	/// </param>
	internal record OpenHtml(string tag, Dictionary<string,string> attributes, bool selfClosing) : Token()
	{
		public override string ToString()
			=> $"OpenHtml {{ {nameof(tag)} = {tag}, {nameof(attributes)} = {attributes.Show()} , {nameof(selfClosing)} = {selfClosing}}}";
	}
	internal record CloseHtml(string tag) : Token();

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

	private static readonly char[] special = ['*', '_', '[', ']', '!', '\\', '<'];

	private static int countWhile<T>(this ReadOnlySpan<T> data, Func<T, bool> pred)
	{
		int n = 0;

		while(n < data.Length && pred(data[n]))
			++n;

		return n;
	}

	private static bool parseStringLiteral(ReadOnlySpan<char> content, out int n)
	{
		n = 0;

		while(true)
		{
			if(n >= content.Length)
				return false;
			switch(content[n++])
			{
				case '\n':
					return false;

				case '"':
					return true;

				case '\\':
					++n;
				break;
			}
		}
	}

	/// <summary>
	///  Accepts an identifier possibly preceded by spaces
	/// </summary>
	/// <param name="text"></param>
	/// <param name="n"></param>
	/// <returns></returns>
	private static string? parseName(ReadOnlySpan<char> text, ref int n)
	{
		if(! char.IsLetter(nextNonSpace(text, ref n)))
			return null;

		var cur = text.Slice(n);
		var l = cur.countWhile(char.IsLetterOrDigit);
		n += l;

		return cur.Slice(0, l).ToString();
	}

	private static char nextNonSpace(ReadOnlySpan<char> text, ref int n)
	{
		n += text.Slice(n).countWhile(char.IsWhiteSpace);

		return n < text.Length ? text[n] : (char)0;
	}

	private static OpenHtml? parseOpeningHtml(ReadOnlySpan<char> text, out int n)
	{
		n = 1;

		var tagName = parseName(text, ref n);

		if(tagName is null)
			return null;

		var attr = new Dictionary<string, string>();

		while(true)
		{
			switch(nextNonSpace(text, ref n))
			{
				case '>':
				{
					++n;
					return new OpenHtml(tagName.ToString(), attr, false);
				}
				case '/':
				{
					++n;

					if(n >= text.Length || text[n] != '>')
						return null;

					++n;

					return new OpenHtml(tagName.ToString(), attr, true);
				}
				case (char)0:
					return null;
			}

			var attrName = parseName(text, ref n);

			if(attrName is null || nextNonSpace(text, ref n) != '=')
				return null;

			++n;

			if(nextNonSpace(text, ref n) != '"')
				return null;

			++n;
			var vStart = text.Slice(n);

			if(! parseStringLiteral(vStart, out var vl))
				return null;

			n += vl;
			attr[attrName] = vStart.Slice(0, vl - 1).ToString();
		}
	}

	private static CloseHtml? parseClosingHtml(ReadOnlySpan<char> text, out int n)
	{
		n = 2;
		var tagName = parseName(text, ref n);

		if(tagName is null)
			return null;

		if(nextNonSpace(text, ref n) != '>')
			return null;

		++n;
		return new(tagName);
	}

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

				case '<':
					put = ((text.Length > 1 && text[1] == '/')
							? (Token?)parseClosingHtml(text, out cut)
							: parseOpeningHtml(text, out cut)
						) ?? throw new FormatException($"Invalid HTML tag: {text}");
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