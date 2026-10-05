using System;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Configurator
{
    public partial class MainWindow
    {
        public void SetPaginationButtons(bool first, bool prev, bool next, bool last)
        {
            if (_rightPanel == null) return;

            _rightPanel.BtnFirst.IsEnabled = first;
            _rightPanel.BtnPrev.IsEnabled = prev;
            _rightPanel.BtnNext.IsEnabled = next;
            _rightPanel.BtnLast.IsEnabled = last;
        }

        public void UpdatePagination()
        {
            if (_activeVm == null || _rightPanel?.PageInfo == null)
            {
                SetPaginationButtons(false, false, false, false);
                return;
            }

            if (_activeVm.CurrentPage < 1)
                _activeVm.CurrentPage = 1;
            if (_activeVm.CurrentPage > _activeVm.TotalPages)
                _activeVm.CurrentPage = _activeVm.TotalPages;

            _rightPanel.PageInfo.Text = $"Стр. {_activeVm.CurrentPage} / {_activeVm.TotalPages}";

            SetPaginationButtons(
                first: _activeVm.CurrentPage > 1,
                prev: _activeVm.CurrentPage > 1,
                next: _activeVm.CurrentPage < _activeVm.TotalPages,
                last: _activeVm.CurrentPage < _activeVm.TotalPages
            );
        }

        public async void BtnFirst_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_activeVm == null) return;
            _activeVm.CurrentPage = 1;
            await LoadDataAsync();
        }

        public async void BtnPrev_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_activeVm == null || _activeVm.CurrentPage <= 1) return;
            _activeVm.CurrentPage--;
            await LoadDataAsync();
        }

        public async void BtnNext_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_activeVm == null || _activeVm.CurrentPage >= _activeVm.TotalPages) return;
            _activeVm.CurrentPage++;
            await LoadDataAsync();
        }

        public async void BtnLast_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_activeVm == null) return;
            _activeVm.CurrentPage = _activeVm.TotalPages;
            await LoadDataAsync();
        }

        public async Task JumpToLastPageAsync()
        {
            if (_activeVm == null) return;
            // Не используем int.MaxValue: это приводит к переполнению OFFSET.
            _activeVm.CurrentPage = _activeVm.TotalPages;
            await LoadDataAsync();
        }
    }
}
