using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Grimoire.Util;

/// <summary>
/// A simple glob expression with exactly one variable
/// </summary>
public abstract record Glob
{
	private Glob()
	{}

	public record Wildcard(string prefix, string suffix) : Glob()
	{
		public override int Arity => 1;

		public override string Insert(string? variable)
		{
			if(variable is null)
				throw new ArgumentException("Glob arity mismatch, expected 1 but got 0");

			return prefix + variable + suffix;
		}

		public override bool Test(string str, out string? variable)
		{
			if(str.Length >= prefix.Length + suffix.Length && str.StartsWith(prefix, true, null) && str.EndsWith(suffix, true, null))
			{
				variable = str[prefix.Length .. ^suffix.Length];
				return true;
			}
			else
			{
				variable = default;
				return false;
			}
		}
	}

	public record Constant(string value) : Glob()
	{
		public override int Arity => 0;

		public override string Insert(string? variable)
		{
			if(variable is not null)
				throw new ArgumentException("Glob arity mismatch, expected 0 but got 1");

			return value;
		}

		public override bool Test(string str, out string? variable)
		{
			variable = null;
			return string.Equals(str, value, StringComparison.CurrentCultureIgnoreCase);
		}
	}

	[Pure]
	public static bool TryParse(string pattern, [MaybeNullWhen(false)] out Glob glob)
	{
		var spl = pattern.Split('*', 3);

		switch(spl.Length)
		{
			case 1:
				glob = new Constant(pattern);
				return true;
			case 2:
				glob = new Wildcard(spl[0], spl[1]);
				return true;
			default:
				glob = default;
				return false;
		}
	}

	/// <summary>
	///  Number of * wildcards appearing in the Glob
	/// </summary>
	public abstract int Arity { get; }

	/// <summary>
	///  Tests if this pattern applies
	/// </summary>
	[Pure]
	public abstract bool Test(string str, out string? variable);

	/// <summary>
	///  Reconstructs the input to a successful `Test`
	/// </summary>
	/// <param name="variable"> The value returned by `Test` via `variable` </param>
	[Pure]
	public abstract string Insert(string? variable);
}