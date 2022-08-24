using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DNSR.ViewModels;
using Wpf.Ui.Controls;

namespace DNSR
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : UiWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
            this.Activated += MainWindow_Activated;
            NotifyIcon.Visibility = Visibility.Hidden;
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            NotifyIcon.Visibility = Visibility.Hidden;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            var vm = this.DataContext as MainViewModel;
            if (!vm.Configuration.IsNotFirstRun)
            {
                vm.Configuration.IsNotFirstRun = true;
                vm.WriteConfig();
                _ = Task.Run(async () =>
                {
                    App.Current.Dispatcher.Invoke(() =>
                        this.RootSnackbar.Show("Notice", "Application is still available in Tray. To close it right click on application icon in tray and click on Close application", Wpf.Ui.Common.SymbolRegular.Info24)
                    );
                    await Task.Delay(4000);
                    App.Current.Dispatcher.Invoke(() =>
                        this.Hide()
                    );
                });
                return;
            }
            this.Hide();

        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            Environment.Exit(0);
        }
    }
}
