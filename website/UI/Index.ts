
/** Handles the index.html UI */
namespace UI
{
	import IGame = Games.IGame

	export class Index<TSpell extends Data.ISpell>
	{
		private readonly game : IGame<TSpell>;
		private readonly table : Table<TSpell>;
		private readonly books : Data.BookIndex

		/** Initializes an the page UI */
		static async init<TSpell extends Data.ISpell>(game : IGame<TSpell>) : Promise<Index<TSpell>>
		{
			const p = new URLSearchParams(window.location.search);
			const ind = new Index(game, new Table(game, p.get("q")), await game.getBooks());

			await ind.makeSourceSelector(p.getAll("from"))

			return ind;
		}

		/** Handles setting up UI callbacks and creation the source selectors */
		private constructor(game : IGame<TSpell>, table : Table<TSpell>, books : Data.BookIndex)
		{
			this.game = game;
			this.table = table;
			this.books = books;

			document.title = `Grimoire: ${game.fullName} Spells`

			document.getElementById("static-link").onclick = _ => {
				const url = `${window.location.origin}${window.location.pathname}?${this.urlParams()}`;
				console.log(url);
				navigator.clipboard.writeText(url)
				return false;
			}

			document.getElementById("create-list").onclick = _ => this.makeSpellList({
				query : table.query,
				sources : this.selectedSources(),
				prepared : [],
				game : this.game.shorthand
			});

			{
				const uploadButton = document.getElementById("faux-upload-list") as HTMLButtonElement;
				const uploadInput = document.getElementById("upload-list") as HTMLInputElement;

				uploadButton.onclick = _ => uploadInput.click();
				uploadButton.ondragover = ev => ev.preventDefault();
				uploadButton.ondrop = async ev => {
					ev.preventDefault();

					if(!ev.dataTransfer)
						return;

					await this.filesToSpellList(ev.dataTransfer.files);
				}
				uploadInput.oninput = async ev => {
					await this.filesToSpellList(uploadInput.files);
				}
			}

			document.getElementById("spell-card-view").onclick = _ => window.location.href = `cards.html?${this.urlParams()}`;
		}


		/** Creates the source selector
		 * @param preload A list of book IDs to import immediately
		*/
		private async makeSourceSelector(preload : string[])
		{
			let elem = document.getElementById("source-selector");
			document.getElementById("source-selector-placeholder")?.remove();

			if(Object.keys(this.books).length == 1)
			{
				const id = Object.keys(this.books)[0];
				this.table.insert(await this.game.fetchSource(id));
				return;
			}

			for (const id in this.books)
			{
				let container = document.createElement("div");
				container.innerText = this.books[id];

				let l = loading();
				setHidden(l, true);
				let select = document.createElement("input");

				select.type = "checkbox";
				select.id = `source_${id}`;
				select.checked = false;

				select.onchange = async _ => {
					if(select.checked)
					{
						setHidden(l, false);
						this.table.insert(await this.game.fetchSource(id));
						setHidden(l, true);
					}
					else
						this.table.deleteIf(s => s.source === id);
				}

				container.appendChild(select);
				container.appendChild(l);
				elem?.appendChild(container);

				if(preload.some(x => x.toUpperCase() === id))
				{
					select.checked = true;
					select.onchange(null);
				}
			}
		}

		/** Reproduces a query string (without leading '?') that encodes the current selected sources and query */
		private urlParams() : string
		{
			return this.selectedSources()
				.map(x => `from=${encodeURIComponent(x)}`)
				.concat(`q=${encodeURIComponent(this.table.searchField.value)}`)
				.concat(`game=${encodeURIComponent(this.game.shorthand)}`)
				.join('&');
		}

		/** The sources that are currently selected */
		private selectedSources() : string[]
		{
			const k = Object.keys(this.books);

			return (k.length == 1)
				? [ k[0] ]
				: k.filter(id => (document.getElementById(`source_${id}`) as HTMLInputElement).checked);
		}

		/** Creates a spell list from the current filter and switches location to its list view */
		private makeSpellList(list : Data.SpellList)
		{
			const name = prompt("Name for spell list?");

			if(!name)
				return;

			window.localStorage.setItem(name, JSON.stringify(list));
			window.location.href = `list.html#${name}`
		}

		/** Creates a spell list from a file supplied via upload that contains .json generated by saving a list */
		private async filesToSpellList(files : FileList)
		{
			if(!files || files.length != 1 || files[0].type != "application/json")
				return;

			const data = loadSpellList(JSON.parse(await files[0].text()));

			this.makeSpellList({ query: data.query, sources: data.sources, prepared : data.prepared, game : data.game });
		}
	}

	/** Initializes the index UI. Called from index.html on page load. */
	export function initIndex()
	{
		withGame(async function(g) {
			await Index.init(g);
		});
	}
}