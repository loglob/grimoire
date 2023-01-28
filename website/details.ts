function ordinal(i : number) : string
{
    switch(i <= 20 ? i : i % 10)
    {
        case 1: return `${i}st`;
        case 2: return `${i}nd`;
        case 3: return `${i}rd`;
        default: return `${i}th`;
    }
}

async function spellDetails(from : string, spell : string)
{
    let sp = (await Spells.getFrom(from)).find(s => s.name.toLowerCase() === spell.toLowerCase());

    document.getElementById("spell-name").innerText = sp.name;
    document.getElementById("level+class").innerText = sp.level
        ? `${ordinal(sp.level)}-level ${sp.school}`
        : `${sp.school} Cantrip`;
    document.getElementById("casting-time").innerText = sp.reaction ? `${sp.castingTime}, ${sp.reaction}` : sp.castingTime;
    document.getElementById("range").innerText = sp.range;
    document.getElementById("components").innerText = sp.materials ? `${sp.components} (${sp.materials})` : sp.components;
    document.getElementById("duration").innerText = (sp.concentration ? "Concentration, up to " : "") + sp.duration;
    document.getElementById("classes").innerText = sp.classes.join(", ");

    document.getElementById("description").innerHTML = sp.description + (sp.statBlock ? sp.statBlock : "");
    if(sp.upcast)
        document.getElementById("upcast").innerHTML = "<strong>At higher levels: </strong>" + sp.upcast;
}