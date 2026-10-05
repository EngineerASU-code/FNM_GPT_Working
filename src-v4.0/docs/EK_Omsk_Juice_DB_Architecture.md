# EK / Omsk Juice — карта структуры и взаимосвязей БД

Источник: `EK_Omsk_Juice_SchemaExport.xlsx`.

Эта версия документа подготовлена из фактического Excel-экспорта схемы и содержит таблицы, колонки, физические FK, логические правила, Program/Matrix и защиту удаления.

Полная локальная версия содержит полный список колонок каждой таблицы. Основные правила:

- `Area` объектов → `Areas.AreaName`, а не `Areas.Record`.
- `PLC` → `PLC.Record`.
- `Class` → `Classes.Record`.
- `PLC + PLC_Class_Number + Record` — базовый составной идентификатор простых объектов.
- `PLC_Class_Number` разрешается через `PLC_CFG.(PLC_Number, PLC_Class_Number) → Class_Number`.
- `Type` и `Unit` — логические lookup, зависящие от версии БД.
- Matrix имеет физические FK внутри своей подсистемы.
- Matrix device — полиморфная ссылка через `Classes.Record → Classes.DBName`.
- Program — сложная сущность с дочерними таблицами и контекстными ключами.

Физические FK не изменяются и новые FK при клонировании не создаются.

Для удаления применяется схема: анализ зависимостей → выбор замен → dry-run validation → SQL transaction → UPDATE зависимостей → reinitialization validation → DELETE → COMMIT; ошибка ведёт к ROLLBACK.
