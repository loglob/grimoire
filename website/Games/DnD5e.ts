
namespace Games.DnD5e
{
	import bold = Util.bold
	import child = Util.child
	import same = Util.same
	import infixOf = Util.infixOf

	export type Spell =
	{
		name : string, source : string,
		school : string, level : number,
		castingTime : string, reaction : string, ritual : boolean,
		range : string,
		verbal : boolean, somatic : boolean, materials : string|null,
		concentration : boolean, duration : string,
		description : string, upcast : string|null,
		classes : string[],
		statBlock : string|null,
		hint : string|null
	}

	const timeUnits : { [k : string] : number } = {
		"reaction": 1,
		"bonus action": 2,
		"action": 3,
		"minute": 60,
		"minutes": 60,
		"hour": 60*60,
		"hours": 60*60
	} as const

	export class Game extends IGame<Spell>
	{
		tableHeaders: (keyof Spell)[] = [
			"level", "school", "castingTime", "ritual", "concentration", "source"
		] as const

		customComparers = {
			"castingTime": (x : Spell, y : Spell) => Games.compareQuantities(timeUnits, x.castingTime, y.castingTime)
		} as const

		spellCard(spell: Spell, book: string): HTMLDivElement
		{
			const div = document.createElement("div");
			child(div, "hr");
			child(div, "h3").innerText = spell.name;
			child(div, "p").innerHTML = spell.school;

			{
				const properties = child(div, "p");

				properties.append(
					"Level: ",
					bold( spell.level ? spell.level.toString() : "Cantrip" ),
					document.createElement("br"),
					"Casting time: ",
					bold( spell.castingTime ),
					document.createElement("br"),
					"Range: ",
					bold( spell.range ),
					document.createElement("br"),
					"Components: ",
					bold( (spell.verbal ? ["V"] : [])
						.concat(spell.somatic ? ["S"] : [])
						.concat(spell.materials ? [`M (${spell.materials})`] : [])
						.join(", ") ),
					document.createElement("br"),
					"Duration: ",
					bold( spell.duration )
				);
			}

			child(div, "p").innerHTML = spell.description;

			if(spell.upcast)
			{
				child(div, "h4").innerText = "At higher levels";
				child(div, "p").innerHTML = spell.upcast;
			}

			if(spell.statBlock)
			{
				child(div, "p").innerHTML = spell.statBlock;
				child(div, "hr", "subtle")
			}

			child(div, "p", "subtle from").innerHTML = spell.hint ? `${book} (${spell.hint})` : book;

			return div;
		}

		spellMatchesTerm(term: string, s: Spell): boolean
		{
			const term1 = term.substring(1);
			const lim = (term[0] === 'L')
				? term1.split('-').map(x => Number.parseInt(x))
				: [];

			return infixOf(term, s.name)
				|| [ s.school, s.castingTime, s.duration, ...s.classes ].some(x => same(term, x))
				|| Util.fieldTermMatch(s, term, "verbal", "somatic", "ritual", "concentration", "upcast")
				|| (s.materials && term === "material")
				|| (this.isPrepared && term === "prepared" && this.isPrepared(s))
				|| (term[0] === '$' && s.materials && infixOf(term1, s.materials))
				|| (term[0] === ':' && same(s.source, term))
				|| (term[0] === '#' && s.hint && infixOf(term1, s.hint))
				|| (term[0] === '\\' && same(s.name, term1))
				|| (term === "$$" && s.materials && /[1-9][0-9,]+\s*gp/i.test(s.materials))
				|| (lim.length == 1 && lim[0] == s.level)
				|| (lim.length == 2 && lim[0] <= s.level && s.level <= lim[1])
				|| Util.fullTextMatch(term, s.description, s.upcast, s.statBlock);
		}

		cardOrder(spells: Spell[]): Spell[]
		{
			return spells
				.sort((a,b) => a.name > b.name ? +1 : -1)
				.sort((a,b) => a.level - b.level)
		}

		details(sp: Spell, book : string, div: HTMLDivElement): void
		{
			// level + class
			child(div, "i").innerText = sp.level
				? `${Util.ordinal(sp.level)}-level ${sp.school}`
				: `${sp.school} Cantrip`;

			// table of properties
			const tr = Util.children(child(div, "table"), "tr", 5);
			child(tr[0], "th").innerText = "Casting Time";
			child(tr[0], "td").innerHTML = sp.reaction ? `${sp.castingTime}, ${sp.reaction}` : sp.castingTime;
			child(tr[1], "th").innerText = "Range";
			child(tr[1], "td").innerHTML = sp.range;

			// display components s.t. expensive and consumed materials are highlighted
			{
				child(tr[2], "th").innerText = "Components"
				const comp = child(tr[2], "td");

				comp.append((sp.verbal ? ["V"] : []).concat(sp.somatic ? ["S"] : []).join(", "));

				if(sp.materials)
				{
					const costRegex = /[1-9][0-9,]+\s*gp/ig;
					let nodes : (string|Node)[] = [];
					let off = 0;
					let expensive = false;

					for(let match of Array.from(sp.materials.matchAll(costRegex)))
					{
						nodes.push(sp.materials.substring(off, match.index));
						nodes.push(bold( match[0] ));
						console.log(off, match, match.index + match[0].length);
						off = match.index + match[0].length;
						expensive = true;
					}

					nodes.push(sp.materials.substring(off));

					const consume = /(which the spell consumes|consumed by the spell)$/i.test(sp.materials)


					if(sp.verbal || sp.somatic)
						comp.append(", ");

					comp.append(expensive ? bold(consume ? "M*" : "M") : "M", " (");
					comp.append(...nodes, ")");
				}
			}

			child(tr[3], "th").innerText = "Duration";
			child(tr[3], "td").innerHTML = (sp.concentration ? "Concentration, up to " : "") + sp.duration;

			child(tr[4], "th").innerText = "Classes";
			child(tr[4], "td").innerHTML = sp.classes.join(", ");

			child(div, "hr");
			child(div, "div").innerHTML = sp.description + (sp.statBlock ? sp.statBlock : "");
			child(div, "hr");

			if(sp.upcast)
				child(div, "div").innerHTML = "<strong>At higher levels: </strong>" + sp.upcast;

			child(div, "p", "subtle from").innerHTML = typeof sp.hint === "string" ? `${book} (${sp.hint})` : book;
		}
	}
}