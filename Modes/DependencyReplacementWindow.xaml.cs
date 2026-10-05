using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class DependencyReplacementWindow : Window
{
    public IReadOnlyList<DependencyReplacementItem> Items { get; private set; } = Array.Empty<DependencyReplacementItem>();
    public bool Confirmed { get; private set; }

    public DependencyReplacementWindow(DeletionPlan plan, Window owner = null)
    {
        InitializeComponent();
        Owner = owner;
        var groups = plan.Dependencies
            .GroupBy(x => new { x.SourceTable, x.Relation.RuleId, Columns = string.Join(",", x.Relation.SourceColumns) })
            .Select(g => new DependencyReplacementItem
            {
                SourceTable = g.Key.SourceTable,
                RuleId = g.Key.RuleId,
                RelationText = $"{string.Join(", ", g.First().Relation.SourceColumns)} → {string.Join(", ", g.First().Relation.TargetColumns)}",
                Count = g.Count(),
                SourceColumns = g.First().Relation.SourceColumns.ToArray()
            }).ToList();
        Items = groups;
        ItemsHost.ItemsSource = Items;
        TxtSummary.Text = $"Удаление: {plan.Request.TableName}. Найдено зависимых строк: {plan.Dependencies.Count}. Укажите новое значение для каждой группы. После замены Configurator повторно проверит связи внутри той же SQL-транзакции.";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Items.Any(x => string.IsNullOrWhiteSpace(x.NewValue)))
        {
            MessageBox.Show("Для каждой найденной зависимости необходимо указать новое значение. Если замена не нужна, отмените удаление.", "Замена зависимостей", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; DialogResult = false; }
}

public sealed class DependencyReplacementItem
{
    public string SourceTable { get; init; } = "";
    public string RuleId { get; init; } = "";
    public string RelationText { get; init; } = "";
    public int Count { get; init; }
    public IReadOnlyList<string> SourceColumns { get; init; } = Array.Empty<string>();
    public string NewValue { get; set; } = "";
    public string CountText => $"Зависимых строк: {Count}";
}
