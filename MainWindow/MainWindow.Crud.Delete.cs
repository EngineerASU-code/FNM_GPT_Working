using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Configurator;

public partial class MainWindow
{
    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_activeVm == null || !_activeVm.HasValidKey) return;
        if (_rightPanel.DataTableGrid.SelectedItems.Count == 0) return;

        if (_rightPanel.DataTableGrid.SelectedItems.Count > 1)
        {
            MessageBox.Show("Для новой архитектуры удаление зависимых объектов выполняется по одной записи. Это необходимо, чтобы для каждой зависимости пользователь явно указал замену и чтобы проверка могла быть выполнена в одной транзакции.", "Безопасное удаление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_rightPanel.DataTableGrid.SelectedItem is not DataRowView row) return;
        string deleteTable = _activeVm.CurrentTable;
        string deleteDbName = _activeDbName;
        var snapshot = row.DataView.Table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => row[c.ColumnName]);

        try
        {
            bool deleted = await new SafeDeletionWorkflowService(_connection.ToConnectionString(deleteDbName))
                .TryDeleteAsync(deleteTable, snapshot, this);
            if (!deleted) return;

            _fieldsBuilder.Clear();
            _activeVm.ClearCurrentRow();
            SafeSetButtonEnabled(_rightPanel.BtnUpdate, false);
            SafeSetButtonEnabled(_rightPanel.BtnDuplicate, false);
            SafeSetButtonEnabled(_rightPanel.BtnDelete, false);
            LogAction($"Удалена запись · {deleteTable}");

            if (_activeVm.CurrentPage > _activeVm.TotalPages)
                _activeVm.CurrentPage = _activeVm.TotalPages;
            await ReloadCurrentPageFastAsync(0, null);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Удаление остановлено: " + ex.Message, "Безопасное удаление", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
