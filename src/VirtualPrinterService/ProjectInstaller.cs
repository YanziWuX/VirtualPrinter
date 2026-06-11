using System;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace VirtualPrinter.Service
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private ServiceProcessInstaller _processInstaller;
        private ServiceInstaller _serviceInstaller;

        public ProjectInstaller()
        {
            _processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem,
                Password = null,
                Username = null
            };

            _serviceInstaller = new ServiceInstaller
            {
                ServiceName = "VirtualPrinterService",
                DisplayName = "VirtualPrinter Service",
                Description = "PostScript to PDF/Image conversion service for VirtualPrinter",
                StartType = ServiceStartMode.Automatic,
                DelayedAutoStart = true
            };

            Installers.Add(_processInstaller);
            Installers.Add(_serviceInstaller);
        }
    }
}
