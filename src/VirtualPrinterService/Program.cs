using System;
using System.ServiceProcess;

namespace VirtualPrinter.Service
{
    static class Program
    {
        static void Main()
        {
            ServiceBase.Run(new ServiceBase[] { new MainService() });
        }
    }
}
