# Grimoire
A dnd-spells.com replacer that supports custom sources such as homebrew.
Contains two components, a **scraper** that compiles a spell database and a **website** to display that database.

# Scraper
Compiles spells from various sources.

The supported sources are:
- The [dnd-wiki](http://http://dnd5e.wikidot.com/)
- A local latex project
- An overleaf instance

## Usage
Running `dotnet run [<books.json>] [sources...]` from the scraper directory processes all listed sources and outputs a spell database in the `./dbs/` directory.

When a `.json` argument is listed, a path to a json file containing a configuration as described below is expected.
If omitted, the listed filename is searched in the working directory.

Possible sources are:
### `latex [<latex.json>] [[book id] [input.tex ...] ...]`
Processes local LaTeX files using the given configuration.

The given book ID sets which source spells found in the following files are listed under, and must be a shorthand from the books.json file, or the special `macros` ID.
If the book ID is `macros`, those files are searched for macro definitions and no spells are extracted.
Macros from other files are not parsed.

If a file contains a line starting with `%% grimoire begin`, only contents after that line until end of file or a closing `"%% grimoire end` is parsed.
The `%% grimoire begin` may also be followed by a book ID which overwrites the current book ID for that segment.
A file may contain multiple such segments.

Otherwise, the file contents between `\begin{document}` and `\end{document}` are searched for spells.

## `overleaf [<overleaf.json>]`
Processes files from an overleaf server.
Within the project, every file marked with `%% grimoire include`, followed by a book ID, within the first 10 lines, is included and parsed as above.

## `dnd-wiki`
Processes all spells found on the [dnd-wiki](http://http://dnd5e.wikidot.com/).

## Configuration
Configuration is given by multiple json files in these formats:

### books.json
Gives the recognized source books. Consists of a list of objects with these fields:
- **fullName**: The full name of the book to display to users
- **shorthand**: The *unique* ID used internally to identify the book.
- **alts**: Optional list of other names for the book. Used when scraping from the dnd wiki

### latex.json
An objects with the fields
- **spellAnchor** a string that always precedes a spell definition. Matched before macro expansion.
- **upcastAnchor** a string that is always between a spell description and upcast description.
- **environments** a string -> string dictionary that maps LaTeX environments onto HTML tags, `itemize` or `tabular`.

### overleaf.json
An object with the fields
- **projectID** the overleaf project ID to scrape
- **password** the overleaf web API password (see [olspy documentation](https://github.com/loglob/olspy) for more details)
- **host** optional hostname for the overleaf instance.
	If omitted, attempt to connect to a local docker instance.
- **latex** a `latex.json` as described above.

# Website
To compile the frontend, run `tsc` in the `website` directory.
Then copy the `dbs` directory into `website/www/`.
Then, start any webserver on the `website/www/` directory.
