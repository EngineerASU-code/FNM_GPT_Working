using System.Windows.Controls;

namespace Configurator;

public partial class RightPanel : UserControl
{
    public RightPanel() => InitializeComponent();
    public Button UndoButton => BtnUndo;
}
