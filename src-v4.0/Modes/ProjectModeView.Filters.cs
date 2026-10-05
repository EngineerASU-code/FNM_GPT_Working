using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Configurator;

public partial class ProjectModeView
{
    private async Task PopulateGlobalFilterListsAsync()
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase))
        {
            InitializeEmptyFilterLists();
            return;
        }

        try
        {
            var classDefs = _architecture.Classes
                .Where(x => x.Available && !x.Name.Equals("Program", StringComparison.OrdinalIgnoreCase)
                            && !x.Name.Equals("Step", StringComparison.OrdinalIgnoreCase)
                            && !x.Name.Equals("Matrix", StringComparison.OrdinalIgnoreCase)
                            && x.PrimaryStorage != null)
                .ToList();
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var plcTask = LoadLookupOptionsAsync(db, new[] { "dbo.PLCs", "dbo.PLC" }, "PLC", "PLC");
            var objectTasks = classDefs.Select(async c => await db.GetDistinctColumnValuesAsync(c.PrimaryStorage.TableName, "PLC"));
            var plcLookup = await plcTask;
            var objectValues = await Task.WhenAll(objectTasks);

            var plcMap = plcLookup.ToDictionary(x => x.Key, x => x.Title, StringComparer.OrdinalIgnoreCase);
            foreach (var values in objectValues)
                foreach (var value in values)
                    if (!string.IsNullOrWhiteSpace(value) && !plcMap.ContainsKey(value)) plcMap[value] = $"PLC {value}";

            var plcOptions = new List<FilterOption> { new("*", "Все PLC") };
            plcOptions.AddRange(plcMap.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => new FilterOption(x.Key, x.Value)));

            _filterSelectionGuard = true;
            CmbPlcFilter.ItemsSource = plcOptions;
            CmbPlcFilter.SelectedIndex = CmbPlcFilter.Items.Count > 0 ? 0 : -1;
            _filterSelectionGuard = false;
            ApplyObjectFilters();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Фильтры PLC: {ex.Message}";
            InitializeEmptyFilterLists();
        }
    }

    private void InitializeEmptyFilterLists()
    {
        _filterSelectionGuard = true;
        CmbPlcFilter.ItemsSource = new[] { new FilterOption("*", "Все PLC") };
        CmbPlcFilter.SelectedIndex = 0;
        _filterSelectionGuard = false;
        ApplyObjectFilters();
    }

    private static async Task<List<FilterOption>> LoadLookupOptionsAsync(DatabaseService db, IEnumerable<string> tableCandidates, string keyColumn, string titleColumn)
    {
        foreach (var table in tableCandidates)
        {
            try
            {
                var columns = await db.GetTableColumnsAsync(table);
                if (columns.Count == 0) continue;

                var key = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals(keyColumn, StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Record", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Id", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(key)) continue;

                var title = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals(titleColumn, StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Description", StringComparison.OrdinalIgnoreCase));

                var data = await db.GetTableDataPageAsync(table, 1, 10000, new[] { key }, null);
                var result = new List<FilterOption>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (DataRow row in data.Rows)
                {
                    if (!data.Columns.Contains(key) || row[key] == DBNull.Value) continue;
                    var id = Convert.ToString(row[key]) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;

                    var display = id;
                    if (!string.IsNullOrWhiteSpace(title) && data.Columns.Contains(title) && row[title] != DBNull.Value)
                    {
                        var text = Convert.ToString(row[title]);
                        if (!string.IsNullOrWhiteSpace(text)) display = text;
                    }

                    result.Add(new FilterOption(id, display));
                }

                if (result.Count > 0)
                    return result.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                // Try the next candidate table.
            }
        }

        return new List<FilterOption>();
    }

    private void RebuildObjectFilters()
    {
        // PLC list is global for the selected database and is not rebuilt from a single page.
        ApplyObjectFilters();
    }

    private static HashSet<string> GetSelectedFilterKeys(ListBox list)
    {
        var selected = list.SelectedItems.Cast<FilterOption>().Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0 || selected.Contains("*"))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" };
        return selected;
    }

    private void ApplyObjectFilters()
    {
        var plcKeys = GetSelectedFilterKeys(CmbPlcFilter);
        var result = BuildObjectNames(_objects, _selectedClass).Where(item =>
        {
            if (item.RowIndex < 0 || item.RowIndex >= _objects.Rows.Count) return false;
            var row = _objects.Rows[item.RowIndex];
            string plc = _objects.Columns.Contains("PLC") ? Convert.ToString(row["PLC"]) ?? "" : "";
            return plcKeys.Contains("*") || plcKeys.Contains(plc);
        }).ToList();
        ObjectList.ItemsSource = result;
    }

    private void ObjectFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_filterSelectionGuard) return;

        if (sender is ListBox list)
        {
            var selected = list.SelectedItems.Cast<FilterOption>().ToList();
            if (selected.Any(x => x.Key == "*"))
            {
                _filterSelectionGuard = true;
                var allWasJustAdded = e.AddedItems.OfType<FilterOption>().Any(x => x.Key == "*");
                var nonAllWasAdded = e.AddedItems.OfType<FilterOption>().FirstOrDefault(x => x.Key != "*");
                list.SelectedItems.Clear();
                if (allWasJustAdded)
                {
                    var all = list.Items.Cast<FilterOption>().FirstOrDefault(x => x.Key == "*");
                    if (all != null) list.SelectedItems.Add(all);
                }
                else if (nonAllWasAdded != null)
                {
                    list.SelectedItems.Add(nonAllWasAdded);
                }
                _filterSelectionGuard = false;
            }
        }

        if (_objects != null) ApplyObjectFilters();
    }

    private sealed record FilterOption(string Key, string Title);
}
