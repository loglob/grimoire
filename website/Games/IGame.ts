namespace Games
{
	/**
	 * @param gold Number of silver pieces per gold piece
	 * @param silver Number of copper pieces per silver piece
	 */
	export type Denominations = { readonly gold: number, readonly silver : number }
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

	/** Context in which materials can be resolved to prices, respective to some spell type */
	export abstract class IMaterialContext<TSpell extends Data.ISpell, TMaterial extends Data.IMaterial>
	{
		/** Gives the size of coin denominations for formatting */
		abstract readonly denominations : Denominations
		readonly game : IGame<TSpell>

		constructor(game : IGame<TSpell>)
		{
			this.game = game
		}

		/** Extracts the materials required by a spell */
		abstract getMaterials(spell : TSpell) : TMaterial[]

		/** Formats a single material
		 * @param materialsPage If true, the output format is for the materials page
		 */
		abstract formatMaterial(mat : TMaterial, materialsPage : boolean) : HTMLElement

		/** Formats an entire material list as a comma-separated list
		 * @param materialsPage If true, the output format is for the materials page,
		 * 						applies CSS formatting to separators and doesn't use `and` as separator,
		 * 						and appends a formatted price
		 */
		formatMaterials(materials : TMaterial[], materialsPage : boolean) : HTMLElement
		{
			const container = document.createElement("span")

			materials.forEach((m, ix) => {
				if(ix > 0)
				{
					if(materialsPage)
						Util.child(container, "span", "material-sep").innerText = ",";
					else
					{
						var sep = (materials.length > 2) ? ',' : '';
						if(ix + 1 == materials.length)
							sep += " and";

						container.append(sep + ' ');
					}
				}

				container.append(this.formatMaterial(m, materialsPage));

				if(materialsPage)
				{
					var priceContainer = Util.child(container, "b");

					if(m.reference)
					{
						const linkContainer = Util.child(priceContainer, "a") as HTMLLinkElement
						linkContainer.href = m.reference
						priceContainer = linkContainer
					}

					priceContainer.append(' (')

					if(m.price !== null)
						priceContainer.append(this.formatPrice(m.price))
					else
						priceContainer.append('?')

					priceContainer.append(')')
				}
			});

			return container
		}

		/** Formats price according to `denominations` */
		formatPrice(totalCopper : number) : HTMLElement
		{
			const container = document.createElement("span")
			const cuPerAu = this.denominations.gold * this.denominations.silver
			const wasZero = totalCopper == 0

			if(totalCopper >= cuPerAu)
			{
				container.appendChild(document.createTextNode(Math.floor(totalCopper / cuPerAu).toString()))
				totalCopper %= cuPerAu;

				var coin = document.createElement("b")
				coin.className = "gold"
				coin.innerText = "Ⓖ"
				container.appendChild(coin)
			}

			if(totalCopper >= this.denominations.silver)
			{
				container.appendChild(document.createTextNode(Math.floor(totalCopper / this.denominations.silver).toString()))
				totalCopper %= this.denominations.silver;

				var coin = document.createElement("b")
				coin.className = "silver"
				coin.innerText = "Ⓢ"
				container.appendChild(coin)
			}

			if(totalCopper > 0 || wasZero)
			{
				container.appendChild(document.createTextNode(totalCopper.toFixed(1).replace(".0", ""))) // wow JS

				var coin = document.createElement("b")
				coin.className = "copper"
				coin.innerText = "Ⓒ"
				container.appendChild(coin)
			}

			return container
		}
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

		/** @returns The URL for a spell's details page */
		spellURL(spell : TSpell) : string
		{
			return `details.html?game=${this.shorthand}&from=${encodeURIComponent(spell.source)}&spell=${encodeURIComponent(spell.name)}`
		}

		/** Finds this game's material context, or returns `null` if no context  */
		withMaterials<A>(consumer : <TMaterial extends Data.IMaterial>(ctx : IMaterialContext<TSpell, TMaterial>) => A) : A | null
		{
			return null
		}
	};
}
