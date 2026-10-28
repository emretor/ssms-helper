# SSMS Helper features

What every feature does, how to trigger it, and what to expect. Version 0.13.23, SSMS 22 (SSMS 21: separate
package `SsmsHelper.Ssms21.vsix`, best effort).

- [Completion list](#completion-list)
- [Snippets](#snippets)
- [JOIN helper](#join-helper)
- [INSERT helper](#insert-helper)
- [Expand SELECT *](#expand-select-)
- [EXEC helper](#exec-helper)
- [F12: script the object](#f12-script-the-object)
- [Copy as INSERT](#copy-as-insert)
- [Close quotes](#close-quotes)
- [WHERE safety check](#where-safety-check)
- [Schema loading and Refresh Schema](#schema-loading-and-refresh-schema)
- [Options](#options)
- [Menu commands and shortcuts](#menu-commands-and-shortcuts)
- [Status bar messages](#status-bar-messages)
- [Limitations](#limitations)
- [Troubleshooting](#troubleshooting)

**Key rule:** wherever a helper generates code, **Tab** gives the default (wide, one item per line) form and
**Shift+Enter** the compact form.

All helpers work in SSMS query windows (and `.sql` files). The JOIN and INSERT helpers read the database of the
query window's current connection, so the window must be connected and on the right database (check the
database dropdown on the toolbar).

## Completion list

A fast list of what fits at the caret, filled from SSMS Helper's in-memory schema (no database call while you
type). It opens **right after the space**, without Ctrl+J:

| You type | The list shows |
|---|---|
| `FROM ` / `JOIN ` / `INSERT INTO ` / `UPDATE ` / `DELETE FROM ` | Tables, then views, then table-valued functions, with schema (`dbo.Orders`). No roles, schemas or system objects |
| `TRUNCATE TABLE ` / `DROP TABLE ` / `DROP TABLE IF EXISTS ` / `ALTER TABLE ` / `MERGE ` / `MERGE INTO ` / `USING ` (in a MERGE) | The same list. Not after `CREATE TABLE ` (a new name) |
| `EXEC ` | Stored procedures |
| `UPDATE dbo.Orders SET ` (and after each `, ` in the SET list) | The columns you can update: no identity, computed or rowversion columns |
| `... WHERE ` (and after `AND ` / `OR ` / `(` in the WHERE clause) | The columns of the tables in the statement, with their types |
| `ORDER ` / `GROUP ` / `PARTITION ` | `BY` (Enter or Tab picks it) |
| `... ORDER BY ` / `... GROUP BY ` (and after each `, ` in the list) | The same columns |
| `... HAVING ` (and after `AND ` / `OR ` / `(`) | The same columns |
| `SELECT ` / `SELECT DISTINCT ` / `SELECT TOP (10) ` and after each `, ` in the SELECT list, **when the query already has a FROM** (going back into `SELECT  FROM dbo.Orders o`) | The columns of the FROM tables. Without a FROM yet, inside a function call (`ISNULL(a, `) and for `@variables`, SSMS's own list |
| `ORDER BY o.OrderDate ` (after a column, a number or a function call) | `DESC` / `ASC`. Not selected until you type: **Tab** picks, **Enter** is still a new line |
| `JOIN dbo.Orders o ` / `... ON ` | The [JOIN helper's](#join-helper) conditions first, then the columns of every table so far (the joined table first) |
| `... ON o.CustomerId = ` (and after `AND ` / `OR ` / `( ` in the ON condition) | The columns of the tables so far, the joined table first |
| `o.` (an alias or table name of the statement, anywhere: SELECT list, WHERE, ...) | The columns of that table, right after the dot |
| `SELECT * ` | Keywords: `FROM`, `INTO`, then statement keywords |
| `USE ` | The server's databases you can open: yours by name, then `master`, `model`, `msdb`, `tempdb` ("system"). Offline databases are left out |

- **Typing filters** the list: names that contain what you typed, names that start with it first (shorter names
  first). `gam` finds `dbo.Games`, then `dbo.VideoGames`. With a dot, the whole name is matched (`sales.ord`).
- **The best match is always selected**: Enter or Tab inserts it, no arrow keys needed. Arrows move the selection,
  Esc closes the list. While the list is open, Enter picks; press Enter again for a new line.
- **A pick replaces the whole name at the caret**: with the caret just left of `a` in `WHERE a`, Ctrl+Space and
  `AddressId` gives `WHERE AddressId`, not `WHERE AddressIda`. Table and procedure lists replace the whole
  qualified name: with the caret left of `dbo` in `JOIN dbo.Orders o`, picking `dbo.Customers` gives
  `JOIN dbo.Customers o` (also `[dbo].[Orders]`, `Shop.dbo.Orders`, `Shop..Orders`).
- **Only the text left of the caret filters**: with the caret at the start of a name the list shows everything;
  in `dbo.Or|ders` it filters by `dbo.Or`.
- **The list closes** as soon as nothing matches what you typed.
- **In the SELECT list, WHERE, HAVING, ORDER BY and GROUP BY** columns are inserted with the table's alias (`o.OrderId`). With several tables and no
  alias, the table name is used (`Orders.OrderId`); one table without an alias gives just `OrderId`. After
  `UPDATE ... SET` and after `o.` names go in without a prefix.
- **A space ends a name and opens the next list**: `SELECT * from` + space closes the keyword list and opens the
  table list.
- **Snippets open it too**: `ssf`, `st100`, `stl`, `scf`, `ii`, `upd`, `df`, `ij`, `lj` + Tab leave the caret where a table goes,
  and the list opens there.
- **Picking a table after `INSERT INTO`** or **a procedure after `EXEC`** with Enter or Tab then runs the INSERT /
  EXEC helper as before (Shift+Enter gives the compact form).
- **The SELECT list shows only columns**: typing a function name there (`COUNT`, `GETDATE`) closes the list, and
  SSMS's function list does not open in its place.
- **Everywhere else** (`dbo.` outside FROM, a SELECT list without FROM, `PARTITION BY`, functions, variables, ...) SSMS's own IntelliSense list works as
  before. SSMS's red error underlines and tooltips are not affected.
- The first time on a database the list waits for the schema to load (`loading schema for ...` in the status bar)
  and then opens by itself, as long as you have not typed or moved meanwhile. The status bar then says
  `schema for ... loaded.`
- Turn it off under [Options](#options) (**Completion list**) to get SSMS's list everywhere.

## Snippets

Type a shortcut and press **Tab**.

| Shortcut | Expands to |
|---|---|
| `ssf` | `SELECT * FROM ` |
| `st100` | `SELECT TOP (100) * FROM ` |
| `stl` | `SELECT TOP (100) * FROM  ORDER BY 1 DESC;` on one line, the caret after `FROM` for the table: the latest 100 rows (the first column is usually the primary key) |
| `scf` | `SELECT COUNT(*) FROM ` |
| `ii` | `INSERT INTO ` |
| `upd` | `UPDATE`, an indented empty line for the caret, `SET`, `WHERE` |
| `df` | `DELETE FROM`, an indented empty line for the caret, `WHERE` |
| `cte` | `WITH cte AS ( ... ) SELECT * FROM cte;` with the caret inside the parentheses |
| `bt` | `BEGIN TRANSACTION;`, an empty line for the caret, then commented `COMMIT` and `ROLLBACK` |
| `ij` | `INNER JOIN ` (the table list opens) |
| `lj` | `LEFT JOIN ` (the table list opens) |
| `ob` | `ORDER BY ` |
| `gb` | `GROUP BY ` |
| `ifs` | `IF`, the caret for the condition, then `BEGIN`, an empty line and `END` |
| `cp` | `CREATE PROCEDURE dbo.` with the caret for the name, a commented parameter line, `AS`, `BEGIN`, `SET NOCOUNT ON;`, an empty line, `END;` and `GO` |

```sql
-- df + Tab                -- upd + Tab
DELETE FROM                UPDATE
    |                          |
WHERE                      SET
                           WHERE
```

The caret line takes a table name or a whole query; `WHERE` is always there and left empty on purpose, as a
reminder not to run an UPDATE or DELETE on the whole table. `SET` and `WHERE` have no trailing space: go to the end
of the line and type the space yourself, and the column list opens.

- Shortcuts are case-insensitive (`SSF` works too).
- The caret lands where you continue typing (after `FROM`, inside the CTE, and so on).
- Multi-line snippets keep the indentation of the line you typed them on.
- A shortcut only expands as a whole word at the end of the word: not inside a longer word, not as part of a
  name such as `dbo.ssf`, `@ssf`, `#ssf` or `[ssf]`, and never inside comments or strings.
- With a selection, Tab indents as usual. With several carets, Tab does nothing special.
- User-defined snippets are planned (SSM-16).

### Surround with Transaction

Select statements (or put the caret on a one-line statement) and run **Tools > SSMS Helper > Surround with
Transaction**, or press **Ctrl+K, Ctrl+T**. Rebind it under Tools > Options > Environment > Keyboard, command
`Tools.SSMSHelperSurroundWithTransaction`.

```sql
BEGIN TRANSACTION;

DELETE FROM dbo.Reviews WHERE Rating < 2

ROLLBACK TRANSACTION;
-- COMMIT TRANSACTION;
```

- Running it shows the result (the Messages tab reports the rows affected) and then rolls back. To keep the change, comment out `ROLLBACK` and
  uncomment `COMMIT`. The caret lands on the `ROLLBACK` line.
- Whole lines are wrapped, as selected; the wrapper lines up with the least indented line. One Ctrl+Z undoes it.
- The `bt` snippet is the empty version, for typing the statements afterwards.

## JOIN helper

Suggests `ON` conditions from the database's foreign keys.

### Opening the list

The list opens by itself when you type a space:

- after an alias: `SELECT * FROM dbo.Customers c JOIN dbo.Orders o ` (the chosen condition is inserted with `ON `)
- after `ON`: `SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON `

It does not open after a table without an alias (`JOIN dbo.Orders `), because you are probably about to type one.
Use **Tools > SSMS Helper > Suggest Join** to open it anywhere after `JOIN table` or `ON`.

With the [completion list](#completion-list) on (the default), the conditions are the top of that list, and
the columns of every table in the query so far follow them, the joined table first, then the nearest earlier
ones. One list, so SSMS's own list never opens on top of it:

```
SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON |
    o.CustomerId = c.CustomerId     FK_Orders_Customers
    o.OrderId                       int
    o.CustomerId                    int
    ...
    c.CustomerId                    int
    c.Name                          nvarchar(100)
```

- Typing filters conditions and columns together, and the conditions stay on top: `o.custo` keeps
  `o.CustomerId = c.CustomerId` first, then the column `o.CustomerId`.
- Picking a column writes it with the alias (`o.CustomerId`); after `JOIN dbo.Orders o ` it gets `ON ` in front,
  as a condition does. Then type ` = ` and the columns list opens again for the other side.
- Foreign keys show their name on the right; guesses (no foreign key) say so there instead of being in italics.
- Enter or Tab picks, Esc closes, as everywhere in the completion list.

With the completion list off, the separate JOIN list below opens instead (conditions only).

### Keys while the list is open

| Key | Action |
|---|---|
| Up / Down | Move the selection |
| Page Up / Page Down | Move by 8 rows |
| Enter or Tab | Insert the selected condition |
| Esc | Close the list |
| Typing | Filter the list (for example `Ship` keeps only `ShippingAddressId`) |
| Double-click | Insert that condition |

The list also closes when you click elsewhere, move the caret out of the condition, or no suggestion matches the
typed text any more.

### What it suggests

Each row shows the condition and, dimmed, the foreign key name or why it was guessed.

| Case | Example | Suggestion |
|---|---|---|
| One foreign key | `Customers c JOIN Orders o ON` | `o.CustomerId = c.CustomerId` |
| Two foreign keys to the same table | `Orders o JOIN Addresses a ON` | `a.AddressId = o.BillingAddressId` and `a.AddressId = o.ShippingAddressId` |
| Composite key | `OrderLines l JOIN OrderLineNotes n ON` | `n.OrderId = l.OrderId AND n.LineNumber = l.LineNumber` |
| Self reference | `Employees e JOIN Employees m ON` | `m.ManagerId = e.EmployeeId` and `m.EmployeeId = e.ManagerId` |
| No foreign key | `Products p JOIN Reviews r ON` | *`r.ProductId = p.ProductId`* (in italics: a name-based guess) |

- The table being joined comes first in each condition, and the nearest earlier table is listed first.
- Real foreign keys come before guesses. Guesses match a column to the other table's primary key by name
  (`CustomerId`, or `CategoryId` to `Categories.Id`) and only when the types are compatible.
- Conditions use the alias when there is one, otherwise the table name (or the full name when the same table
  name appears twice). Names that need it are bracketed (`[Group]`).
- It looks only at the current statement and query level: subqueries, CTE bodies and `UNION` branches are kept
  apart. Derived tables and table-valued functions are not resolved. Temp tables are ignored.

## INSERT helper

Builds the column list and a row of typed placeholder values.

### Triggers

| You type | Then press | Result |
|---|---|---|
| `INSERT INTO dbo.Orders` | Tab | Expanded form (below) |
| `INSERT INTO dbo.Orders` | Shift+Enter | Compact form (below) |
| `INSERT INTO dbo.Ord` and pick `Orders` in the IntelliSense list | Enter or Tab | Expanded form |
| same, pick with | Shift+Enter | Compact form |
| `INSERT INTO dbo.Orders (CustomerId, Status)` | Tab or Shift+Enter | Only the `VALUES` for those columns |
| `INSERT INTO dbo.Orders` (no list open) | Enter | Just a new line, nothing generated |

`INTO` is optional, names may be bracketed (`[dbo].[Order Lines]`) and three-part names work. `ii` + Tab gives
`INSERT INTO `, so `ii` Tab, table name, Tab is the quickest path.

### Expanded form (Tab, Enter)

```sql
INSERT INTO dbo.Orders
(
    CustomerId,
    BillingAddressId,
    ShippingAddressId,
    OrderDate,
    Status
)
VALUES
(
    0,         -- CustomerId int
    0,         -- BillingAddressId int
    0,         -- ShippingAddressId int NULL
    GETDATE(), -- OrderDate datetime2 DEFAULT (sysutcdatetime())
    ''         -- Status varchar(20) DEFAULT ('New')
);
```

Each comment gives the column name, type, `NULL` when the column is nullable, and its default.

### Compact form (Shift+Enter)

```sql
INSERT INTO dbo.Orders
    (CustomerId, BillingAddressId, ShippingAddressId, OrderDate, Status)
VALUES
    (0, 0, 0, GETDATE(), '');
```

No comments. Long lists wrap at 120 characters, continuation lines lined up inside the parenthesis. With your own
column list (`INSERT INTO dbo.Orders (CustomerId, Status)`), only `VALUES` and the indented row are added.

### Rules

- **Skipped columns:** identity, computed, rowversion/timestamp and GENERATED ALWAYS (temporal) columns. The
  status bar lists them, for example `skipped OrderId (identity), RowVersion (rowversion)`.
- **Your own column list** is kept as typed, in your order, even with identity columns. Unknown names get `NULL`
  with the comment `not a column of this table`.
- **Only server-generated columns:** you get `DEFAULT VALUES;`.
- **Caret** lands on the first value. The generated text follows the INSERT line's indentation.
- **Placeholders by type:**

| Type | Placeholder |
|---|---|
| int, bigint, smallint, tinyint, bit | `0` |
| decimal, numeric | `0` or `0.00` (as many zeros as the scale) |
| money, smallmoney | `0.00` |
| float, real | `0.0` |
| nvarchar, nchar, ntext, xml | `N''` |
| varchar, char, text | `''` |
| json | `N'{}'` |
| date, datetime, datetime2, smalldatetime | `GETDATE()` |
| datetimeoffset | `SYSDATETIMEOFFSET()` |
| time | `CAST(GETDATE() AS time)` |
| uniqueidentifier | `NEWID()` |
| binary, varbinary, image | `0x` |
| others (sql_variant, geography, hierarchyid, ...) | `NULL` |

Alias types (such as `sysname`) use their base type's placeholder. Nullable columns get a typed placeholder too;
an option to use `NULL` instead is planned with the options page (SSM-12).

### Undo

- Ctrl+Z after an expansion removes the generated text.
- If the expansion came right after picking a table from the IntelliSense list (`Gam` to `Games`), one Ctrl+Z
  also takes back the pick: you are back at `INSERT INTO Gam` and the IntelliSense list opens again so you can
  pick another table. If you edited anything in between, Ctrl+Z is normal.
- Redo (Ctrl+Y) takes two steps in that case.

## Expand SELECT *

Put the caret right after the `*` and press:

- **Tab** (default, wide): one column per line, lined up under the first, and `FROM` on its own line:

```sql
SELECT c.CustomerId,
       c.Name,
       c.Email,
       c.CreatedAt
FROM dbo.Customers c
```

- **Shift+Enter** (compact): `SELECT` alone, the columns on the next line(s), then `FROM` on its own line. Long
  lists wrap at 120 characters, every line indented the same way:

```sql
SELECT
    c.CustomerId, c.Name, c.Email, c.CreatedAt
FROM dbo.Customers c
```

Which columns you get (shown on one line here for brevity):

| You type (caret after `*`) | Result |
|---|---|
| `SELECT * FROM dbo.Customers` | `SELECT CustomerId, Name, Email, CreatedAt FROM dbo.Customers` |
| `SELECT * FROM dbo.Customers c` | `SELECT c.CustomerId, c.Name, c.Email, c.CreatedAt FROM dbo.Customers c` |
| `SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON ...` | the columns of `c`, then of `o`, all qualified |
| `SELECT c.Name, o.* FROM dbo.Customers c JOIN dbo.Orders o ON ...` | only the columns of `o` |

- Works after `SELECT`, `DISTINCT`, `TOP (n)`, `TOP (n) PERCENT`, `WITH TIES` and after a comma in the SELECT list.
  `COUNT(*)` and multiplication (`a * b`) are left alone.
- **Every** column is listed, identity, computed and rowversion columns included (that is what `*` returns).
- Columns are qualified when there is an alias or more than one table; a single table without alias stays
  unqualified. Names that need it are bracketed.
- The FROM clause is read from the same statement and query level: a `*` in a subquery uses the subquery's
  tables, `UNION` branches are separate.
- `DISTINCT` and `TOP (n)` stay on the `SELECT` line. After other columns (`SELECT a, o.*`) Shift+Enter continues
  on the same line and wraps from there.
- `FROM` is moved to its own line only when it was on the same line right after the `*`; if it is already on its
  own line it stays where it is.
- Nothing is expanded when a table cannot be resolved (unknown table, derived table, temp table); the status bar
  says why. With a table variable (`FROM @rows`) Tab just inserts a tab. `alias.*` still works when only *other*
  tables are unknown.
- There must be a FROM clause: `SELECT *` with nothing after it yet stays as it is.
- The caret ends after the last column. Ctrl+Z puts the `*` back.

## EXEC helper

Type `EXEC dbo.GetOrders` (or `EXECUTE`, or `EXEC @rc = dbo.GetOrders`) and press:

- **Tab** (default): one parameter per line, values left empty for you, the type as a comment, `;` on its own line:

```sql
EXEC dbo.GetOrders
    @CustomerId = , -- int
    @From = ,       -- datetime2
    @Status =       -- varchar(20)
;
```

- **Shift+Enter** (compact): on the EXEC line, no comments, wrapped at 120 characters:
  `EXEC dbo.GetOrders @CustomerId = , @From = , @Status = ;`

- **Picking from the IntelliSense list** (`EXEC dbo.GetOr`, pick `GetOrders`) with Enter or Tab writes the Tab
  form; picking with Shift+Enter writes the compact form. One Ctrl+Z takes back both the parameters and the pick.
  A pick only uses an already loaded schema; picking a system procedure such as `sp_who` does nothing extra.
- The caret lands after the first `= `.
- OUTPUT parameters say so in the comment (`-- int OUTPUT`); write `@yourVariable OUTPUT` as the value.
- A procedure without parameters just gets `;`.
- Every parameter is listed. SQL Server does not tell which parameters have a default, so optional ones are listed
  too; delete the ones you do not need.
- Unknown name: the status bar says it is not a stored procedure in this database, and Tab works as usual.
  Dynamic SQL (`EXEC (@sql)`, `EXEC @proc`) and temporary procedures are left alone.

## F12: script the object

Put the caret on a name (anywhere in `dbo.GetOrders`, or right after it) and press **F12**. A new query window opens
on the same database with:

- **Procedure, view, function or trigger:** its `ALTER` statement, with `USE`, the `SET ANSI_NULLS` /
  `SET QUOTED_IDENTIFIER` it was created with, and the source as stored (comments included).
- **Table:** its `CREATE TABLE` statement with defaults, keys, foreign keys, checks and indexes, the same as Object
  Explorer's Script Table as > CREATE To.

Nothing is executed; edit and run the script yourself. Details:

- Names without a schema resolve like the server does (your default schema, then dbo). Three-part names work for
  the window's own database; for another database the status bar asks you to switch first (also when the
  connection has no database set and the name points elsewhere than your default database).
- Encrypted modules, or no `VIEW DEFINITION` permission: the status bar says it cannot be scripted.
- Synonyms, sequences, table types, temp tables and names with a linked server are not scripted.
- Also on the menu as **Tools > SSMS Helper > Script Object** (keyboard name `Tools.SSMSHelperScriptObject`); rebind
  it under Tools > Options > Environment > Keyboard. Turn it off under [Options](#options).
- Cost: one small query per F12, plus SMO for tables (about half a second the first time, then faster). Nothing
  runs while you type.

## Copy as INSERT

Select cells in a query's results grid, right-click and pick one of:

| Menu item | Identity columns |
|---|---|
| **Copy as INSERT** | Kept with their values, inside `SET IDENTITY_INSERT ... ON` / `OFF`, so the copy keeps the same IDs |
| **Copy as INSERT (without identity)** | Left out; the target table generates new IDs |

The clipboard gets `INSERT ... VALUES` with one row per line:

```sql
SET IDENTITY_INSERT dbo.Orders ON;

INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, Status)
VALUES
    (1, 1, '2026-10-18T14:05:09.120', N'New'),
    (2, 3, '2026-10-18T15:00:00', NULL);

SET IDENTITY_INSERT dbo.Orders OFF;
```

- **Which cells**: the selected rows and the selected columns, in the order the grid shows them. Select whole rows
  (click the row numbers) for all columns, or a block of cells for just those columns.
- **Values** are written for their type: `NULL`, numbers as they are, `N'...'` for Unicode text and `'...'` for
  varchar, dates in ISO format (`'2026-10-18T14:05:09.120'`, safe under any language or DATEFORMAT), `0x...` for
  binary.
- **The table name** comes from the query that produced the grid when it is a SELECT from one table (no JOIN, no
  UNION, no derived table), and the table has every selected column. With several result grids, the grids are
  matched to the script's SELECT statements in order. Otherwise the INSERT says `[TargetTable]`: replace it after
  pasting. A `#temp` table or a three-part name (`Archive.dbo.Orders`) is used as written.
- **Never inserted**: computed, rowversion and GENERATED ALWAYS (temporal) columns are left out. The status bar
  lists what was left out.
- More than 1000 rows are split into several INSERT statements (the T-SQL limit per VALUES list); at most 50,000
  rows per copy.
- Long text is copied as the grid holds it: SSMS keeps at most "Maximum Characters Retrieved" per cell (Tools >
  Options > Query Results > SQL Server > Results to Grid, 65,535 by default), so longer values arrive cut off.
- Turn it off under [Options](#options) (**Copy as INSERT**); the menu items then disappear.
- Cost: nothing until you click it. The grid is read through SSMS internals; if an SSMS update changes them,
  the status bar says it cannot read the grid, and nothing else is affected.

## Close quotes

Typing `'` adds the closing quote and puts the cursor between them: `WHERE name LIKE '` gives `''` with the cursor inside.

| You type | Result |
|---|---|
| `'` in code (after a space, `(`, `,`, `=`, `N`, ...) | `''` with the cursor between them |
| `'` right before a closing quote (cursor after `'abc`) | The cursor steps over it, behind `'abc'` |
| Backspace right after the pair | Both quotes go |
| Ctrl+Z right after the pair | Only the added quote goes |

- Only one quote: inside a comment or a string (between `It` and `s` in `'Its'`; type `''` there for an escaped quote),
  right after a letter or digit (`It'`), right before one, before another quote, with a selection or
  several cursors.
- Turn it off under [Options](#options) (**Close quotes**).
- Cost: nothing while you type other keys. On `'` the text before the cursor is scanned once (about a millisecond for
  a very large script).

## WHERE safety check

When you execute (F5, Ctrl+E or the Execute button) a script with an `UPDATE` or `DELETE` that has no `WHERE`,
SSMS Helper asks first:

```
DELETE without WHERE on dbo.Orders

This changes every row. Run anyway?
```

- **OK** runs the script as usual; **Cancel** (the default, so Enter does not run it) stops it and the status bar
  says `execution cancelled.`
- It checks what SSMS runs: the selection, or the whole script when nothing is selected. Up to five statements are
  listed.
- It checks the window that executes, also when you press F5 with the focus in that window's results grid or in a
  tool window such as Object Explorer.
- Still warns for `UPDATE ... FROM ... JOIN` without `WHERE`, and names the table behind an alias
  (`UPDATE o ... FROM dbo.Orders o` says `dbo.Orders`).
- Leaves alone: temp tables (`#work`) and table variables (`@rows`), `TRUNCATE TABLE`, statements inside
  `CREATE`/`ALTER PROCEDURE`, `TRIGGER` or `FUNCTION` (creating them runs nothing), and other uses of the words
  such as `ON DELETE CASCADE`, `GRANT UPDATE`, `MERGE ... THEN DELETE`, `UPDATE STATISTICS`.
- `WHERE CURRENT OF cursor` counts as a `WHERE`. A `WHERE 1 = 1` is not questioned.
- Text in strings and comments is ignored, so dynamic SQL (`EXEC (@sql)`) is not checked.
- Turn it off under [Options](#options): **Ask before UPDATE or DELETE without WHERE**.


- Tables, views, columns, keys, foreign keys and stored procedures (with parameters) of the window's database are loaded in the background the first
  time a helper needs them, and kept for 30 minutes (change it under [Options](#options)). After that they are reloaded in the background while the old
  copy keeps working.
- The first use on a new database may show `loading schema for ...` in the status bar. The JOIN list then opens
  when loading finishes. For INSERT the first Tab waits for the load; the first IntelliSense pick only completes
  the name (press Tab again).
- **Ctrl+Shift+R** (SSMS's Edit > IntelliSense > Refresh Local Cache) refreshes SSMS's IntelliSense and also
  reloads the SSMS Helper schema of the window's database, for example after creating a table or a column. The
  helpers keep using the old schema until the new one has loaded (`schema for <server>/<db> loaded.`). Turn it off
  under [Options](#options).
- **Tools > SSMS Helper > Refresh Schema** reloads only the SSMS Helper schema and reports the counts.
- The database list after `USE ` is loaded per server, with one small query, the first time you type `USE `, and kept
  as long as the schema. Ctrl+Shift+R reloads it too (for example after creating a database).
- **Executing a schema change reloads it**: when a script you execute (F5) creates, alters, drops or renames a
  table, view, procedure, function, synonym, type or schema (`CREATE` / `ALTER` / `DROP`, `CREATE OR ALTER`,
  `SELECT ... INTO dbo.x`, `sp_rename`), the schema of the window's database, and of databases the script `USE`s,
  reloads about 2 seconds later (`schema for <server>/<db> reloaded after the schema change.`). Only databases the
  helpers have already loaded. Temp tables, table variables and `OUTPUT ... INTO` do not count. `CREATE` / `ALTER` /
  `DROP DATABASE` reloads the database list after `USE `. Cost: one pass over the executed text per Execute, no query
  unless the script has DDL. Turn it off under [Options](#options).
- Changes made elsewhere (another person, a deployment) are not detected: press Ctrl+Shift+R. The same for DDL in
  dynamic SQL (`EXEC('CREATE TABLE ...')`, `sp_executesql`) and for a DDL script that runs longer than 2 seconds.
- A column or parameter of a user-defined type you have no permission on shows its base type (for example
  `nvarchar(20)` instead of `dbo.Code`); before 0.13.10 such a column made the whole load fail.
- When loading fails (wrong password, disabled login, server down), the helpers wait 30 seconds before trying that
  database again, so typing does not open a connection per key (or lock out the login). Until then they repeat the
  last error, and a schema loaded earlier keeps working. Ctrl+Shift+R or Refresh Schema tries again at once.
- The extension opens its own connection with the window's credentials. It never runs anything in your query
  window's session.

## Options

**Tools > Options > SSMS Helper > General** (or **Tools > SSMS Helper > Options...**). Changes apply at once, also in open query windows.

| Setting | Default | What it switches |
|---|---|---|
| Completion list | On | Our list after `FROM ` / `JOIN ` / `INTO ` / `UPDATE ` / `EXEC ` / `TRUNCATE TABLE ` / `MERGE `, `SET `, `WHERE ` / `HAVING ` / `ORDER BY ` / `GROUP BY `, the SELECT list (with a FROM), `SELECT * `. Off: SSMS's list everywhere |
| Snippets | On | Tab expands `ssf`, `ii`, `upd`, `df`, `bt`, ... |
| JOIN list opens automatically | On | The join conditions after `JOIN table alias ` and `ON ` (in the completion list, or the JOIN list when that is off). Off: use the Suggest Join command or Ctrl+J |
| INSERT helper | On | Tab / Shift+Enter / IntelliSense pick after `INSERT INTO table` |
| Expand SELECT * | On | Tab / Shift+Enter right after the `*` |
| EXEC helper | On | Tab / Shift+Enter / IntelliSense pick after `EXEC procedure` |
| F12 scripts the object | On | F12 opens the ALTER / CREATE script in a new window |
| Copy as INSERT | On | The two Copy as INSERT items on the results grid's right-click menu |
| Close quotes | On | Typing `'` adds the closing quote |
| Ask before UPDATE or DELETE without WHERE | On | The confirmation on Execute |
| Ctrl+Shift+R also reloads the schema | On | SSMS's Refresh Local Cache reloads our schema too |
| Reload the schema after CREATE / ALTER / DROP | On | Executing DDL reloads the schema a moment later |
| Schema cache minutes | 30 | How long a loaded schema is used before a background refresh (1 to 1440) |

A helper that is off leaves its keys to SSMS: Tab inserts a tab, Shift+Enter starts a new line.

## Menu commands and shortcuts

All commands are in the **Tools > SSMS Helper** submenu.

| Command | Keyboard name | What it does |
|---|---|---|
| Suggest Join | `Tools.SSMSHelperSuggestJoin` | Opens the JOIN list at the caret |
| Script Object | `Tools.SSMSHelperScriptObject` | F12: ALTER / CREATE script of the object under the caret in a new window |
| Surround with Transaction | `Tools.SSMSHelperSurroundWithTransaction` | Ctrl+K, Ctrl+T: wraps the selected lines in BEGIN TRANSACTION ... ROLLBACK |
| Refresh Schema | `Tools.SSMSHelperRefreshSchema` | Reloads the current database's schema |
| Options... | `Tools.SSMSHelperOptions` | Opens Tools > Options > SSMS Helper |
| About | `Tools.SSMSHelperAbout` | Shows the extension version, the SSMS version and the .NET runtime |
| Uninstall... | `Tools.SSMSHelperUninstall` | Asks to confirm, then the extension installer removes SSMS Helper from this SSMS once you close it (other SSMS versions keep theirs) |

On the **results grid's right-click menu**: Copy as INSERT and Copy as INSERT (without identity) (keyboard
names `Tools.SSMSHelperCopyAsInsert`, `Tools.SSMSHelperCopyAsInsertWithoutIdentity`).

Bind a shortcut under **Tools > Options > Environment > Keyboard**: search for `SSMSHelper`.

Useful SSMS shortcuts alongside: **Ctrl+J** lists members (IntelliSense list at the caret), **Ctrl+Space**
completes the word (inserts a single match directly), **Ctrl+Shift+R** refreshes SSMS's IntelliSense cache and the SSMS Helper schema.

## Status bar messages

All messages start with `SSMS Helper:`.

| Message | Meaning |
|---|---|
| `loading schema for <server>/<db> ...` | First use on this database; wait a moment |
| `reloading schema for <server>/<db>...` | After Ctrl+Shift+R; the old schema keeps working meanwhile |
| `loading the database list of <server>...` / `... loaded.` | First `USE ` on this server; the list opens when it is loaded |
| `schema for <server>/<db> loaded.` | Loading finished |
| `schema for <server>/<db> reloaded after the schema change.` | You executed CREATE / ALTER / DROP; the helpers now know the change |
| `loading schema for ... failed: ...` | The extension could not read the catalog (permissions, connection). Tried again after 30 seconds, or at once with Ctrl+Shift+R |
| `<tables> not found in <db>. Is the query window on the right database?` | JOIN: the tables are not in this database (shown once, not on every key) |
| `<table> not found in <db>.` | INSERT: unknown table (also shown while IntelliSense is still completing a name) |
| `INSERT for <table>; skipped ...` | INSERT done; lists skipped server-generated columns |
| `expanded * to <n> column(s).` | SELECT * expansion done |
| `cannot expand *: ...` | A table behind the `*` is unknown, derived or temporary |
| `<x> is not a table or alias in this query.` | `x.*` does not match any table in the FROM clause |
| `<n> parameter(s) of <procedure>.` | EXEC helper done |
| `<name> is not a stored procedure in <db>.` | EXEC helper: unknown procedure |
| `scripting <name>...` | F12 is looking up the object |
| `ALTER script for <name>.` / `CREATE script for <name>.` | F12 opened the script |
| `<name> is not a table, view, procedure, function or trigger in this database.` | F12: nothing with that name |
| `<name> is encrypted or you lack VIEW DEFINITION; ...` | F12: no source available |
| `<name> is in another database; switch the window to <db> first.` | F12 on `OtherDb.dbo.x` |
| `<name> was not found by SMO; ...` | F12 on a table that was just dropped or renamed |
| `wrapped in a transaction that rolls back; ...` | Surround with Transaction done |
| `copied <n> row(s) as INSERT into <table>.` | Copy as INSERT done; also lists left-out columns |
| `... Table unknown: replace [TargetTable] after pasting.` | Copy as INSERT could not tell the source table |
| `click into a results grid first.` | Copy as INSERT used without a focused results grid |
| `the clipboard is in use by another program; ...` | Copy as INSERT: another program (clipboard manager, Remote Desktop) kept the clipboard busy; try again |
| `cannot read ... from this SSMS version. ...` | Copy as INSERT: SSMS changed its grid; please report it |
| `execution cancelled.` | You chose Cancel in the WHERE safety check |
| `no foreign keys or matching key columns found for <table>.` | Suggest Join command found nothing to offer |
| `the query window is not connected to a database.` | Suggest Join command in a disconnected window |
| `place the caret after "JOIN table alias" or after "ON" ...` | Suggest Join command used elsewhere |

## Limitations

- Microsoft does not support third-party SSMS extensions; an SSMS update can break this one.
- With the completion list off, SSMS's IntelliSense list can appear on top of the JOIN list while you type to
  filter. With it on (the default), the conditions are in our list and this does not happen.
- A name typed over several lines (for example `JOIN` on one line and the alias on the next) does not open the
  JOIN list automatically; use the Suggest Join command.
- Cross-database names (`OtherDb.dbo.Table`) are looked up in the current database only.

## Troubleshooting

- Nothing happens: check the helper is on under [Options](#options), the window is connected and the database dropdown shows the right database; run
  Refresh Schema.
- To see what the extension logs, start SSMS with `Ssms.exe /log`, reproduce the problem, close SSMS and look for
  `SsmsHelper` in `%AppData%\Microsoft\SSMS\22.0_<id>\ActivityLog.xml`.
- `samples/ShopDemo.sql` creates the `SsmsHelperDemo` database with every case above, for trying things out.
