/** Code for spells */
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
		statBlock : string|null,
		hint : string|null
	}

	/** A predicate that determines whether a spell is prepared.
	 * Overwritten iff the current frontend provides that information.
	 */
	export var isPrepared : (s : Spell) => boolean = null

	/** Checks whether a spell matches a single search term
	 * @param term A single search term, without joining operators or the negation operator
	 * @param s A spell to check against
	 * @returns true iff the spell is selected by the term
	 */
	function spellMatchesTerm(term : string, s : Spell) : boolean
	{
		const term1 = term.substring(1);
		const term2 = term.substring(2);
		const lim = (term[0] === 'l')
			? term1.split('-').map(x => Number.parseInt(x))
			: [];

		return  s.name.toLowerCase().includes(term)
			|| s.classes.some(c => c.toLowerCase() === term)
			|| s.school.toLowerCase() === term
			|| s.castingTime.toLowerCase() === term
			|| s.duration.toLowerCase() === term
			|| (s.verbal && term === "verbal")
			|| (s.somatic && term === "somatic")
			|| (s.materials && term === "material")
			|| (s.ritual && term === "ritual")
			|| (s.concentration && term === "concentration")
			|| (s.upcast && term === "upcast")
			|| (isPrepared && term === "prepared" && isPrepared(s))
			|| (term[0] === '$' && s.materials && s.materials.toLowerCase().includes(term1))
			|| (term[0] === ':' && s.source.toLowerCase() === term1)
			|| (term[0] === '#' && s.hint && s.hint.toLowerCase().includes(term1))
			|| (term[0] === '/' && (term[1] === '/'
				? s.description.toLowerCase().split(/\s+/).some(w => w === term2 || w.split(/\W+/).includes(term2))
				: s.description.toLowerCase().includes(term1)))
			|| (term[0] === '\\' && s.name.toLowerCase() === term1)
			|| (term === "$$" && s.materials && /[1-9][0-9,]+\s*gp/i.test(s.materials))
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
	async function getFromOne(source : string) : Promise<Spell[]>
	{
		const r = await fetch(`db/${source}.json`);
		return await r.json();
	}

	/**
	 * @param sources Source IDs returned from getSources()
	 * @returns All spells in those sources
	 */
	export async function getFrom(...sources : string[]) : Promise<Spell[]>
	{
		return (await Promise.all(sources.map(getFromOne))).flat()
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
