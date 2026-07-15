using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using POTimeTracker.Services;

namespace POTimeTracker
{
    public partial class App : Application
    {
        // Fixed, app-specific names so any running version (old or new) recognizes the others.
        private const string MutexName     = "POTimeTracker_SingleInstance_Mutex_9F3B2C41";
        private const string ShowEventName = "POTimeTracker_SingleInstance_ShowEvent_9F3B2C41";
        private const string ExitEventName = "POTimeTracker_SingleInstance_ExitEvent_9F3B2C41";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showEvent;
        private EventWaitHandle? _exitEvent;
        private Thread? _signalListenerThread;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Another instance is already running. If this one is a newer version,
                // ask the older instance to close and take its place; otherwise just
                // bring the existing instance to the front and exit.
                if (IsThisVersionNewerThanRunningInstance() && TakeOverFromOlderInstance())
                {
                    LogService.Info("Instancia anterior cerrada: esta version es mas nueva");
                    StartSignalListener();
                    RunNormalStartup(e);
                    return;
                }

                NotifyExistingInstance();
                Environment.Exit(0);
                return;
            }

            StartSignalListener();
            RunNormalStartup(e);
        }

        private void RunNormalStartup(StartupEventArgs e)
        {
            Services.EnvLoader.Load();
            Services.UiScaleService.Initialize();
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

        /// <summary>Compares this process' version against any other running instance's file version.</summary>
        private static bool IsThisVersionNewerThanRunningInstance()
        {
            try
            {
                if (!Version.TryParse(UpdateService.GetCurrentVersion(), out var myVersion))
                    return false;

                var current = Process.GetCurrentProcess();
                var others = Process.GetProcessesByName(current.ProcessName)
                    .Where(p => p.Id != current.Id);

                foreach (var p in others)
                {
                    try
                    {
                        var otherVersionStr = p.MainModule?.FileVersionInfo.FileVersion;
                        if (otherVersionStr != null && Version.TryParse(otherVersionStr, out var otherVersion))
                            return myVersion > otherVersion;
                    }
                    catch { /* Process exited mid-check or access denied; ignore and keep looking */ }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("IsThisVersionNewerThanRunningInstance: no se pudo comparar versiones", ex);
            }
            return false;
        }

        /// <summary>Asks the older instance to shut down and waits until it releases the single-instance mutex.</summary>
        private bool TakeOverFromOlderInstance()
        {
            try
            {
                using var exitEvent = EventWaitHandle.OpenExisting(ExitEventName);
                exitEvent.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
            {
                // The other instance predates this feature and won't react to the exit signal.
                return false;
            }

            return _singleInstanceMutex!.WaitOne(TimeSpan.FromSeconds(15));
        }

        private static void NotifyExistingInstance()
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
                showEvent.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
            {
                // The existing instance hasn't created the event yet (startup race) — nothing to signal.
            }
        }

        private void StartSignalListener()
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            var handles = new WaitHandle[] { _showEvent, _exitEvent };

            _signalListenerThread = new Thread(() =>
            {
                while (true)
                {
                    int signaled;
                    try { signaled = WaitHandle.WaitAny(handles); }
                    catch (ObjectDisposedException) { return; }

                    try
                    {
                        if (signaled == 1)
                        {
                            // A newer version is taking over: shut down cleanly and stop listening.
                            Dispatcher.Invoke(Shutdown);
                            return;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow is Views.MainWindow mw)
                                mw.BringToFront();
                        });
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn("StartSignalListener: error al procesar senal de otra instancia", ex);
                    }
                }
            })
            { IsBackground = true, Name = "POTimeTracker-SingleInstanceListener" };
            _signalListenerThread.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Info("Aplicacion cerrada");
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
