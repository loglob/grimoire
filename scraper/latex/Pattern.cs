
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Grimoire.Latex;

/// <summary>
///  A pattern to be matched against lexed LaTeX source code
/// </summary>
public abstract class Pattern
{
	private Pattern()
	{}

	abstract public int MaxWidth { get; }

	/// <summary>
	///  Determines whether this pattern matches the prefix of a collection
	/// </summary>
	abstract public bool MatchesWindow(IReadOnlyCollection<Token> tokens, [MaybeNullWhen(false)] out (int offset, int length) ixs);

	public static Pattern Of(string prefix, string pattern, string postfix)
		=> new Leaf(prefix + pattern + postfix);

	public static Pattern Of(string pattern)
		=> new Leaf(pattern);

	public static Pattern Of(params Pattern[] options)
		=> new Select(options.SelectMany(p => p is Select s ? s.Options : [(Leaf)p]).ToImmutableList());

	public static Pattern Of(params string[] options)
		=> new Select(options.Select(o => new Leaf(o)).ToImmutableArray());

	private class Select(IReadOnlyCollection<Leaf> options) : Pattern()
	{
		public Select(params Leaf[] options) : this(options.AsReadOnly())
		{ }

		public readonly IReadOnlyCollection<Leaf> Options = options;

		public override int MaxWidth { get; } = options.Max(x => x.MaxWidth);

		public override bool MatchesWindow(IReadOnlyCollection<Token> tokens, [MaybeNullWhen(false)] out (int,int) ixs)
		{
			foreach(var o in Options)
			{
				if(o.MatchesWindow(tokens, out ixs))
					return true;
			}

			ixs = default;
			return false;
		}
	}

	private class Leaf(string pre, string pattern, string post) : Pattern()
	{
		public Leaf(string pattern) : this("", pattern, "")
		{}

		public readonly int PrefixLength = pre.Length;
		public readonly int SuffixLength = post.Length;
		public readonly string FullPattern = pre + pattern + post;

		public override int MaxWidth
			=> FullPattern.Length;

		private static bool matchesChar(Token token, char pattern)
			=> pattern switch
			{
				'#' => token is not Character c || !char.IsLetterOrDigit(c.Char),
				'.' => true,
				' ' => token is WhiteSpace,
				_ => token is Character c && char.ToLower(c.Char) == char.ToLower(pattern)
			};

		public override bool MatchesWindow(IReadOnlyCollection<Token> tokens, [MaybeNullWhen(false)] out (int,int) ixs)
		{
			if(tokens.Count >= MaxWidth && tokens.Zip(FullPattern).All(zip => matchesChar(zip.First, zip.Second)))
			{
				ixs = (PrefixLength, FullPattern.Length - PrefixLength - SuffixLength);
				return true;
			}
			else
			{
				ixs = default;
				return false;
			}
		}
	}
}
