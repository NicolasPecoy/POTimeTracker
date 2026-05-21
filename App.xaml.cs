using System;
using System.Threading.Tasks;
using System.Windows;
using POTimeTracker.Services;

namespace POTimeTracker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Services.EnvLoader.Load();
            base.OnStartup(e);

            DispatcherUnhandledException += (_, ex) =>
            {
                LogService.Error("Excepcion no manejada en UI thread", ex.Exception);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                if (ex.ExceptionObject is Exception exception)
                    LogService.Error("Excepcion no manejada en dominio", exception);
            };

            TaskScheduler.UnobservedTaskException += (_, ex) =>
            {
                LogService.Error("Excepcion no observada en Task", ex.Exception);
                ex.SetObserved();
            };

            LogService.Info("Aplicacion iniciada");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Info("Aplicacion cerrada");
            base.OnExit(e);
        }
    }
}
