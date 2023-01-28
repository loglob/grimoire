function loading()
{
	let l = document.createElement("embed");
	l.height = "5";
	l.src = "loading.svg"

	return l;
}

/**
 * @returns The index showing all known sources
 */
async function getSources() : Promise<{ [id: string] : string }>
{
	const r = await fetch("db/index.json");
	return await r.json();
}

function setHidden(element : HTMLElement, hide : boolean)
{
	element.style.display = hide ? "none" : "initial";
}

/** Switches location pathname, and also clears hash and parameters
 * @param newPath The new path. Not escaped in any way.
*/
function switchPath(newPath : string)
{
	window.location.href = `${window.location.protocol}//${window.location.host}${newPath[0] == '/' ? "" : "/"}${newPath}`;
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

function selectedSources() : string[]
{
	return Object.keys(sources)
		.filter(id => (document.getElementById(`source_${id}`) as HTMLInputElement).checked);
}

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

	document.getElementById("create-list").onclick = _ => {
		const sl : Spells.SpellList = {
			filter : Spells.toFilter(Table.searchField.value),
			sources : selectedSources(),
			prepared : []
		}
		const name = prompt("Name for spell list?");

		if(!name)
			return;

		window.localStorage.setItem(name, JSON.stringify(sl));
		switchPath(`list.html#${name}`)
	}
}

/** Loads the spell list from the URL fragment. Also sets the list-name HTML element. */
function loadSpellList() : Spells.SpellList & { name : string }
{
	if(!window.location.hash)
		window.location.pathname = "/";

	const name = window.location.hash.substring(1);
	const listJson = window.localStorage.getItem(name)

	if(listJson === null)
	{
		// maybe handle via a custom html page instead, to serve an actual error code
		alert("That spell list doesn't exist! Did you clear browser data?");
		switchPath("/")
	}

	const list = JSON.parse(listJson) as Spells.SpellList;

	return {
		sources: list.sources,
		prepared : list.prepared,
		filter : list.filter,
		name : name
	}
}

function storeWith(list : Spells.SpellList & { name : string }, prepared : Iterable<string>|ArrayLike<string>)
{
	const newList : Spells.SpellList = {
		filter : list.filter,
		sources : list.sources,
		prepared : Array.from(prepared)
	}

	window.localStorage.setItem(list.name, JSON.stringify(newList))
}

async function initListUI()
{
	const list = loadSpellList();
	var preparedSet = new Set(list.prepared);
	console.log(list)

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

		downloadList.onclick = _ => {
			var x : Blob = new Blob( [window.localStorage.getItem(list.name)], { type: "application/json" } );
			var url = URL.createObjectURL(x);
			window.open(url);
			window.setTimeout(() => URL.revokeObjectURL(url), 10000);
		}
	}
}