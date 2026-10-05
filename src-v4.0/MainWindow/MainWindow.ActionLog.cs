using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Configurator;

public partial class MainWindow
{
    private void ActionLog_Changed(object sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(RefreshActionLogUi);
            return;
        }
        RefreshActionLogUi();
    }

    private void RefreshActionLogUi()
    {
        if (LastActionText != null)
            LastActionText.Text = _actionLog.LastDescription;

        if (_rightPanel?.UndoButton != null)
            _rightPanel.UndoButton.IsEnabled = _actionLog.CanUndo;
    }

    private void MenuOptionsActionLog_Click(object sender, RoutedEventArgs e)
    {
        _actionLog.Info("Открыт журнал действий");
        var window = new ActionLogWindow(_actionLog) { Owner = this };
        window.ShowDialog();
    }

    private void GlobalButtonClicked(object sender, RoutedEventArgs e)
    {
        // CheckBox/ToggleButton тоже наследуются от ButtonBase, но это не
        // пользовательские команды журнала. Иначе в журнал попадали
        // технические имена вроде "toggleButton".
        if (e.OriginalSource is not Button button)
            return;

        string label = button.Content?.ToString();
        if (string.IsNullOrWhiteSpace(label))
            label = button.ToolTip?.ToString();
        if (string.IsNullOrWhiteSpace(label))
            label = button.Name;

        if (!string.IsNullOrWhiteSpace(label))
            _actionLog.Info($"Нажата кнопка «{label}»");
    }

    private async void BtnUndo_Click(object sender, RoutedEventArgs e)
        => await UndoLastActionAsync();

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            await UndoLastActionAsync();
        }
    }

    private async Task UndoLastActionAsync()
    {
        try
        {
            await _actionLog.UndoAsync();
            if (_activeVm != null)
                await ReloadCurrentPageFastAsync();
            RefreshActionLogUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось отменить действие: {ex.Message}", "Отмена действия", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LogAction(string description, Func<Task> undo = null)
        => _actionLog.Record(description, undo);
}
