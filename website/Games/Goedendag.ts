namespace Games.Goedendag
{
	import bold = Util.bold
	import child = Util.child

	export const Arcana = {
		General: 0,
		Nature:  1,
		Elementalism: 2,
		Charms: 3,
		Conjuration: 4,
		Divine: 5,
		Ritual: 6
	};

	export const PowerLevelDCs = {
		Generalist: 15,
		Petty: 15,
		Lesser: 22,
		Greater: 30,
		Ritual: 0
	};

	export type Spell =
	{
		name : string,
		arcanum : keyof (typeof Arcana),
		powerLevel : keyof (typeof PowerLevelDCs),
		combat : boolean,
		reaction : boolean,
		distance : string,
		duration : string,
		castingTime : string,
		components : string,
		brief : string,
		effect : string,
		critSuccess : string,
		critFail : string,
		extra : string | undefined,
		source : string
	}

	function cmpPowerLevel(a : Spell, b : Spell) : number
	{
		return PowerLevelDCs[a.powerLevel] - PowerLevelDCs[b.powerLevel];
	}

	function cmpArcana(a : Spell, b : Spell) : number
	{
		return Arcana[a.arcanum] - Arcana[b.arcanum];
	}

	function fmtFields(spell : Spell) : [string, string][]
	{
		const dc = PowerLevelDCs[spell.powerLevel]

		return [
			[ "Arcanum", spell.arcanum ],
			[ "Power Level", spell.powerLevel + (dc > 0 ? ` (DV ${dc})` : "") + (spell.combat ? " (C)" : "") ],
			[ "Casting Time", spell.castingTime + (spell.reaction ? " (R)" : "") ],
			[ "Distance", spell.distance ],
			[ "Duration", spell.duration ],
			[ "Components", spell.components ]
		]
	}

	export class Game extends IGame<Spell>
	{
		tableHeaders: (keyof Spell)[] = [
			"powerLevel", "arcanum", "castingTime", "distance", "combat", "reaction"
		]

		customComparers = {
			"powerLevel": cmpPowerLevel,
			"arcanum": cmpArcana
		}

		spellCard(spell: Spell, _book: string): HTMLDivElement
		{
			const div = document.createElement("div");
			child(div, "hr");

			child(div, "h3").innerText = spell.name;
			child(div, "p", "subtle").innerHTML = spell.brief;

			const p = child(div, "p");
			var fst = true;

			for (const kvp of fmtFields(spell)) {
				if(! fst)
					child(p, "br");

				p.append( kvp[0] + ": ", bold(kvp[1]) )
				fst = false;
			}

			{
				const table = child(div, "table")
				const e = child(table, "tr");
				child(e, "th").innerText = "Effect:";
				child(e, "td").innerHTML = spell.effect;

				const c = child(table, "tr");
				child(c, "th").innerHTML = "Succeeding &geq; 10:";
				child(c, "tr").innerHTML = spell.critSuccess;

				const f = child(table, "tr");
				child(f, "th").innerHTML = "Failing &leq; 5:";
				child(f, "tr").innerHTML = spell.critFail;
			}

			if(spell.extra)
			{
				child(div, "br");
				child(div, "div").innerHTML = spell.extra;
			}

			return div;
		}

		spellMatchesTerm(term: string, s: Spell): boolean
		{
			const term1 = term.substring(1);

			return  s.name.toLowerCase().includes(term)
				|| [ s.arcanum, s.powerLevel, s.distance, s.duration, s.castingTime ].some(x => x.toLowerCase() === term)
				|| [ "combat", "reaction" ].some(x => s[x as keyof Spell] && term === x)
				|| (this.isPrepared && term === "prepared" && this.isPrepared(s))
				|| (term[0] === '$' && s.components && s.components.toLowerCase().includes(term1))
				|| Util.fullTextMatch(term, s.brief, s.effect, s.critSuccess, s.critFail, s.extra)
				|| (term[0] === '\\' && s.name.toLowerCase() === term1)
		}

		cardOrder(spells: Spell[]): Spell[]
		{
			return spells
				.sort((a,b) => cmpPowerLevel(a,b))
				.sort((a,b) => a.name > b.name ? +1 : -1)
				.sort((a,b) => cmpArcana(a,b))
		}

		details(spell: Spell, _book : string, div: HTMLDivElement): void
		{
			child(div, "p", "subtle").innerHTML = spell.brief;

			const prop =  child(div, "table");

			for (const kvp of fmtFields(spell))
			{
				const r = child(prop, "tr");
				child(r, "th").innerText = kvp[0];
				child(r, "td").innerHTML = kvp[1];
			}

			child(div, "hr");
			child(div, "p").innerHTML = "<b>Effect: </b>" + spell.effect;
			child(div, "p").innerHTML = "<b>Succeeding &geq; 10: </b>" + spell.critSuccess;
			child(div, "p").innerHTML = "<b>Failing &leq; 5: </b>" + spell.critFail;

			if(spell.extra)
			{
				child(div, "hr");
				child(div, "div", "extra").innerHTML = spell.extra
			}
		}
	}
}