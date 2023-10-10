/** Handles the details.html UI */
namespace UI
{
	/** Initializes the details UI. Must be called from details.html */
	export async function initDetails()
	{
		const p = new URLSearchParams(window.location.search);
		const from = p.get("from");
		const spell = p.get("spell");

		if(!from || !spell)
		{
			window.location.href = "index.html";
			return;
		}

		await withGame(async function(g) {
			const book = (await g.getBooks())[from]
			
			if(! book)
			{
				alert(`No source '${g.shorthand}/${from}' exists!`);
				window.location.href = "index.html";
				return;
			}
			
			const sp = (await g.fetchSource(from)).find(s => s.name.toLowerCase() === spell.toLowerCase());

			if(!sp)
			{
				alert(`No spell named ${spell} in source ${book}!`);
				window.location.href = "index.html";
				return;
			}

			document.title = sp.name;
			document.getElementById("spell-name").innerText = sp.name;

			g.details(sp, book, document.getElementById("spell-details") as HTMLDivElement);
		});
	}
}
