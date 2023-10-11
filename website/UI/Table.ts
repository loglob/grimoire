namespace UI
{
	import IGame = Games.IGame
	import Query = Data.Query

	/** Encapsulates the state of the table that displays the currently filtered spells. */
	export class Table<TSpell extends Data.ISpell>
	{
		readonly game : IGame<TSpell>

		/** The text input for the current search query */
		readonly searchField : HTMLInputElement;

		private sortOn : keyof TSpell
		private reverse : boolean = true
		query : Query = []
		private spells : TSpell[] = []
		private display : TSpell[] = []
		private readonly table : HTMLTableElement
		private readonly customRowElements : (s : TSpell) => HTMLTableCellElement[] = null

		/** Initializes the table with the known headers, and sets up the search-field text input to filter the table
			@param q The query to load on the table
			@param initial The initial set of spells. Filtered by q.
			@param customRowElements A callback for prepending custom row elements before each row
		*/
		constructor(game : IGame<TSpell>, q : string|null = null, customRowElements : (s : TSpell) => HTMLTableCellElement[] = null)
		{
			const UP_ARROW = "\u2191";
			const DOWN_ARROW = "\u2193";
			this.game = game;
			this.searchField = document.getElementById("search-field") as HTMLInputElement;
			this.sortOn = "name"
			this.table = document.getElementById("spells") as HTMLTableElement;
			this.customRowElements = customRowElements;

			// the <tr> containing the headers 
			const tr = document.getElementById("spell-headers");

			for (const h of game.tableHeaders.concat(["name"]) )
			{
				var th : HTMLElement

				if(h === "name")
					th = document.getElementById("name-header")
				else
				{
					th = Util.child( tr, "th" );
					th.id = `${String(h)}-header`;
					th.innerText = Util.camelToTitle(String(h));
				}

				let m = document.createElement("b");
				m.id = `${String(h)}-marker`;
				th.appendChild(m);

				if(h === this.sortOn)
					m.innerText = this.reverse ? UP_ARROW : DOWN_ARROW;

				th.onclick = _ => {
					if(this.sortOn === h)
						this.reverse = !this.reverse;
					else
					{
						document.getElementById(`${String(this.sortOn)}-marker`).innerText = "";
						this.sortOn = h;
						this.reverse = false;
					}

					m.innerText = this.reverse ? UP_ARROW : DOWN_ARROW;
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
				this.searchField.oninput(null)
			}
		}

		/** Compares two spells for sorting according to the current table sort setting. */
		compareSpell(l : TSpell, r : TSpell) : number
		{
			const so = this.sortOn;
			const sos = so as string;

			const cmp = (sos in this.game.customComparers)
				? this.game.customComparers[sos](l, r)
				: (l[so] > r[so] ? -1 : l[so] < r[so] ? +1 : 0)

			return (this.reverse ? -cmp : +cmp);
		}

		/** Displays a spell as a row in the spell table. */
		toRow(spell : TSpell) : HTMLTableRowElement
		{
			var row = document.createElement("tr");

			if(this.customRowElements)
			{
				for (const cell of this.customRowElements(spell))
					row.appendChild(cell);
			}

			{
				let cell = document.createElement("td");
				let link = document.createElement("a");
				link.href=`details.html?game=${this.game.shorthand}&from=${encodeURIComponent(this.game.getSource(spell))}&spell=${encodeURIComponent(spell.name)}`;
				link.innerText = spell.name;
				cell.appendChild(link);
				cell.classList.add("left");
				row.appendChild(cell);
			}

			let td = (x : string) => {
				let c = document.createElement("td");
				c.innerText = x;
				row.appendChild(c);
			}

			/** TODO: turn true/false into yes/no */
			for (const h of this.game.tableHeaders) {
				td(spell[h].toString());
			}

			return row;
		}

		/** Updates the 'Found ??? spells' text next to the search bar */
		updateCount()
		{
			const sp = document.getElementById("spell-count") as HTMLSpanElement;
			sp.innerText = `Found ${this.display.length} spells`
		}

		/** Removes all spells that match the predicate from the table and tableState
		 * @param pred The predicate to match for deletion
		 */
		deleteIf(pred : (spell: TSpell, index: number) => boolean)
		{
			for (let i = this.display.length; i--;)
			{
				if(pred(this.display[i], i))
				{
					this.display.splice(i, 1);
					this.table.removeChild(this.table.children[i + 1]);
				}
			}

			this.spells = this.spells.filter((v,i) => !pred(v,i));
			this.updateCount();
		}

		/** Inserts into the table, preserving sortedness and filtering
		 * @param spells A list of new spells
		*/
		insert(spells : TSpell[]) : void
		{
			spells.sort((x,y) => this.compareSpell(x,y));

			if(this.spells.length == 0)
			{
				this.spells = spells;
				this.display = Array(...spells);
				return this.reset(false);
			}

			let off = 0;

			for(const spell of spells)
			{
				if(!this.game.spellMatchesQuery(this.query, spell))
					continue;

				while(off < this.display.length && this.compareSpell(spell, this.display[off]) > 0)
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
			this.updateCount();
		}

		/** Re-sorts and re-filters the table and rebuilds the displayed table from scratch. */
		reset(resort : boolean = true) : void
		{
			if(resort)
				this.spells.sort((x,y) => this.compareSpell(x,y));

			this.display = this.spells.filter(s => this.game.spellMatchesQuery(this.query, s));

			while(this.table.childElementCount > 1)
				this.table.removeChild(this.table.lastChild);

			for (const s of this.display)
				this.table.appendChild(this.toRow(s));

			this.updateCount();
		}
	}
}
