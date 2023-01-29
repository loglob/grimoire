namespace Spells
{
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
		statBlock : string|null
	}

	export var isPrepared : (s : Spell) => boolean = null

	function spellMatchesTerm(term : string, s : Spell) : boolean
	{
		const lim = (term[0] === 'l')
			? term.substring(1).split('-').map(x => Number.parseInt(x))
			: [];

		return  s.name.toLowerCase().includes(term)
			|| s.classes.some(c => c.toLowerCase() === term)
			|| s.school.toLowerCase() === term
			|| s.castingTime.toLowerCase() === term
			|| s.duration.toLowerCase() === term
			|| (s.ritual && term === "ritual")
			|| (s.concentration && term === "concentration")
			|| (s.upcast && term === "upcast")
			|| (isPrepared && term === "prepared" && isPrepared(s))
			|| (term[0] === '\\' && s.name.toLowerCase() === term.substring(1))
			|| (lim.length == 1 && lim[0] == s.level)
			|| (lim.length == 2 && lim[0] <= s.level && s.level <= lim[1]);
	}

	/** Checks whether a spell matches a filter
	 * @param filter A filter split by the binary operators according to their precedence and trimmed
	 * 					i.e. first by ';'. then by '|' and then by ','
	 * @param s The spell to test against
	 * @returns true iff the spell is selected by the filter
	 */
	export function match(filter : string[][][], s : Spell) : boolean
	{
		return filter
			.every(x => x
				.some(y => y
					.every(z =>
						(z[0] === '!')
							? !spellMatchesTerm(z.substring(1), s)
							: spellMatchesTerm(z, s)
			)	)	);
	}

	/** Normalizes a query to a filter. Resolved operators, squeezes whitespace and converts to lowercase. */
	export function toFilter(query : string) : string[][][]
	{
		return query
			.split(';').map(x => x
				.split('|').map(y => y
					.split(',').map(z => z.toLowerCase().split(/\s+/).filter(x => x.length).join(' '))));
	}

	/**
	 * @param source A source id returned by getSources()
	 * @returns The spells of that source
	 */
	export async function getFrom(source : string) : Promise<Spell[]>
	{
		const r = await fetch(`db/${source}.json`);
		return await r.json();
	}

	export type SpellList =
	{
		/** The sources this list is built from */
		sources : string[],
		/** List of spell names that are prepared. May be outside of the include set. */
		prepared : string[],
		/** The filter for the underlying spell set */
		filter : string[][][]
	}
}
