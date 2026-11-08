using System.Windows;

namespace WorkManagement.Views;

public partial class TaskSchedulerConfigDialog : Window
{
    public TaskSchedulerConfigDialog()
    {
        InitializeComponent();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
