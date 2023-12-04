using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using Latex;
using Util;

public record class Goedendag(Config.Game Conf) : IGame<Goedendag.Spell>
{
	[JsonConverter(typeof(StringEnumConverter))]
	public enum Arcanum
	{
		General,
		Nature,
		Elementalism,
		Charms,
		Conjuration,
		Divine
	}

	[JsonConverter(typeof(StringEnumConverter))]
	public enum PowerLevel
	{
		Generalist,
		Petty,
		Lesser,
		Greater
	}

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public readonly record struct Spell(
        string name,
		Arcanum arcanum,
		PowerLevel powerLevel,
		bool combat,
		bool reaction,
		string distance,
		string duration,
		string castingTime,
		string components,
		string brief,
		string effect,
		string critSuccess,
		string critFail,
		string? extra
    ) : ISpell
    {
        string ISpell.Source => "GD";
    }


	private static (Chain<Token> left, string[][] table, Chain<Token> rest) takeTable(Compiler comp, Chain<Token> code, string name = "spells")
	{
		int off = code.Items().FindOnSameLevel(x => x is BeginEnv b && b.Env == name);

		if(off < 0)
			throw new FormatException($"Expected a '\\begin{{{name}}}'");

		var left = code.Slice(0, off);

		var spec = code.Slice(off + 1).Args(1, 2);
		code = spec.rest;

		if(code.SplitOn(x => x is EndEnv, true) is not var (inner, end, rest))
			throw new FormatException($"No matching \\end{{{name}}}");
		if(end is not EndEnv e || e.Env != name)
			throw new FormatException($"Malformed environments don't match");

		var table = inner.SplitBy(x => x is BackBack, true)
			.Select(x => x.SplitBy(y => y is AlignTab, true)
				.Select(comp.ToString)
				.ToArray())
			.ToList();

		if(table[^1] is [] || (table[^1] is [ var x ] && string.IsNullOrWhiteSpace(x)))
			table.RemoveAt(table.Count - 1);

		return (left, table.ToArray(), rest);
	}

    public Spell ExtractLatexSpell(Compiler comp, string source, Chain<Token> body)
    {
		if(body.SplitOn(x => x is MacroName mn && mn.Macro == "label") is not var (_name, _, _label))
			throw new FormatException("Bad spell format");

		var label = _label.Args(0, 1);
		var labelUri = label.args[0]!;
		body = label.rest;

		bool combat = false;
		bool reaction = false;
		string name;

		{
			var nameWords = comp.ToString(_name).Split();
			int wLen = nameWords.Length;
			recheck_combat:
			if(!combat && nameWords[wLen - 1] == "\\C")
			{
				combat = true;
				--wLen;
			}
			if(!reaction && nameWords[wLen - 1] == "\\R")
			{
				reaction = true;
				--wLen;
				goto recheck_combat;
			}

			name = string.Join(' ', nameWords.Take(wLen));
		}

		Arcanum arcanum;

		{
			var link = comp.ToString(labelUri.Value);

			arcanum = link.Split(':') switch {
				["arc", _] => throw new NotASpellException(),
				[_, "general",  _] => Arcanum.General,
				[_, "nature",  _] => Arcanum.Nature,
				[_, "ele",  _] => Arcanum.Elementalism,
				[_, "charm",  _] => Arcanum.Charms,
				[_, "divine",  _] => Arcanum.Divine,
				[_, "conj", _] => Arcanum.Conjuration,
				_ => throw new FormatException($"Unexpected hyperref format '{link}'")
			};
		}


		var (_brief, props, afterProps) = takeTable(comp, body);
		string brief = comp.ToHTML(_brief);
		body = afterProps;

		// there is a trailing '\\'
		if(props.Length != 2 || props.Min(x => x.Length) != 4)
			throw new FormatException($"Properties must be a 2x4 table, got {props.Length}x{props.Max(x => x.Length)}");

		var (distance, duration) = props[0] switch {
			[ "Distance:", var x, "Duration:", var y ] => (x, y),
			_ => throw new FormatException("Bad format of first properties row: " + props[0].Show())
		};

		var (castingTime, powerLevel) = props[1] switch {
			[ "Casting Time:", var x, "Power Level:", var y ] => (x, Enum.Parse<PowerLevel>(y, true)),
			_ => throw new FormatException("Bad format of second properties row: " + props[0].Show())
		};

		var (discard, details, afterDetails) = takeTable(comp, afterProps);

		if(discard.Items().Any(x => x is not WhiteSpace))
			throw new FormatException($"Unexpected tokens between spell tables: '{Lexer.Untokenize(discard)}'");

		var dtDim = (x: details.Length, y: details.Min(x => x.Length));

		if(dtDim != (4,2))
			throw new FormatException($"Details table must be 4x2, got {dtDim.x}x{dtDim.y}");

		var components = details[0] switch {
			["Components:", var x] => x,
			_ => throw new FormatException("Bad format of first details row: " + details[0].Show())
		};
		var effect = details[1] switch {
			["Effect:", var x] => x,
			_ => throw new FormatException("Bad format of second details row: " + details[1].Show())
		};
		var crit = details[2] switch {
			["Passing \\geq 10:", var x] => x,
			_ => throw new FormatException("Bad format of third details row: " + details[2].Show())
		};
		var fail = details[3] switch {
			["Failing \\leq 5:", var x] => x,
			_ => throw new FormatException("Bad format of fourth details row: " + details[3].Show())
		};

		var extra = afterDetails.Items().Any(x => x is not WhiteSpace)
			? comp.ToHTML(afterDetails)
			: null;

		return new(name, arcanum, powerLevel, combat, reaction, distance, duration, castingTime, components, brief, effect, crit, fail, extra);
    }

    public ISource<Spell> Instantiate(Config.Source src)
    {
		return src switch
		{
			Config.CopySource s => new Copy<Spell>(s.From),
			Config.LatexSource l => new LatexFiles<Spell>(this, l),
			Config.OverleafSource o => new Overleaf<Spell>(this, o),
			Config.DndWikiSource => throw new ArgumentException("The DnDWiki doesn't have any Goedendag spells"),
			_ => throw new ArgumentException($"Invalid source for Goedendag: {src}")
		};
    }
}