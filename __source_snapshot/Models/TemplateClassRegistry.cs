using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Configurator
{
    /// <summary>
    /// Хранит только созданные пользователем классы редактора шаблонов.
    /// Системные классы Valve/DSV/... хранятся в коде и защищены паролем.
    /// </summary>
    public static class TemplateClassRegistry
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Configurator");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "template-classes.json");

        public sealed class Entry
        {
            public string Server { get; set; } = "";
            public string Database { get; set; } = "";
            public string ClassName { get; set; } = "";
            public string TableName { get; set; } = "";
        }

        public static List<Entry> Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new List<Entry>();
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<List<Entry>>(json) ?? new List<Entry>();
            }
            catch
            {
                return new List<Entry>();
            }
        }

        public static void Upsert(string server, string database, string className, string tableName)
        {
            var items = Load();
            var item = items.FirstOrDefault(x =>
                string.Equals(x.Server, server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Database, database, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ClassName, className, StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                items.Add(new Entry
                {
                    Server = server ?? "",
                    Database = database ?? "",
                    ClassName = className ?? "",
                    TableName = tableName ?? ""
                });
            }
            else
            {
                item.TableName = tableName ?? "";
            }

            Save(items);
        }

        public static void Remove(string server, string database, string className)
        {
            var items = Load();
            items.RemoveAll(x =>
                string.Equals(x.Server, server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Database, database, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ClassName, className, StringComparison.OrdinalIgnoreCase));
            Save(items);
        }

        public static void RemoveByTable(string server, string database, string tableName)
        {
            var items = Load();
            items.RemoveAll(x =>
                string.Equals(x.Server, server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Database, database, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.TableName, tableName, StringComparison.OrdinalIgnoreCase));
            Save(items);
        }

        private static void Save(List<Entry> items)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var temp = SettingsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, SettingsPath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TemplateClassRegistry] Save failed: {ex.Message}");
            }
        }
    }
}