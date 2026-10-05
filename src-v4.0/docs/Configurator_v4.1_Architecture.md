# Configurator 4.1 — architecture

Configurator 4.1 treats the connected SQL database as the source of truth. System class names are labels only; they do not define a fixed database schema.

## 1. Schema Intelligence Layer

At connection time the application builds an internal model of the actual database:

- tables and columns;
- primary/unique keys;
- physical foreign keys;
- inferred logical relations;
- entity scopes;
- detected capabilities;
- hierarchical structures for Program, Step and Matrix.

The model is used by both the UI graph and data operations. The Configurator never creates or changes SQL relationships merely because it detected them.

## 2. Dependency Graph

Mode 2 should use the same `DependencyGraph` model for visualization and backend traversal. A graph edge displays the concrete source and target columns, for example `AnaIN.Area → Areas.AreaName`. The graph is factual: it is not a filter or a list of manually maintained project templates.

## 3. Entity descriptors and scopes

Each entity can describe its table, primary key, fields, relations, children and supported operations. Scope is explicit:

- Global;
- PLC;
- PLC + Class;
- PLC + Class + Parent.

This prevents a Record value from being treated as globally unique when the real database uses a composite context.

## 4. Operation Plan

Add, edit, clone, move and delete operations are represented as an `OperationPlan` before execution. The plan can be shown as a dry run, validated, executed in a transaction, and validated again before commit.

## 5. Hierarchical Program / Matrix

Program, Step and Matrix are not flat CRUD tables. They are dynamic entity graphs. When a child such as StepParam is added, the planner walks the detected child hierarchy and creates all required descendants that actually exist in the connected database. Missing optional tables are not assumed.

## 6. Deep clone and reference remapping

`ReferenceRemappingEngine` stores old-to-new identifiers. Internal references in a cloned subtree are remapped; external references remain subject to an explicit policy instead of being silently redirected.

## 7. Safe deletion

Deletion must first build dependencies and a replacement plan. A parent such as Area is not deleted while dependent rows still reference it. Replacement updates and the final delete are executed as one transaction. Validation failure causes rollback.

## 8. Record allocation

`RecordAllocator` finds the first unused value in the actual scope rather than using MAX + 1. Manual values are rejected when they are outside the detected range or already occupied in that scope.

## 9. Compatibility report

The architecture layer exposes detected capabilities and warnings. A database can therefore be partially compatible without pretending that missing structures exist. This is essential because historical project databases may have different schemas.

## 10. Operation journal

The journal records the operation itself, entity, key/context, affected rows, validation result and transaction outcome. It is not a button-click log.
