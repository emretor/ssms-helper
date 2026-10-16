# SSMS Helper features

What every feature does, how to trigger it, and what to expect. Version 0.7.2, SSMS 22.

- [Snippets](#snippets)
- [JOIN helper](#join-helper)
- [INSERT helper](#insert-helper)
- [Expand SELECT *](#expand-select-)
- [Schema loading and Refresh Schema](#schema-loading-and-refresh-schema)
- [Menu commands and shortcuts](#menu-commands-and-shortcuts)
- [Status bar messages](#status-bar-messages)
- [Limitations](#limitations)
- [Troubleshooting](#troubleshooting)

**Key rule:** wherever a helper generates code, **Tab** gives the default (wide, one item per line) form and
**Shift+Enter** the compact form.

All helpers work in SSMS query windows (and `.sql` files). The JOIN and INSERT helpers read the database of the
query window's current connection, so the window must be connected and on the right database (check the
database dropdown on the toolbar).

## Snippets

Type a shortcut and press **Tab**.

| Shortcut | Expands to |
|---|---|
| `ssf` | `SELECT * FROM ` |
| `st100` | `SELECT TOP (100) * FROM ` |
| `scf` | `SELECT COUNT(*) FROM ` |
| `ii` | `INSERT INTO ` |
| `upd` | `UPDATE`, an indented empty line for the caret, `SET `, `WHERE ` |
| `df` | `DELETE FROM`, an indented empty line for the caret, `WHERE ` |
| `cte` | `WITH cte AS ( ... ) SELECT * FROM cte;` with the caret inside the parentheses |
| `bt` | `BEGIN TRANSACTION;`, an empty line for the caret, then commented `COMMIT` and `ROLLBACK` |

```sql
-- df + Tab                -- upd + Tab
DELETE FROM                UPDATE
    |                          |
WHERE                      SET
                           WHERE
```

The caret line takes a table name or a whole query; `WHERE` is always there and left empty on purpose, as a
reminder not to run an UPDATE or DELETE on the whole table.

- Shortcuts are case-insensitive (`SSF` works too).
- The caret lands where you continue typing (after `FROM`, inside the CTE, and so on).
- Multi-line snippets keep the indentation of the line you typed them on.
- A shortcut only expands as a whole word at the end of the word: not inside a longer word, not as part of a
  name such as `dbo.ssf`, `@ssf`, `#ssf` or `[ssf]`, and never inside comments or strings.
- With a selection, Tab indents as usual. With several carets, Tab does nothing special.
- User-defined snippets are planned (SSM-16).

## JOIN helper

Suggests `ON` conditions from the database's foreign keys.

### Opening the list

The list opens by itself when you type a space:

- after an alias: `SELECT * FROM dbo.Customers c JOIN dbo.Orders o ` (the chosen condition is inserted with `ON `)
- after `ON`: `SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON `

It does not open after a table without an alias (`JOIN dbo.Orders `), because you are probably about to type one.
Use **Tools > SSMS Helper: Suggest Join** to open it anywhere after `JOIN table` or `ON`.

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

## Schema loading and Refresh Schema

- Tables, views, columns, keys and foreign keys of the window's database are loaded in the background the first
  time a helper needs them, and kept for 30 minutes. After that they are reloaded in the background while the old
  copy keeps working.
- The first use on a new database may show `loading schema for ...` in the status bar. The JOIN list then opens
  when loading finishes. For INSERT the first Tab waits for the load; the first IntelliSense pick only completes
  the name (press Tab again).
- **Tools > SSMS Helper: Refresh Schema** reloads the current database now, for example after creating a table.
  SSMS's own IntelliSense has a separate cache: refresh it with Ctrl+Shift+R.
- The extension opens its own connection with the window's credentials. It never runs anything in your query
  window's session.

## Menu commands and shortcuts

| Command (Tools menu) | What it does |
|---|---|
| SSMS Helper: Suggest Join | Opens the JOIN list at the caret (keyboard name `Tools.SSMSHelperSuggestJoin`) |
| SSMS Helper: Refresh Schema | Reloads the current database's schema |
| SSMS Helper: About | Shows the extension version, the SSMS version and the .NET runtime |

Bind a shortcut under **Tools > Options > Environment > Keyboard**: search for `SSMSHelper`.

Useful SSMS shortcuts alongside: **Ctrl+J** lists members (IntelliSense list at the caret), **Ctrl+Space**
completes the word (inserts a single match directly), **Ctrl+Shift+R** refreshes SSMS's IntelliSense cache.

## Status bar messages

All messages start with `SSMS Helper:`.

| Message | Meaning |
|---|---|
| `loading schema for <server>/<db> ...` | First use on this database; wait a moment |
| `schema for <server>/<db> loaded.` | Loading finished |
| `loading schema for ... failed: ...` | The extension could not read the catalog (permissions, connection) |
| `<tables> not found in <db>. Is the query window on the right database?` | JOIN: the tables are not in this database |
| `<table> not found in <db>.` | INSERT: unknown table (also shown while IntelliSense is still completing a name) |
| `INSERT for <table>; skipped ...` | INSERT done; lists skipped server-generated columns |
| `expanded * to <n> column(s).` | SELECT * expansion done |
| `cannot expand *: ...` | A table behind the `*` is unknown, derived or temporary |
| `<x> is not a table or alias in this query.` | `x.*` does not match any table in the FROM clause |
| `no foreign keys or matching key columns found for <table>.` | Suggest Join command found nothing to offer |
| `the query window is not connected to a database.` | Suggest Join command in a disconnected window |
| `place the caret after "JOIN table alias" or after "ON" ...` | Suggest Join command used elsewhere |

## Limitations

- Microsoft does not support third-party SSMS extensions; an SSMS update can break this one.
- SSMS's IntelliSense list can appear on top of the JOIN list while you type to filter (SSM-40).
- A name typed over several lines (for example `JOIN` on one line and the alias on the next) does not open the
  JOIN list automatically; use the Suggest Join command.
- Cross-database names (`OtherDb.dbo.Table`) are looked up in the current database only.

## Troubleshooting

- Nothing happens: check the window is connected and the database dropdown shows the right database; run
  Refresh Schema.
- To see what the extension logs, start SSMS with `Ssms.exe /log`, reproduce the problem, close SSMS and look for
  `SsmsHelper` in `%AppData%\Microsoft\SSMS\22.0_<id>\ActivityLog.xml`.
- `samples/ShopDemo.sql` creates the `SsmsHelperDemo` database with every case above, for trying things out.
