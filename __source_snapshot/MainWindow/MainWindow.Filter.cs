using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Configurator
{
    public partial class MainWindow
    {
        private void ShowFilterNotAppliedNotice(string fieldName)
        {
            string message = string.IsNullOrWhiteSpace(fieldName)
                ? "Данный фильтр невозможно применить к текущей таблице так как такое поле не определено."
                : $"Данный фильтр невозможно применить к текущей таблице так как поле «{fieldName}» не определено.";

            // Фильтр не сбрасываем: только сообщаем пользователю и оставляем
            // текущие данные без применения несовместимого условия.
            if (_actionLog != null)
                _actionLog.Info(message);
            if (LastActionText != null)
                LastActionText.Text = message;
        }

        private async Task ReloadWithoutCurrentFilterAsync(TableViewModel vm, CancellationToken ct)
        {
            if (vm == null) return;

            var pageDataTask = vm.LoadPageIgnoringFilterAsync(ct);
            var countTask = vm.GetTotalCountIgnoringFilterAsync(ct);

            await Task.WhenAll(pageDataTask, countTask);
            if (ct.IsCancellationRequested || !ReferenceEquals(vm, _activeVm))
                return;

            var pageData = pageDataTask.Result;
            BindPageData(pageData, -1, null, resetScroll: false);
            UpdatePagination();
        }

    }
}
