namespace Games
{
	export type IsPrepared<TSpell> = ((sp : TSpell) => boolean)|null;

	/**
	 * @template TSpell The local spell type
	 */
	export abstract class IGame<TSpell extends Data.ISpell>
	{
		/** A unique shorthand identifying this game */
		readonly shorthand : string;
		/** The full name for this game */
		readonly fullName : string;
		/** Cache for the book index */
		private books : { [id : string] : string } | null = null;

		/** The fields that are listed in index view */
		readonly abstract tableHeaders : (keyof TSpell)[];

		/** Overwritten by some front-ends with a predicate that checks whether a function is prepared */
		isPrepared : ((sp : TSpell) => boolean)|null = null

		constructor(shorthand : string, fullName : string)
		{
			this.shorthand = shorthand;
			this.fullName = fullName;
		}
		
		abstract getSource(s : TSpell) : string;

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

		/** Loads the source index from the database
		 * @returns The index showing all known sources
		 */
		async getBooks() : Promise<Data.BookIndex>
		{
			if(this.books === null)
				this.books = await (await fetch(`db/${this.shorthand}/index.json`)).json();
			
			return this.books;
		}
	};
}
