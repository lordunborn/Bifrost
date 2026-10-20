using System.Windows;
using Caliburn.Micro;
using OmegaLauncher.Wpf.ViewModels;

namespace OmegaLauncher.Wpf
{
    public class Bootstrapper :BootstrapperBase
    {
        public Bootstrapper()
        {
            Initialize();
        }

        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            DisplayRootViewForAsync<ShellViewModel>();
        }
    }
}
