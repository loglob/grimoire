/** The datatypes for interacting with the scraper database */
namespace Data
{
	export type BookIndex = { [id : string] : string };

	export type GameIndex = { [id : string]: BookIndex; };

	export type Query = string[][][];

	export type Sorting<TSpell extends ISpell> = { key : keyof TSpell, reverse : boolean }

	export function defaultSorting() : Sorting<ISpell>
	{
		return { key: "name", reverse: true };
	}

	/** Explicitly annotates Html code strings */
	export type HtmlCode = string;

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

	/** A generic material component */
	export interface IMaterial
	{
		/** Displayable code for this entire component
		 * Depending on game, other fields may be encoded or separate.
		 */
		readonly display : HtmlCode,
		/** Whether this component is consumed */
		readonly consumed : boolean,
		/** The price of this component, or null if unknown */
		readonly price : number | null,
		/** A reference for the price.
		 * May be present independently of price.
		 */
		readonly reference : string | null
	}

	export interface ISpell
	{
		readonly name : string
		readonly source : string
	}

	/** Normalizes a query. Resolves operators and squeezes whitespace. */
	export function parseQuery(query : string) : Data.Query
	{
		return query
			.split(';').map(x => x
				.split('|').map(y => y
					.split(',').map(z => z.split(/\s+/).filter(x => x.length).join(' '))));
	}

	/** Parses a sorting passed as URL parameter */
	export function parseSorting<TSpell extends ISpell>(sorting : string) : Sorting<TSpell>
	{
		const rev = sorting[0] === '-' ? true : false;

		if(rev)
			sorting = sorting.substring(1);

		// there is no (automatic) runtime check for this cast, I'd have to extend Game
		return { key: <keyof TSpell> sorting, reverse: rev };
	}

	/** Compares two spells using the given sorting */
	export function cmpSpell<TSpell extends ISpell>(game : Games.IGame<TSpell>, s : Sorting<TSpell>, l : TSpell, r : TSpell) : number
	{
		const cmp = (s.key in game.customComparers)
			? game.customComparers[s.key]!(l, r)
			: (l[s.key] > r[s.key] ? -1 : l[s.key] < r[s.key] ? +1 : 0);

		return (s.reverse ? -cmp : +cmp);
	}

	export function sortSpells<TSpell extends ISpell>(game : Games.IGame<TSpell>, s : Sorting<TSpell>, spells : TSpell[])
	{
		spells.sort((x,y) => cmpSpell(game, s, x, y))
	}
}