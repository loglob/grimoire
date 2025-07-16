namespace Games.Goedendag
{
	import bold = Util.bold
	import child = Util.child
	import same = Util.same
	import infixOf = Util.infixOf
	import HtmlCode = Data.HtmlCode

	export const Arcana = {
		General: 0,
		Nature:  1,
		Elementalism: 2,
		Charms: 3,
		Conjuration: 4,
		Divine: 5,
		Ritual: 6,
		Wytch: 7
	} as const;

	export const PowerLevelDCs = {
		Generalist: 15,
		Petty: 15,
		Lesser: 22,
		Greater: 30,
		Ritual: 0
	} as const;

	export type Spell =
	{
		name : string,
		arcanum : keyof (typeof Arcana),
		powerLevel : keyof (typeof PowerLevelDCs),
		combat : boolean,
		reaction : boolean,
		distance : HtmlCode,
		duration : HtmlCode,
		castingTime : HtmlCode,
		components : HtmlCode,
		brief : HtmlCode,
		effect : HtmlCode,
		critSuccess : HtmlCode,
		critFail : HtmlCode,
		extra : HtmlCode | undefined,
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

	const timeUnits : { [k : string] : number } = {
		"acp": 1,
		"[s]": 1,
		"[turn]": 3,
		"[turns]": 3,
		// Although turns and rounds are both defined to be 3 seconds
		// and all turns happen "concurrently" in the round,
		// an entire round is longer than a turn in causal terms
		"[round]": 3.001,
		"[rounds]": 3.001,
		"[min]": 60,
		"[h]": 60*60,
		"[d]": 24*60*60,
		"[w]": 7*24*60*60,
		"[m]": 365.2425*24*60*60 / 12
	}

	export function normalizeDistance(n : string, stage : number = 0) : number|null
	{
		// stage 0: keywords
		if(stage <= 0)
		{
			if(/^touch$/i.test(n))
				return 0.1;
			else if(/^any$/i.test(n))
				return Infinity;
			else if(/^vision$/i.test(n))
				return 5000;
		}

		// stage 1: filler
		if(stage <= 1)
		{
			const r = n.match(/^(.*)\s+(radius|diameter)$/i)

			if(r)
				return normalizeDistance(r[1].trim(), 1.5);
		}
		if(stage <= 1.5)
		{
			const v = n.match(/^vision\s*\(\s*maximum\s+([^()]+)\)$/i)

			if(v)
				return normalizeDistance(v[1].trim(), 2);
		}

		// stage 2: units
		if(stage <= 2)
		{
			const unit = n.match(/^(.*)\s+\[(.*)\]$/)

			if(unit) switch(unit[2])
			{
				case "m": return normalizeDistance(unit[1].trim(), 3);
				case "km": return 1000 * normalizeDistance(unit[1].trim(), 3);
			}

			// you need a unit of some kind
			return null;
		}

		// stage 3: operator resolution
		if(stage <= 3)
		{
			const mul = n.split(/·|&#183;|&#xB7;|&centerdot;/i)

			if(mul.length > 1)
				return mul.reduce((p,c) => p * normalizeDistance(c.trim(), 4), 1);

			const div = n.split('/', 2)

			if(div.length == 2)
				return normalizeDistance(div[0], 4) / normalizeDistance(div[1], 4);
		}

		// stage 4: variable/constant decision
		if(stage <= 4)
		{
			if(/\d+/.test(n))
				return Number(n);
			// don't bother with an exhaustive list of every class and stat the game
			// pick 6 as universal constant for those
			if(/\w+/.test(n))
				return 6;
		}

		return null;
	}

	export class Game extends IGame<Spell>
	{
		tableHeaders: (keyof Spell)[] = [
			"powerLevel", "arcanum", "castingTime", "distance", "combat", "reaction"
		]

		customComparers = {
			"powerLevel": cmpPowerLevel,
			"arcanum": cmpArcana,
			"castingTime": (x : Spell, y : Spell) => Games.compareQuantities(timeUnits, x.castingTime, y.castingTime),
			"distance": (x : Spell, y : Spell) => Games.compareNorm(normalizeDistance, x.distance, y.distance)
		};

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

			return  infixOf(term, s.name)
				|| [ s.arcanum, s.powerLevel, s.distance, s.duration, s.castingTime ].some(x => same(x, term))
				|| Util.fieldTermMatch(s, term, "combat", "reaction")
				|| (this.isPrepared && term === "prepared" && this.isPrepared(s))
				|| (term[0] === '$' && s.components && infixOf(term1, s.components))
				|| (term[0] === '\\' && same(s.name, term1))
				|| Util.fullTextMatch(term, s.brief, s.effect, s.critSuccess, s.critFail, s.extra)
		}

		cardOrder(spells: Spell[]): Spell[]
		{
			return spells
				.sort((a,b) => a.name > b.name ? +1 : -1)
		}

		details(spell: Spell, _book : string, div: HTMLDivElement): void
		{
			child(div, "p", "subtle").innerHTML = spell.brief;

			const prop =  child(div, "table");

			for (const kvp of fmtFields(spell))
			{
				const r = child(prop, "tr");
				child(r, "th").innerHTML = kvp[0];
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