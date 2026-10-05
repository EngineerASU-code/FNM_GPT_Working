# GA / Milk — карта структуры и взаимосвязей БД

Источник: `GA_Milk_SchemaExport.xlsx`.

Эта версия документа подготовлена из фактического Excel-экспорта схемы. GA содержит ту же базовую модель связей, но имеет меньше таблиц/колонок, чем EK.

Ключевые правила:

- `Area` объектов → `Areas.AreaName`, а не `Areas.Record`.
- `PLC` → `PLC.Record`.
- `Class` → `Classes.Record`.
- `PLC + PLC_Class_Number + Record` — базовый составной идентификатор простых объектов.
- `PLC_Class_Number` разрешается через `PLC_CFG`.
- `Type` и `Unit` — логические lookup и могут иметь исторические несовпадения.
- Matrix имеет физические FK внутри своей подсистемы.
- Matrix device — полиморфная ссылка через `Classes.Record → Classes.DBName`.
- Program — сложная сущность с дочерними таблицами и контекстными ключами.

В GA отсутствуют относительно EK: `PLC.IP/Rack/Slot`, часть `PLC_CFG`, `PLC_Types.CpuType`, Matrix-поля в Program Queue/Sequence, HMI-поля StepParam, а также таблицы `Prog_Statuses` и `Prog_StepParamHMITypes`.

Следствие: Configurator 4.0 должен читать возможности конкретной БД при подключении и не считать отсутствующие колонки ошибкой.

Физические FK не изменяются и новые FK при клонировании не создаются. Удаление выполняется только через dependency plan + replacement + validation + transaction + commit/rollback.
