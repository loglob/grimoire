
namespace Grimoire;

/// <summary>
///  Signals that a scraping attempt encountered data that does not represent a spell.
///  Silently ignored , as opposed to a FormatException
/// </summary>
internal class NotASpellException : Exception
{ }