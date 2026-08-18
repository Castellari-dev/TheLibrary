using System.Windows;
using System.Windows.Input;

namespace TheLibrary.Services
{
    public static class Busy
    {
        public static void Set(bool on)
        {
            Mouse.OverrideCursor = on ? Cursors.Wait : null;
        }
    }

    public static class Dialogs
    {
        public static void Info(string message, string title = "The Library")
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public static void Warn(string message, string title = "The Library")
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public static void Error(string message, string title = "The Library")
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public static bool Confirm(string message, string title = "Confirmar")
            => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}
