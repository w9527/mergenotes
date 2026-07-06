using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MergerNotes.App.Localization;
using MergerNotes.Infrastructure.Import;

namespace MergerNotes.App;

public partial class App : Application
{
    public static LocalizationService Localization { get; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = new ViewModels.MainWindowViewModel(
                    new JwlibraryBackupImporter(),
                    new JwlibraryBackupMerger(),
                    Localization)
            };

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
