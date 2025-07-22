namespace UI
{
	import IGame = Games.IGame
	import Query = Data.Query
	import Sorting = Data.Sorting

	type ColumnGenerator<in TSpell> = (s : TSpell) => HTMLTableCellElement[]

	/** Encapsulates the state of the table that displays the currently filtered spells. */
	export class Table<TSpell extends Data.ISpell>
	{
		readonly game : IGame<TSpell>

		/** The text input for the current search query */
		readonly searchField : HTMLInputElement;

		/** Called whenever the set of displayed spells changes */
		onDisplayChange : () => void = () => {}

		/** The current search filter */
		query : Query = []
		/** Spells in exact display order (i.e. already filtered) */
		private display : TSpell[] = []
		/** The current sorting of `display` and `table` */
		private sorting : Sorting<TSpell>
		/** All known spells, before filtering */
		private spells : TSpell[] = []
		/** The HTML table that contains rows corresponding to `display` */
		private readonly table : HTMLTableElement
		private readonly customRowElements : ColumnGenerator<TSpell>

		/** Initializes the table with the known headers, and sets up the search-field text input to filter the table
			@param q The query to load on the table
			@param initial The initial set of spells. Filtered by q.
			@param customRowElements A callback for prepending custom row elements before each row
		*/
		constructor(game : IGame<TSpell>, q : string|null = null, sort : string|null = null, customRowElements : ColumnGenerator<TSpell>|null = null)
		{
			const UP_ARROW = "\u2191";
			const DOWN_ARROW = "\u2193";
			this.game = game;
			this.searchField = Util.getElement("search-field") as HTMLInputElement;
			this.sorting = sort ? Data.parseSorting(sort) : Data.defaultSorting()
			this.table = Util.getElement("spells") as HTMLTableElement;
			this.customRowElements = customRowElements ?? (() => []);

			// the <tr> containing the headers
			const tr = Util.getElement("spell-headers");

			for (const h of game.tableHeaders.concat(["name"]) )
			{
				var th : HTMLElement

				if(h === "name")
					th = Util.getElement("name-header");
				else
				{
					th = Util.child( tr, "th" );
					th.id = `${String(h)}-header`;
					th.innerText = Util.camelToTitle(String(h));
				}

				let m = document.createElement("b");
				m.id = `${String(h)}-marker`;
				th.appendChild(m);

				if(h === this.sorting.key)
					m.innerText = this.sorting.reverse ? UP_ARROW : DOWN_ARROW;

				th.onclick = _ => {
					if(this.sorting.key === h)
						this.sorting.reverse = !this.sorting.reverse;
					else
					{
						Util.getElement(`${String(this.sorting.key)}-marker`).innerText = "";
						this.sorting = { key: h, reverse: false };
					}

					m.innerText = this.sorting.reverse ? UP_ARROW : DOWN_ARROW;
					this.reset();
					return false;
				}
			}

			this.searchField.oninput = _ => {
				this.query = Data.parseQuery(this.searchField.value)
				this.reset(false);
			};

			if(q)
			{
				this.searchField.value = q;
				this.searchField.oninput(null!)
			}
		}

		/** Displays a spell as a row in the spell table. */
		private toRow(spell : TSpell) : HTMLTableRowElement
		{
			var row = document.createElement("tr");

			for (const cell of this.customRowElements(spell))
				row.appendChild(cell);

			{
				let cell = document.createElement("td");
				let link = document.createElement("a");
				link.href= this.game.spellURL(spell);
				link.innerText = spell.name;
				cell.appendChild(link);
				cell.classList.add("left");
				row.appendChild(cell);
			}

			let td = (x : string) => {
				let c = document.createElement("td");
				c.innerHTML = x;
				row.appendChild(c);
			}

			for (const h of this.game.tableHeaders)
			{
				if(typeof spell[h] === "boolean")
					td(spell[h] ? "Yes" : "No")
				else
					td(spell[h]?.toString() ?? "");
			}

			return row;
		}

		/** Updates the 'Found ??? spells' text next to the search bar */
		private updateCount()
		{
			const sp = document.getElementById("spell-count") as HTMLSpanElement;
			sp.innerText = `Found ${this.display.length} spells`
		}

		/** Removes all spells that match the predicate from the table and tableState
		 * @param pred The predicate to match for deletion
		 */
		deleteIf(pred : (spell: TSpell, index: number) => boolean)
		{
			var narrowed = false;

			for (let i = this.display.length; i--;)
			{
				if(pred(this.display[i], i))
				{
					narrowed = true;
					this.display.splice(i, 1);
					this.table.removeChild(this.table.children[i + 1]);
				}
			}

			this.spells = this.spells.filter((v,i) => !pred(v,i));

			if(narrowed)
			{
				this.updateCount();
				this.onDisplayChange();
			}
		}

		/** Inserts into the table, preserving sortedness and filtering
		 * @param spells A list of new spells
		*/
		insert(spells : TSpell[]) : void
		{
			Data.sortSpells(this.game, this.sorting, spells)

			if(this.spells.length == 0)
			{
				this.spells = spells;
				this.display = Array(...spells);
				return this.reset(false);
			}

			let off = 0;
			var widened = false;

			for(const spell of spells)
			{
				if(!this.game.spellMatchesQuery(this.query, spell))
					continue;

				widened = true;

				while(off < this.display.length && Data.cmpSpell(this.game, this.sorting, spell, this.display[off]) > 0)
					off++;

				let row = this.toRow(spell);

				if(off < this.display.length)
				{
					this.display.splice(off, 0, spell);
					this.table.insertBefore(row, this.table.children[1 + off]);
				}
				else
				{
					this.display.push(spell);
					this.table.appendChild(row);
				}

				off++;
			}

			this.spells.push(...spells);

			if(widened)
			{
				this.updateCount();
				this.onDisplayChange();
			}
		}

		/** Re-sorts and re-filters the table and rebuilds the displayed table from scratch. */
		private reset(resort : boolean = true) : void
		{
			if(resort)
				Data.sortSpells(this.game, this.sorting, this.spells);

			this.display = this.spells.filter(s => this.game.spellMatchesQuery(this.query, s));

			while(this.table.childElementCount > 1)
				this.table.removeChild(this.table.lastChild!);

			for (const s of this.display)
				this.table.appendChild(this.toRow(s));

			this.updateCount();
			this.onDisplayChange();
		}

		/** Returns the current sorting, or null if it's the default sorting */
		getSorting() : string | null
		{
			const def = Data.defaultSorting()

			if(this.sorting.key === def.key && this.sorting.reverse === def.reverse)
				return null

			var s = String(this.sorting.key)

			if(this.sorting.reverse)
				s = `-${s}`

			return s
		}

		/** Gets all displayed spells, along with their HTML elements */
		getRows() : { cells : HTMLTableCellElement[], spell : TSpell }[]
		{
			const n = this.display.length;
			const arr = []

			for(let i = 0; i < n; ++i)
			{
				arr.push({
					cells: Array.from(this.table.rows.item(i + 1)!.cells),
					spell : this.display.at(i)!
				});
			}

			return arr;
		}

		/** Invokes a callback for each currently displayed spell */
		forEachDisplayed(f : (spell : TSpell) => void)
		{
			this.display.forEach(x => f(x))
		}
	}
}
