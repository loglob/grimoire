/** Handles the index.html UI */
namespace Index
{
	import setHidden = Util.setHidden;

	/** Maps book IDs onto canonical titles */
	export var sources : { [id: string] : string } = {}

	/** Looks up existing sources and inserts them into the source selector
	 * @param preload A list of book IDs to import immediately
	*/
	async function loadSources(preload : string[])
	{
		let elem = document.getElementById("source-selector");
		sources = await Util.getSources();
		document.getElementById("source-selector-placeholder")?.remove();

		for (const id in sources)
		{
			let container = document.createElement("div");
			container.innerText = sources[id];

			let l = Util.loading();
			setHidden(l, true);
			let select = document.createElement("input");

			select.type = "checkbox";
			select.id = `source_${id}`;
			select.checked = false;

			select.onchange = async _ => {
				if(select.checked)
				{
					setHidden(l, false);
					Table.insert(await Spells.getFrom(id));
					setHidden(l, true);
				}
				else
					Table.filter(s => s.source === id);
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

	/** The sources that are currently selected */
	function selectedSources() : string[]
	{
		return Object.keys(sources)
			.filter(id => (document.getElementById(`source_${id}`) as HTMLInputElement).checked);
	}

	/** Initializes the index UI. Called from index.html on page load. */
	export function initUI()
	{
		const p = new URLSearchParams(window.location.search);
		loadSources(p.getAll("from"));

		Table.init(p.get("q"));

		document.getElementById("static-link").onclick = _ => {
			const fs = Object.keys(sources)
				.filter(x => (document.getElementById(`source_${x}`) as HTMLInputElement)?.checked)
				.map(x => `from=${encodeURIComponent(x)}`)
				.join('&');
			const url = `${window.location.origin}${window.location.pathname}?q=${encodeURIComponent(Table.searchField.value)}${fs.length ? '&' : ''}${fs}`;
			console.log(url);
			navigator.clipboard.writeText(url)
			return false;
		}

		function makeSpellList(list : Spells.SpellList)
		{
			const name = prompt("Name for spell list?");

			if(!name)
				return;

			window.localStorage.setItem(name, JSON.stringify(list));
			window.location.href = `list.html#${name}`
		}

		document.getElementById("create-list").onclick = _ => makeSpellList({
			filter : Spells.toFilter(Table.searchField.value),
			sources : selectedSources(),
			prepared : []
		});

		async function filesToSpellList(files : FileList)
		{
			function isStringArray(dims : number, thing : any) : boolean
			{
				if(!Array.isArray(thing))
					return false;

				return thing.every(x => (dims > 1) ? isStringArray(dims - 1, x) : typeof(x) === "string");
			}

			if(!files || files.length != 1 || files[0].type != "application/json")
				return;

			const data = JSON.parse(await files[0].text());

			if(!isStringArray(3, data.filter) || !isStringArray(1, data.sources) || !isStringArray(1, data.prepared))
			{
				alert("The given spell list is invalid");
				return;
			}

			makeSpellList({ filter: data.filter as string[][][], sources: data.sources as string[], prepared : data.prepared as string[] });
		}

		{
			const uploadButton = document.getElementById("faux-upload-list") as HTMLButtonElement;
			const uploadInput = document.getElementById("upload-list") as HTMLInputElement;

			uploadButton.onclick = _ => uploadInput.click();
			uploadButton.ondragover = ev => ev.preventDefault();
			uploadButton.ondrop = async ev => {
				ev.preventDefault();

				if(!ev.dataTransfer)
					return;

				await filesToSpellList(ev.dataTransfer.files);
			}
			uploadInput.oninput = async ev => {
				await filesToSpellList(uploadInput.files);
			}
		}
	}

}