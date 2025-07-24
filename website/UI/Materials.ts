
/** Handles the index.html UI */
namespace UI
{
	import child = Util.child;
	import IGame = Games.IGame;
	import IMaterialContext = Games.IMaterialContext;

	/** A fractional price that may contain unknown variables */
	class Price
	{
		/** The definitely known portion of the price */
		public readonly value : number
		/** The components we don't have prices for */
		public readonly unsureAbout : readonly string[]

		/** Whether this price has an unknown factor */
		public get unsure() : boolean
		{
			return this.unsureAbout.length > 0
		}

		private constructor(value : number, unsureAbout : readonly string[])
		{
			this.value = value;
			this.unsureAbout = unsureAbout
		}

		public static Certain(value : number) : Price
		{
			return new Price(value, [])
		}

		public static Unsure(...about : readonly string[])
		{
			return new Price(0.0, about)
		}

		public static readonly ZERO : Price = new Price(0, [])

		public plus(other : Price) : Price
		{
			return new Price( this.value + other.value, this.unsureAbout.concat(other.unsureAbout) );
		}

		public scale(by : number) : Price
		{
			if(by == 0)
				return Price.ZERO;

			return new Price( this.value * by, this.unsureAbout );
		}

		public static sum(...xs : Price[])
		{
			var acc = this.ZERO

			for(const x of xs)
				acc = acc.plus(x);

			return acc;
		}

		public static of(m : Data.IMaterial)
		{
			return m.price === null ? this.Unsure(m.display) : this.Certain(m.price);
		}
	}

	/** State container for observing casting count changes, representing a single spell */
	class SumGadget<TSpell extends Data.ISpell, TMaterial extends Data.IMaterial>
	{
		readonly parent : Materials<TSpell, TMaterial>;
		/** Total price of all consumed components */
		public readonly consumed : Price;
		/** Total price of all persistent components */
		public readonly persistent : Price;
		/** The number input that gives the number of casts to calculate */
		readonly input : HTMLInputElement;
		/** Holds last observed `input` value */
		castCount : number = 0;

		constructor(parent : Materials<TSpell, TMaterial>, persistent : Price, consumed : Price, input : HTMLInputElement)
		{
			this.parent = parent;
			this.persistent = persistent;
			this.consumed = consumed;
			this.input = input;

			input.onchange = _ => this.listener();
		}

		/** Called when the value of `input` changes by user action. Triggers re-computation of the total prices. */
		private listener()
		{
			const newValue = parseInt(this.input.value)

			if(newValue == this.castCount || isNaN(newValue) || newValue < 0)
				return

			this.castCount = newValue
			this.parent.recomputeTotals()
		}
	}

	/** Container for the Materials UI */
	class Materials<TSpell extends Data.ISpell, TMaterial extends Data.IMaterial>
	{
		/** Provides game-specific material information */
		readonly context : IMaterialContext<TSpell, TMaterial>
		readonly game : IGame<TSpell>
		/** Gadgets for every displayed spell */
		readonly gadgets : SumGadget<TSpell, TMaterial>[] = []

		constructor(game : IGame<TSpell>, context : IMaterialContext<TSpell, TMaterial>)
		{
			this.game = game;
			this.context = context;

			Util.getElement("incr-all").onclick = _ => this.stepAll(true);
			Util.getElement("decr-all").onclick = _ => this.stepAll(false);
		}

		/** In-/decrements every single gadget counter by one */
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

		/** Displays an uncertain price (does not list the unknown variables) */
		formatPriceSum(p : Price) : (string|Node)[]
		{
			if(p.unsureAbout.length && p.value == 0)
				return ["???"]

			const price = this.context.formatPrice(p.value)
			return p.unsureAbout.length ? [price, "+?"] : [price]
		}

		/** Formats the main spell table */
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

				child(row, "td").appendChild(this.context.formatMaterials(persistent, true))

				const pSum = Price.sum(...persistent.map(m => Price.of(m)))
				child(row, "td").append(...this.formatPriceSum(pSum))

				child(row, "td").appendChild(this.context.formatMaterials(consumed, true))

				const cSum = Price.sum(...consumed.map(m => Price.of(m)))
				child(row, "td").append(... this.formatPriceSum(cSum))

				const inp = child(child(row, "td"), "input") as HTMLInputElement
				inp.type = "number"
				inp.max = "9999"
				inp.min = "0"

				this.gadgets.push(new SumGadget(this, pSum, cSum, inp))
			}
		}

		/** (WIP) Formats the unknown materials */
		formatUnsure(sum : Price) : (string | Node)[]
		{
			// TODO: turn this into a table & maybe parse out the actual material
			if(! sum.unsure)
				return [];

			var result = [];

			result.push(
				document.createElement("br"),
				`Materials without listed prices: `
			);

			var first = true;

			for (const m of sum.unsureAbout)
			{
				if(! first)
				{
					const sep = document.createElement("span");
					sep.className = "material-sep";
					sep.innerText = ",";
					result.push(sep);
				}

				first = false;
				const txt = document.createElement("span");
				txt.innerHTML = m;
				result.push(txt);
			}

			return result;
		}

		/** regenerates the totals displayed under the table */
		recomputeTotals()
		{
			var totalCasts = 0
			var totalSpells = 0
			var persistentSum = Price.ZERO
			var consumedSum = Price.ZERO

			for(const g of this.gadgets)
			{
				totalCasts += g.castCount

				if(g.castCount > 0)
				{
					++totalSpells;
					persistentSum = persistentSum.plus(g.persistent);
					consumedSum = consumedSum.plus(g.consumed.scale(g.castCount));
				}
			}

			const consumedContainer = Util.getElement("consumed-totals")
			const persistentContainer = Util.getElement("persistent-totals")
			consumedContainer.innerHTML = "";
			persistentContainer.innerHTML = "";

			consumedContainer.append(
				`Material cost for all ${totalCasts} casting${totalCasts != 1 ? 's' : ''} is `,
				Util.wrap("b", ...this.formatPriceSum(consumedSum)),
				// ... this.formatUnsure(consumedSum)
			);

			persistentContainer.append(
				`Casting all ${totalSpells} selected spell${totalSpells != 1 ? 's' : ''} requires an additional `,
				Util.wrap("b", ...this.formatPriceSum(persistentSum)),
				` in persistent components`,
				// ... this.formatUnsure(persistentSum)
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