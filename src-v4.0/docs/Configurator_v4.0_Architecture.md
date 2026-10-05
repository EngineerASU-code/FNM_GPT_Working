# Configurator 4.0 — Architecture

## 1. Core principle

Configurator 4.0 treats the connected project database as the source of truth. System classes are labels only. They do not define table names, columns, relations or the shape of Program/Step/Matrix.

```text
SystemClass labels
       │
       ▼
Connected Database → DatabaseSchema → RelationGraph
                              │              ├─ Physical FK
                              │              ├─ Composite entity key
                              │              ├─ Conventional lookup
                              │              └─ Polymorphic reference
                              ▼
                    ProjectArchitectureProfile
```

## 2. Relation discovery

1. Read tables/columns from `sys.tables/sys.columns/sys.types`.
2. Read PK/UNIQUE indexes from `sys.indexes/sys.index_columns`.
3. Read real SQL FKs from `sys.foreign_key_columns`.
4. Add conservative logical candidates.
5. Create a schema fingerprint.
6. Keep ambiguous relations unresolved; unresolved relations never authorize destructive operations.

### Generic rules

| Rule | Detection | Confidence |
|---|---|---:|
| Physical FK | SQL Server FK metadata | Confirmed |
| Entity key | `PLC + PLC_Class_Number + Record` matches unique target key | High |
| PLC lookup | `PLC` → `PLC.Record` | High |
| Class lookup | `Class` → `Classes.Record` | High |
| Area lookup | `Area` → `Areas.AreaName` | High |
| Unit lookup | `Unit/Units` → `Units.Unit_ID` | Medium |
| Type lookup | `Type` → matching `<Table>_Type(s).Record` | Medium |
| Polymorphic class | `Device_Global_Class` → `Classes.Record`, then `Classes.DBName` | High |

No rule creates or alters an SQL FK.

## 3. System classes

System classes are labels only. The connected database determines actual tables and fields. `Classes.DBName` can be used as a database-side mapping from class label to concrete table, but the table schema is always read from that database.

## 4. Simple vs complex entities

Simple entities use generic CRUD and dependency protection. Complex entities are explicitly separated:

- Program root: `dbo.Prog` plus `Prog_*` child tables.
- Matrix root: `dbo.Matrix_List` plus `Matrix_*` child tables.

Complex handlers validate the entire graph before destructive operations.

## 5. Program

Root key: `Prog.(PLC, PLC_Class_Number, Record)`.

Typical child context uses `PLC + PLC_Class_Prog_Number + Prog_Record`, with additional Sequence/Step keys. `TransDest_01..08` are destinations inside the sequence context, not automatic FKs to `Prog_Step.Record`.

## 6. Matrix

Root key: `Matrix_List.(PLC, PLC_Class_Number, Record)`.

Physical graph:

```text
Matrix_List
 ├─ Matrix_DeviceList
 │    ├─ Matrix_DeviceListType
 │    └─ Matrix_Devices
 └─ Matrix_CommandList
      └─ Matrix_Commands
```

`Matrix_Devices` is polymorphic: `Device_Global_Class → Classes.Record → Classes.DBName → concrete table`, with the target object identified by `(Device_PLC, Device_PLC_Class_Number, Device_Record)`.

## 7. Deletion protection

```text
Request delete
  ↓
Build DeletionPlan
  ↓
Find confirmed/high dependencies
  ↓
User selects replacements
  ↓
Dry-run / reinitialization validation
  ↓
BEGIN TRANSACTION
  ↓
UPDATE dependent references
  ↓
Validate again
  ├─ fail → ROLLBACK
  └─ pass → DELETE → COMMIT
```

Example: deleting `AreaName = 105` first finds every `*.Area → Areas.AreaName` dependency, requires replacements, validates the result, then deletes the Area row only after successful transaction validation.

## 8. Clone protection

Clone copies values; it does not create or mutate SQL relationships. Complex clone operations are handled by `IEntityClonePolicy` implementations.

## 9. Schema evolution

Missing columns/tables are treated as unavailable features, not fatal schema errors. The same Configurator binary must work with both EK and GA architectures.

## 10. Safety levels

- Confirmed — real SQL FK.
- High — unambiguous structural relation.
- Medium — naming-based logical lookup.
- Low/Unresolved — diagnostics only; never enough for irreversible delete.

## 11. Forbidden behavior

- No automatic `CREATE FOREIGN KEY`.
- No automatic `ALTER TABLE` to restore historical links.
- No system-class template overriding the connected schema.
- No `Areas.Record` substitution for object `Area`.
- No deletion before replacement and validation.
- No silent repair of unresolved references.
- No flat CRUD model for Program/Matrix.
