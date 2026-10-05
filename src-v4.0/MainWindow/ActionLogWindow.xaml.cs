using System;
using System.Linq;
using System.Windows;

namespace Configurator;

public partial class ActionLogWindow : Window
{
    private readonly ActionLogService _service;

    public ActionLogWindow(ActionLogService service)
    {
        InitializeComponent();
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.Changed += Service_Changed;
        Refresh();
        Closed += (_, _) => _service.Changed -= Service_Changed;
    }

    private void Service_Changed(object sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.DataBind);

    private void Refresh()
    {
        LogList.ItemsSource = _service.Entries.ToList();
        TxtCount.Text = $"Записей: {_service.Entries.Count}";
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[0]);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
