using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator
{
    /// <summary>
    /// Backwards-compatible facade for database operations.
    /// The implementation is split into focused partial files by responsibility.
    /// </summary>
    public partial class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connString)
        {
            _connectionString = connString ?? throw new ArgumentNullException(nameof(connString));
        }

        private static (string schema, string table) ParseTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Имя таблицы не задано.", nameof(tableName));

            var parts = tableName.Split('.', 2);
            return parts.Length > 1 ? (parts[0], parts[1]) : ("dbo", parts[0]);
        }

        private string QuoteIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Идентификатор SQL не задан.", nameof(name));
            return "[" + name.Replace("]", "]]" ) + "]";
        }

        private string QuoteTableName(string tableName)
        {
            var (schema, table) = ParseTableName(tableName);
            return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        }

        private static string EscapeSqlLiteral(string value) => (value ?? "").Replace("'", "''");
        private static string QuoteStatic(string name) => "[" + (name ?? "").Replace("]", "]]" ) + "]";
    }
}
