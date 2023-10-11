using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using static Latex;

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

    public Spell ExtractLatexSpell(Latex comp, string source, IEnumerable<Latex.Token> body, string? upcast)
    {
		//Console.WriteLine(Untokenize(body));
		var pos = body.GetEnumerator();
		SkipWS(pos);

		// Why is there 1 extra arg?
		var args = GetArgs(pos, 3);
		Arcanum arcanum;

		{
			var link = Untokenize(args[1]);

			arcanum = link.Split('_') switch {
				["arc", _] => throw new NotASpellException(),
				[_, "gen",  _] => Arcanum.General,
				[_, "nat",  _] => Arcanum.Nature,
				[_, "ele",  _] => Arcanum.Elementalism,
				[_, "cha",  _] => Arcanum.Charms,
				[_, "div",  _] => Arcanum.Divine,
				[_, "conj", _] => Arcanum.Conjuration,
				_ => throw new FormatException($"Unexpected hyperref format '{link}'")
			};
		}


		bool combat = false;
		bool reaction = false;
		string name;

		{
			var nameWords = Untokenize(args[2]).Split();
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

		
		string brief = Untokenize(pos.TakeWhile(x => x is not Latex.Environment));
		
		var props = Tabular(((Latex.Environment)pos.Current).inner);

		if(props.Count != 2 || props[0].Length != 4)
			throw new FormatException($"Properties must be a 2x4 table, got {props.Count}x{props[0].Length}");

		var p0 = props[0].ArraySelect(x => Untokenize(x, true));
		var (distance, duration) = p0 switch {
			[ "Distance:", var x, "Duration:", var y ] => (x, y),
			_ => throw new FormatException("Bad format of first properties row: " + p0.Show())
		};

		var p1 = props[1].ArraySelect(x => Untokenize(x, true));
		var (castingTime, powerLevel) = p1 switch {
			[ "Casting Time:", var x, "Power Level:", var y ] => (x, Enum.Parse<PowerLevel>(y, true)),
			_ => throw new FormatException("Bad format of second properties row: " + p1.Show())
		};

		if(! pos.MoveNext()) 
			throw new FormatException("No data after properties table");

		SkipWS(pos);
		var details = Tabular(((Latex.Environment)pos.Current).inner);

		if(details.Count != 4 || details[0].Length != 2)
			throw new FormatException("Details table must be 4x2");
		
		var d0 = details[0].ArraySelect(x => Untokenize(x, true));
		var components= d0 switch {
			["Components:", var x] => x,
			_ => throw new FormatException("Bad format of first details row: " + d0.Show())
		};
		var d1 = details[1].ArraySelect(x => Untokenize(x, true));
		var effect = d1 switch {
			["Effect:", var x] => x,
			_ => throw new FormatException("Bad format of second details row: " + d1.Show())
		};
		var d2 = details[2].ArraySelect(x => Untokenize(x, true));
		var crit = d2  switch {
			["Passing \\geq 10:", var x] => x,
			_ => throw new FormatException("Bad format of third details row: " + d2.Show())
		};
		var d3 = details[3].ArraySelect(x => Untokenize(x, true));
		var fail = d3  switch {
			["Failing \\leq 5:", var x] => x,
			_ => throw new FormatException("Bad format of fourth details row: " + d3.Show())
		};

		var trailing = pos.FromHere();

		var extra = trailing.Any(x => x is not WhiteSpace)
			? comp.LatexToHtml(trailing)
			: null;

		return new(name, arcanum, powerLevel, combat, reaction, distance, castingTime, components, brief, effect, crit, fail, extra);
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