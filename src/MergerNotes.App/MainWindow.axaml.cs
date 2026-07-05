using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MergerNotes.App.Localization;
using MergerNotes.App.ViewModels;

namespace MergerNotes.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var openBaseButton = this.FindControl<Button>("OpenBaseButton");
        if (openBaseButton is not null)
        {
            openBaseButton.Click += OpenBaseButton_Click;
        }

        var openIncomingButton = this.FindControl<Button>("OpenIncomingButton");
        if (openIncomingButton is not null)
        {
            openIncomingButton.Click += OpenIncomingButton_Click;
        }

        var mergeButton = this.FindControl<Button>("MergeButton");
        if (mergeButton is not null)
        {
            mergeButton.Click += MergeButton_Click;
        }
    }

    private async void OpenBaseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = vm.OpenBasePickerTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(vm.BackupFileTypeName)
                {
                    Patterns = ["*.jwlibrary"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        await vm.LoadBaseBackupAsync(files[0].Path.LocalPath);
    }

    private async void OpenIncomingButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = vm.OpenIncomingPickerTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(vm.BackupFileTypeName)
                {
                    Patterns = ["*.jwlibrary"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        await vm.LoadIncomingBackupAsync(files[0].Path.LocalPath);
    }

    private async void MergeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.SaveMergedPickerTitle,
            SuggestedFileName = "merged.jwlibrary",
            DefaultExtension = "jwlibrary",
            FileTypeChoices =
            [
                new FilePickerFileType(vm.BackupFileTypeName)
                {
                    Patterns = ["*.jwlibrary"]
                }
            ]
        });

        if (saveFile is null)
        {
            return;
        }

        await vm.MergeAsync(saveFile.Path.LocalPath);
    }
}
