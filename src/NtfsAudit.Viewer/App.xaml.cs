using System.Windows;
using NtfsAudit.App;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.Viewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var viewModel = new MainViewModel(true);
            var window = new MainWindow(viewModel)
            {
                Title = "NTFS Audit Viewer"
            };
            window.Show();
        }
    }
}
