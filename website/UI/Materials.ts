
/** Handles the index.html UI */
namespace UI
{
	import child = Util.child;
	import IGame = Games.IGame;
	import IMaterialContext = Games.IMaterialContext;

	type PriceSum = Readonly<{ value: number, unsure: boolean }>

	const ZERO : PriceSum = { value: 0, unsure: false }

	function sumPrices(...xs : (number | null)[]) : PriceSum
	{
		var sum = 0;
		var unsure = false;

		for (const x of xs)
		{
			if(x === null)
				unsure = true;
			else
				sum += x;
		}

		return { value: sum, unsure: unsure };
	}

	function scale(p : PriceSum, x : number) : PriceSum
	{
		return { value: p.value * x, unsure: p.unsure }
	}

	function sum(a : PriceSum, ...bs : PriceSum[])
	{
		for (const b of bs)
			a = { value: a.value + b.value, unsure: a.unsure || b.unsure }

		return a
	}

	/** State container for observing  */
	class SumGadget<TSpell extends Data.ISpell, TMaterial extends Data.IMaterial>
	{
		readonly parent : Materials<TSpell, TMaterial>;
		public readonly consumed : PriceSum;
		public readonly persistent : PriceSum;
		readonly input : HTMLInputElement;
		castCount : number = 0;

		constructor(parent : Materials<TSpell, TMaterial>, persistent : PriceSum, consumed : PriceSum, input : HTMLInputElement)
		{
			this.parent = parent;
			this.persistent = persistent;
			this.consumed = consumed;
			this.input = input;

			input.onchange = _ => this.listener();
		}

		listener()
		{
			const newValue = parseInt(this.input.value)

			if(newValue == this.castCount || isNaN(newValue) || newValue < 0)
				return

			this.castCount = newValue
			this.parent.recomputeTotals()
		}
	}

	class Materials<TSpell extends Data.ISpell, TMaterial extends Data.IMaterial>
	{
		readonly context : IMaterialContext<TSpell, TMaterial>
		readonly game : IGame<TSpell>
		readonly gadgets : SumGadget<TSpell, TMaterial>[] = []

		constructor(game : IGame<TSpell>, context : IMaterialContext<TSpell, TMaterial>)
		{
			this.game = game;
			this.context = context;

			Util.getElement("incr-all").onclick = _ => this.stepAll(true);
			Util.getElement("decr-all").onclick = _ => this.stepAll(false);
		}

		stepAll(incr : boolean)
		{
			for(const g of this.gadgets)
			{
				if(incr)
					++g.castCount
				else if(g.castCount > 0)
					--g.castCount

				g.input.value = g.castCount.toString()
			}

			this.recomputeTotals()
		}

		formatPriceSum(p : PriceSum) : (string|Node)[]
		{
			if(p.unsure && p.value == 0)
				return ["???"]

			const price = this.context.formatPrice(p.value)
			return p.unsure ? [price, "+?"] : [price]
		}

		formatSpells(table : HTMLTableElement, spells : TSpell[])
		{
			for(const s of spells)
			{
				const row = child(table, "tr") as HTMLTableRowElement
				const spellRef = child(child(row, "td"), "a") as HTMLLinkElement
				spellRef.href = this.game.spellURL(s)
				spellRef.innerText = s.name

				const all = this.context.getMaterials(s)
				const persistent = all.filter(m => !m.consumed)
				const consumed = all.filter(m => m.consumed)

				child(row, "td").appendChild(this.context.formatMaterials(persistent, true, true))

				const pSum = sumPrices(...persistent.map(m => m.price))
				child(row, "td").append(...this.formatPriceSum(pSum))

				child(row, "td").appendChild(this.context.formatMaterials(consumed, true, true))

				const cSum = sumPrices(...consumed.map(m => m.price))
				child(row, "td").append(... this.formatPriceSum(cSum))

				const inp = child(child(row, "td"), "input") as HTMLInputElement
				inp.type = "number"
				inp.max = "9999"
				inp.min = "0"

				this.gadgets.push(new SumGadget(this, pSum, cSum, inp))
			}
		}

		/** regenerates the totals displayed under the table */
		recomputeTotals()
		{
			var totalCasts = 0
			var totalSpells = 0
			var persistentSum = ZERO
			var consumedSum = ZERO

			for(const g of this.gadgets)
			{
				totalCasts += g.castCount

				if(g.castCount > 0)
				{
					++totalSpells;
					persistentSum = sum(persistentSum, g.persistent);
					consumedSum = sum(consumedSum, scale(g.consumed, g.castCount))
				}
			}

			const consumedContainer = Util.getElement("consumed-totals")
			const persistentContainer = Util.getElement("persistent-totals")
			consumedContainer.innerHTML = "";
			persistentContainer.innerHTML = "";

			consumedContainer.append(
				`Material cost for all ${totalCasts} casting${totalCasts != 1 ? 's' : ''} is `,
				Util.wrap("b", ...this.formatPriceSum(consumedSum))
			);

			persistentContainer.append(
				`Casting all ${totalSpells} selected spell${totalSpells != 1 ? 's' : ''} requires an additional `,
				Util.wrap("b", ...this.formatPriceSum(persistentSum)),
				` in persistent components`
			)
		}
	}

	/** Initialized the spell list UI. Must be called from lists.html on page load. */
	export async function initMaterials()
	{
		withSelectedSpells(async function(game, spells) {
			game.withMaterials(ctx => {

				const table = document.getElementById("materials") as HTMLTableElement

				const mat = new Materials(game, ctx)
				mat.formatSpells(table, spells)
				mat.recomputeTotals()

				return true
			}) ?? Util.backToIndex("No material data is configured for this instance")
		})
	}
}