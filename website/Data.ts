/** The datatypes for interacting with the scraper database */
namespace Data
{
	export type BookIndex = { [id : string] : string };

	export type Query = string[][][];

	export type SpellList =
	{
		/** The sources this list is built from */
		sources : string[],
		/** List of spell names that are prepared. May be outside of the include set. */
		prepared : string[],
		/** The query for the underlying spell set */
		query : Query,
		/** The ID of the game this spell list is for */
		game : string
	}

	export type NamedSpellList = SpellList & { name : string };

	export interface ISpell
	{
		name : string,
		source : string
	}

	/** Normalizes a query. Resolved operators, squeezes whitespace and converts to lowercase. */
	export function parseQuery(query : string) : Data.Query
	{
		return query
			.split(';').map(x => x
				.split('|').map(y => y
					.split(',').map(z => z.toLowerCase().split(/\s+/).filter(x => x.length).join(' '))));
	}
}