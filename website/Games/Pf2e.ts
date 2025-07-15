namespace Games.Pf2e
{
	import bold = Util.bold
	import child = Util.child
	import same = Util.same
	import infixOf = Util.infixOf

	export const Traditions = {
		Arcane: 1,
		Divine: 2,
		Occult: 3,
		Primal: 4,
		Focus: 5,
		Elemental: 6
	} as const

	export type Spell =
	{
		name : string,
		source : string,
		summary : string,
		traditions : (keyof (typeof Traditions))[],
		level : number,
		castingTime : string,
		seconds : number,
		reaction : string | null,
		components : string,
		range : string | null,
		feet : number,
		targets : string | null,
		area : string | null,
		duration : string | null,
		save : string | null,
		tags : string[],
		description : string,
		page : number
	}

	const Images : { [k : string] : boolean } = {
		"Free Action": true,
		"Two Actions": true,
		"Three Actions": true,
		"Single Action": true,
		"Reaction": true
	} as const

	function fmtCastingTime(time : string) : Node|null
	{
		let op = time.split(" to ")

		if(op.length == 2)
		{
			let l = fmtCastingTime(op[0])
			let r = fmtCastingTime(op[1])

			if(l && r)
			{
				let both = document.createElement("span")
				both.append(l, " to ", r);
				return both
			}
		}
		else if(op.length == 1)
		{
			if(Images[time])
			{
				let out = document.createElement("img");
				out.setAttribute("src", `/img/${time}.png`);
				return out;
			}
		}

		return null
	}

	/** Produces 2D key-value pairs of the spell's properties */
	function fmtFields(spell : Spell, withTraditions : Boolean) : [string, string|Node][][]
	{
		const out : [string,string|Node][][] = [ ]

		function line(...xs : (keyof Spell | [keyof Spell, string])[])
		{
			const ln : [string,string][] = []

			for(const x of xs)
			{
				let name
				let key

				if(typeof x === "object")
				{
					key = x[0]
					name = x[1]
				}
				else
				{
					key = x
					name = x as string
					name = name[0].toUpperCase() + name.slice(1)
				}

				if(spell[key])
					ln.push([ name, spell[key] as string ])
			}

			if(ln.length > 0)
				out.push(ln)
		}

		if(spell.traditions.length > 0 && withTraditions)
			out.push([[ "Traditions", spell.traditions.join(", ") ]])

		out.push([[ "Level", (spell.tags.some(x => x.toLowerCase() == "cantrip") ? "Cantrip " : "Spell ") + spell.level ]])

		{
			let t = fmtCastingTime(spell.castingTime)

			if(t)
			{
				let sp = document.createElement("span")
				sp.append(t, " ", spell.components)
				out.push([[ "Cast", sp ]])
			}
			else
				out.push([[ "Cast", `${spell.castingTime}; ${spell.components}` ]])

		}


		line([ "reaction", "Trigger" ])

		line(
			"range",
			"area",
			"targets"
		)

		line(
			"save",
			"duration"
		)

		return out
	}

	function createTagList(spell : Spell, attach : HTMLElement)
	{
		const tags = child(attach, "div", "tag-list")

		for (const tag of spell.tags)
			child(tags, "div", "tag-box").innerText = tag
	}

	function createProperties(spell : Spell, details : Boolean, attach : HTMLElement)
	{
		const fields = child(attach, "p")

		for(const line of fmtFields(spell, details))
		{
			let first = true

			for(const field of line)
			{
				if(! first)
					fields.append("; ");

				(details ? child(fields, "b") : fields).append(field[0]);
				fields.append(" ");
				(details ? fields : child(fields, "b")).append(field[1]);

				first = false;
			}

			child(fields, "br")
		}
	}

	export class Game extends IGame<Spell>
	{
		readonly tableHeaders = [
			"level", "range", "castingTime", "components"
		] as (keyof Spell)[]

		readonly customComparers = {
			"castingTime": (x : Spell, y : Spell) =>  x.seconds - y.seconds,
			"range": (x : Spell, y : Spell) => {
				var diff = x.feet - y.feet

				// "0 feet", "touch", "" and "varies" all have feet == 0
				return (diff === 0 && x.feet === 0)
					? x.range.localeCompare(y.range)
					: diff;
			},
			"components": (x : Spell, y : Spell) => {
				var l = x.components.length - y.components.length;

				return l ? l : x.components.localeCompare(y.components)
			}
		} as const;

		spellCard(spell: Spell, book: string): HTMLDivElement
		{
			const div = document.createElement("div");
			child(div, "hr");
			child(div, "h3").innerText = spell.name;

			createTagList(spell, div)
			createProperties(spell, false, div)

			child(div, "div").innerHTML = spell.description;

			child(div, "p", "subtle from").innerText = `${book} (pg. ${spell.page})`;

			return div;
		}

		spellMatchesTerm(term: string, s: Spell): boolean
		{
			const term1 = term.substring(1);
			const lim = (term[0] === 'L')
				? term1.split('-').map(x => Number.parseInt(x))
				: [];
			const saves : {[k : string] : boolean } = { "will":true, "fortitude":true, "reflex":true }

			return  infixOf(term, s.name)
				|| [ s.range, s.area, s.duration, s.castingTime, ...s.tags, ...s.traditions ].some(x => x && same(x, term))
				|| (this.isPrepared && term === "prepared" && this.isPrepared(s))
				|| (s.save && saves[term.toLowerCase()] && infixOf(term, s.save))
				|| (term[0] === '$' && s.components && infixOf(term1, s.components))
				|| (term[0] === '\\' && same(s.name, term1))
				|| (lim.length == 1 && lim[0] == s.level)
				|| (lim.length == 2 && lim[0] <= s.level && s.level <= lim[1])
				|| Util.fullTextMatch(term, s.description, s.reaction, s.summary)
		}

		cardOrder(spells: Spell[]): Spell[]
		{
			return spells.sort((a,b) => a.name.localeCompare(b.name))
		}

		details(spell: Spell, book : string, div: HTMLDivElement): void
		{
			createTagList(spell, div)
			child(div, "p", "subtle").innerText = spell.summary;
			createProperties(spell, true, div)

			child(div, "div").innerHTML = spell.description

			child(div, "p", "subtle from").innerText = `${book} (pg. ${spell.page})`;
		}
	}
}