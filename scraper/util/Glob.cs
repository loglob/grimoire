using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Grimoire.Util;

/// <summary>
/// A simple glob expression with exactly one variable
/// </summary>
public readonly record struct Glob(string prefix, string suffix)
{
	[Pure]
	public static bool TryParse(string pattern, [MaybeNullWhen(false)] out Glob glob)
	{
		var spl = pattern.Split('*', 3);

		if(spl.Length == 2)
		{
			glob = new(spl[0], spl[1]);
			return true;
		}
		else
		{
			glob = default;
			return false;
		}
	}

	[Pure]
	public bool Test(string str, [MaybeNullWhen(false)]out string variable)
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

	[Pure]
	public string Insert(string str)
		=> prefix + str + suffix;
}