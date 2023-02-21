/**
 * @returns An element that displays a loading animations.
 */
function loading() : HTMLElement
{
	let l = document.createElement("embed");
	l.height = "5";
	l.src = "loading.svg"

	return l;
}

/**
 * @returns A <b> element displaying the given text
 */
function bold(txt : string) : HTMLElement
{
	const b = document.createElement("b");
	b.innerText = txt;
	return b;
}

/**
 * @returns The index showing all known sources
 */
async function getSources() : Promise<{ [id: string] : string }>
{
	const r = await fetch("db/index.json");
	return await r.json();
}

/** Hides or un-hides an element
 * @param hide Whether to hide or un-hide
 */
function setHidden(element : HTMLElement, hide : boolean)
{
	element.style.display = hide ? "none" : "initial";
}

/** Maps book IDs onto canonical titles */
let sources : { [id: string] : string } = {}

/** Looks up existing sources and inserts them into the source selector
 * @param preload A list of book IDs to import immediately
*/
async function loadSources(preload : string[])
{
	let elem = document.getElementById("source-selector");
	sources = await getSources();
	document.getElementById("source-selector-placeholder")?.remove();

	for (const id in sources)
	{
		let container = document.createElement("div");
		container.innerText = sources[id];

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
function initUI()
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

/** Loads the spell list from the URL fragment. Also sets the list-name HTML element. */
function loadSpellList() : Spells.SpellList & { name : string }
{
	if(!window.location.hash)
		window.location.href = "index.html";

	const name = window.location.hash.substring(1);
	const listJson = window.localStorage.getItem(name)

	if(listJson === null)
	{
		// maybe handle via a custom html page instead, to serve an actual error code
		alert("That spell list doesn't exist! Did you clear browser data?");
		window.location.href = "index.html";
	}

	const list = JSON.parse(listJson) as Spells.SpellList;

	return {
		sources: list.sources,
		prepared : list.prepared,
		filter : list.filter,
		name : name
	}
}

/** Saves a spell list with a modified prepared set.
 * Updated the current
 * @param list The current spell list
 * @param prepared The new prepared set
 */
function storeWith(list : Spells.SpellList & { name : string }, prepared : Iterable<string>|ArrayLike<string>)
{
	const newList : Spells.SpellList = {
		filter : list.filter,
		sources : list.sources,
		prepared : Array.from(prepared)
	}

	window.localStorage.setItem(list.name, JSON.stringify(newList))
}

/** Initialized the spell list UI. Must be called from lists.html on page load. */
async function initListUI()
{
	const list = loadSpellList();
	var preparedSet = new Set(list.prepared);

	document.title += `: ${list.name}`;

	{
		const nameField = document.getElementById("list-name") as HTMLInputElement;
		nameField.value = list.name;
		nameField.readOnly = true; // TODO editable list name
	}

	Spells.isPrepared = x => preparedSet.has(x.name);

	// TODO select all
	Table.customRowElements = s => {
		const inp = document.createElement("input")
		inp.type = "checkbox"
		inp.checked = preparedSet.has(s.name)
		inp.onclick = _ => {
			if(preparedSet.has(s.name))
			{
				preparedSet.delete(s.name);
				inp.checked = false;
			}
			else
			{
				preparedSet.add(s.name);
				inp.checked = true;
			}

			console.log(preparedSet);
			// eager writeback for now
			storeWith(list, preparedSet)
		}

		const td = document.createElement("td");
		td.appendChild(inp);

		return [td];
	};

	const spells = (await Promise.all(list.sources.map(Spells.getFrom))).flat();
	const inclSpell = function(s : Spell) { return Spells.match(list.filter, s) || preparedSet.has(s.name) }

	Table.insert(spells.filter(inclSpell));
	Table.init();

	{
		const globalSearch = document.getElementById("global-search") as HTMLInputElement;

		// this would be nicer if integrated into the table logic as a sort of multi-level filter
		globalSearch.onchange = _ => {
			if(globalSearch.checked)
				Table.insert(spells.filter(s => !inclSpell(s)));
			else
				Table.filter(s => !inclSpell(s));
		}
	}

	{
		const downloadList = document.getElementById("download-list") as HTMLButtonElement;

		downloadList.onclick = _ =>
			window.open(`data:application/json,${encodeURIComponent(window.localStorage.getItem(list.name))}`);
	}

	{
		const cardView = document.getElementById("spell-card-view") as HTMLButtonElement;

		cardView.onclick = _ =>
			window.location.href = `cards.html#${list.name}`;
	}
}