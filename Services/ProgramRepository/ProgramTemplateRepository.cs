using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

public sealed partial class ProgramTemplateRepository
{
        public const string ProgramTable = "dbo.Prog";
        public const string StepTable = "dbo.Prog_Step";

        private readonly string _connectionString;
        private readonly Dictionary<string, List<ColumnInfo>> _columnsCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _tableExistsCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyList<ProgramSectionDefinition> ProgramSections = new[]
        {
            new ProgramSectionDefinition { Key = "procedure", Caption = "Процедура", TableName = "dbo.Prog_Seq", Description = "Последовательность и визуальная схема шагов/переходов" },
            new ProgramSectionDefinition { Key = "programParams", Caption = "Параметры программы", TableName = "dbo.Prog_StepParam", Description = "Параметры программы: Step_Number = 0" },
            new ProgramSectionDefinition { Key = "stepParams", Caption = "Параметры шага", TableName = "dbo.Prog_StepParam", Description = "Параметры шагов: Step_Number > 0" },
            new ProgramSectionDefinition { Key = "softkeys", Caption = "Кнопки", TableName = "dbo.Prog_SoftKey", Description = "SoftKey программы" },
            new ProgramSectionDefinition { Key = "messages", Caption = "Сообщения", TableName = "dbo.Prog_Msg", Description = "Сообщения, предупреждения и ошибки" },
            new ProgramSectionDefinition { Key = "recipes", Caption = "Рецепты", TableName = "dbo.Prog_Recipe", Description = "Рецепты и их значения" },
            new ProgramSectionDefinition { Key = "queues", Caption = "Очереди", TableName = "dbo.Prog_Queue", Description = "Очереди и выборы очередей" },
        };

        public ProgramTemplateRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public IReadOnlyList<ProgramSectionDefinition> Sections => ProgramSections;

}
