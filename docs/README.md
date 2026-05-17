# Test data scripts

Two SQL scripts for exercising the SQLTableExporter against a representative type catalog. Both create their tables in a database called `TypeAuditDB` (created on first run if missing) and are idempotent — re-running drops and rebuilds the tables.

## Files

### [type_audit_test_data.sql](type_audit_test_data.sql) — type-coverage / edge-case audit (small)

Three tables, exhaustively designed to surface SQL→CSV roundtrip bugs in as few rows as possible:

| Table                         | Rows | Purpose                                                                            |
|-------------------------------|------|------------------------------------------------------------------------------------|
| `dbo.TypeAudit_Standard`      | 6    | Every common scalar type. Each row tests a different edge axis (mins, maxes, typical, precision edges, all-NULL, unicode + NUL). |
| `dbo.TypeAudit_HighPrecision` | 2    | `decimal(38,0)` — one in-range value, one that exceeds .NET decimal range.         |
| `dbo.TypeAudit_UDT`           | 1    | `hierarchyid` + `geography` + `geometry`.                                          |

Use this when:
- You're debugging a specific bug or type-handling change.
- You need a CSV small enough to inspect cell-by-cell.
- You're handing off a SQL+CSV pair for someone to verify roundtrip correctness.

The `TestCase` column on every row describes what the row is testing, so a verifier can scan the CSV next to the script and check each row in isolation.

### [type_audit_bulk_test_data.sql](type_audit_bulk_test_data.sql) — pagination correctness (large)

Three tables with 500 rows each, schemas mirroring the small audit tables:

| Table                              | Rows | Purpose                                                                          |
|------------------------------------|------|----------------------------------------------------------------------------------|
| `dbo.TypeAudit_Bulk_Standard`      | 500  | Full-type-coverage rows with values deterministic from `Id`.                     |
| `dbo.TypeAudit_Bulk_HighPrecision` | 500  | All 500 values exceed .NET decimal range (every row exercises the SqlDecimal path). |
| `dbo.TypeAudit_Bulk_UDT`           | 500  | 500 distinct hierarchyid / geography / geometry values.                          |

Edge-case interactions:
- Every 50th row sets `BitCol = NULL` — verifies NULL handling across batches.
- Every 100th row appends `CR + NUL + comma + quote` to string columns — lands on the `-b 100` batch boundary, verifying the CSV-quoting fixes survive pagination.

Use this when:
- You want to verify keyset pagination produces correct CSVs across many batches.
- You're checking that no rows are missed, duplicated, or reordered at batch boundaries.

Recommended invocation: `-b 100` so the 500 rows force five full keyset queries plus a terminal empty fetch.

## Verifying a bulk export

Most pagination bugs manifest as missing rows, duplicates, or out-of-order rows. PowerShell one-liner:

```powershell
$rows = Import-Csv test_output/<dir>/<table>_1.csv
$ids = $rows.Id | ForEach-Object { [int]$_ }
"Count          : $($rows.Count)            (expect 500)"
"Min Id         : $(($ids | Measure-Object -Minimum).Minimum)        (expect 1)"
"Max Id         : $(($ids | Measure-Object -Maximum).Maximum)        (expect 500)"
"Unique Id count: $(($ids | Select-Object -Unique).Count)            (expect 500)"
"Strictly sorted: $((Compare-Object $ids ($ids | Sort-Object)).Count -eq 0)"
```

Spot-check determinism — pick any row by `Id` and confirm `TestCase = "Row N"`, `IntCol = N`, `BigIntCol = N * 1000000`, `DateCol = 2024-01-01 + N days`, etc.

## Choosing which one to run

- Type-handling change in the read/format path → run **type_audit_test_data.sql**, audit the 6-row CSV cell-by-cell.
- Pagination / keyset / batching change → run **type_audit_bulk_test_data.sql**, export with `-b 100`, run the verification one-liner.
- Major release validation → run both.
