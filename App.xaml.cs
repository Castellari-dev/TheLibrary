using System;
using System.Windows;
using System.Windows.Threading;
using TheLibrary.Models;
using TheLibrary.Services;
using TheLibrary.Views;

namespace TheLibrary
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnUnhandled;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var cfg = ConfigService.Load();
            ThemeManager.Apply(cfg.Theme, cfg.Accent);

            if (!Start(cfg))
            {
                Shutdown();
                return;
            }

            var main = new Views.MainWindow();
            this.MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }

        private bool Start(AppConfig cfg)
        {
            bool needsSetup = !cfg.Configured || string.IsNullOrWhiteSpace(ConfigService.GetConnectionString());

            if (!needsSetup)
            {
                try
                {
                    var db = new Database(cfg.Provider, ConfigService.GetConnectionString());
                    db.TestConnection();
                    db.EnsureSchema();
                    Session.Db = db;

                    if (db.CountUsers() == 0) needsSetup = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Não foi possível conectar ao banco configurado:\n\n" + ex.Message +
                        "\n\nA tela de configuração será aberta.",
                        "The Library", MessageBoxButton.OK, MessageBoxImage.Warning);
                    needsSetup = true;
                }
            }

            if (needsSetup)
            {
                var setup = new SetupWindow();
                bool? ok = setup.ShowDialog();
                if (ok != true) return false;
                return Session.IsReady;
            }

            var login = new LoginWindow();
            bool? logged = login.ShowDialog();
            return logged == true && Session.IsReady;
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Ocorreu um erro inesperado:\n\n" + e.Exception.Message,
                "The Library", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
