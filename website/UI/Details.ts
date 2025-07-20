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
			Util.backToIndex();

		await withGame(async function(g) {
			const book = g.books[from];

			if(! book)
				Util.backToIndex(`No source '${g.shorthand}/${from}' exists!`);

			const sp = (await g.fetchSource(from)).find(s => Util.same(s.name, spell));

			if(!sp)
				Util.backToIndex(`No spell named ${spell} in source ${book}!`)

			document.title = sp.name;
			Util.getElement("spell-name").innerText = sp.name;

			g.details(sp, book, document.getElementById("spell-details") as HTMLDivElement);
		});
	}
}
