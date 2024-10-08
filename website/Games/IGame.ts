namespace Games
{
	export type IsPrepared<TSpell> = ((sp : TSpell) => boolean)|null;

	export function compareNorm(normalize : (s : string) => number|null, l : string, r : string) : number
	{
		const lv = normalize(l), rv = normalize(r)

		if(lv && rv)
			return lv - rv
		else if(lv)
			return -1
		else if(rv)
			return +1
		else
			return l > r ? +1 : l < r ? -1 : 0;
	}

	/** Compares two quantities with specified units.
		Orders all valid quantities as smaller than all invalid quantities.
		Understands alternatives separated by `or`, `,` or `/`.
		@param units Expects unit names to be case-insensitive and given in all lowercase
	 */
	export function compareQuantities(units : { [unit : string] : number }, l : string, r : string) : number
	{
		const unitRegex = /^(\d+)\s+(.+)$/;
		const sep = /\s*(\sor\s|\/|,)\s*/

		function normalize(str : string) : number|null
		{
			const xs = str.split(sep).map(x =>
			{
				const m = x.match(unitRegex)

				if(m === null)
					return null;

				const unit = m[2].toLowerCase(), v = Number(m[1])

				return unit in units ? units[unit] * v : null
			}).filter(x => x !== null);

			if(xs.length)
				return Math.min(...xs);

			console.log(`Bad quantity: ${str}`);
			return null;
		}

		return compareNorm(normalize, l, r);
	}

	/**
	 * @template TSpell The local spell type
	 */
	export abstract class IGame<TSpell extends Data.ISpell>
	{
		/** A unique shorthand identifying this game */
		readonly shorthand : string;
		/** The full name for this game */
		readonly fullName : string;
		/** All sources available for this game. Dynamically read from the DB index. */
		readonly books : Data.BookIndex;

		/** The fields that are listed in index view */
		readonly abstract tableHeaders : (keyof TSpell)[];
		/** A set of custom comparers to use for table sorting */
		readonly abstract customComparers : Partial<{ [key in keyof TSpell] : ((a : TSpell, b : TSpell) => number); }>

		/** Overwritten by some front-ends with a predicate that checks whether a function is prepared */
		isPrepared : ((sp : TSpell) => boolean)|null = null

		constructor(shorthand : string, fullName : string, books : Data.BookIndex)
		{
			this.shorthand = shorthand;
			this.fullName = fullName;
			this.books = books;
		}

		/** Checks whether a spell matches a single search term
		 * @param term A single search term, without joining operators or the negation operator
		 * @param spell A spell to check against
		 * @param isPrepared Determines whether a spell is prepared, if that is sensible on the current endpoint
		 * @returns true iff the spell is selected by the term
		 */
		abstract spellMatchesTerm(term : string, spell : TSpell) : boolean;

		spellMatchesQuery(query : Data.Query, s : TSpell) : boolean
		{
			return query
				.every(x => x
					.some(y => y
						.every(z =>
							(z[0] === '!')
								? !this.spellMatchesTerm(z.substring(1), s)
								: this.spellMatchesTerm(z, s)
				)	)	);
		}

		/** @returns All spells from that source */
		async fetchSource(source : string) : Promise<TSpell[]>
		{
			return await (await fetch(`db/${this.shorthand}/${source}.json`)).json();
		}

		/** @returns All spells from those sources */
		async fetchSources(...sources : string[]) : Promise<TSpell[]>
		{
			return (await Promise.all(sources.map(s => this.fetchSource(s)))).flat();
		}

		/** Generates a single spell card as a self-contained HTML element
			@param book The full name of the source
		*/
		abstract spellCard(spell : TSpell, book : string) : HTMLDivElement;

		/** Brings a list of spells into default card order. May be in-place.  */
		abstract cardOrder(spells : TSpell[]) : TSpell[];

		/** Formats a spell's details and embeds them into the given <div> */
		abstract details(spell : TSpell, book : string, div : HTMLDivElement) : void;
	};
}
