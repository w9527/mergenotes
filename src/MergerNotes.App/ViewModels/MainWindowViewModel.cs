using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MergerNotes.App.Localization;
using MergerNotes.Core.Abstractions;
using MergerNotes.Core.Models;

namespace MergerNotes.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IBackupImporter _importer;
    private readonly IBackupMerger _merger;
    private readonly LocalizationService _localizer;

    private string _statusMessage;
    private string _baseBackupPath = string.Empty;
    private string _incomingBackupPath = string.Empty;
    private string _mergedOutputPath = string.Empty;
    private string _baseSummaryText;
    private string _incomingSummaryText;
    private string _mergedSummaryText;
    private string _mergeReportText;
    private IReadOnlyList<NotePreview> _baseNotePreviews = Array.Empty<NotePreview>();
    private IReadOnlyList<NotePreview> _incomingNotePreviews = Array.Empty<NotePreview>();
    private IReadOnlyList<NotePreview> _mergedNotePreviews = Array.Empty<NotePreview>();

    private BackupSnapshot? _baseSnapshot;
    private BackupSnapshot? _incomingSnapshot;
    private BackupSnapshot? _mergedSnapshot;
    private MergeReport? _mergeReport;

    public MainWindowViewModel(IBackupImporter importer, IBackupMerger merger, LocalizationService localizer)
    {
        _importer = importer;
        _merger = merger;
        _localizer = localizer;
        _localizer.PropertyChanged += LocalizerOnPropertyChanged;
        _statusMessage = localizer.Strings.StatusOpenBasePrompt;
        _baseSummaryText = localizer.Strings.NoBaseLoaded;
        _incomingSummaryText = localizer.Strings.NoIncomingLoaded;
        _mergedSummaryText = localizer.Strings.NoMergedBackup;
        _mergeReportText = localizer.Strings.LoadBothToPreview;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppTitle => _localizer.Strings.AppTitle;

    public string AppSubtitle => _localizer.Strings.AppSubtitle;

    public string AppCaption => _localizer.Strings.AppCaption;

    public string LanguageLabel => _localizer.Strings.LanguageLabel;

    public IReadOnlyList<LanguageChoice> LanguageChoices => _localizer.LanguageChoices;

    public AppLanguage SelectedLanguage
    {
        get => _localizer.SelectedLanguage;
        set
        {
            if (_localizer.SelectedLanguage == value)
            {
                return;
            }

            _localizer.SelectedLanguage = value;
            RaiseLocalizedProperties();
        }
    }

    public LanguageChoice SelectedLanguageChoice
    {
        get => _localizer.LanguageChoices.First(choice => choice.Language == _localizer.SelectedLanguage);
        set => SelectedLanguage = value.Language;
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string OpenBaseButtonText => _localizer.Strings.OpenBaseButton;

    public string OpenIncomingButtonText => _localizer.Strings.OpenIncomingButton;

    public string MergeButtonText => _localizer.Strings.MergeButton;

    public string WorkspaceTitle => _localizer.Strings.WorkspaceTitle;

    public string BaseBackupTitle => _localizer.Strings.BaseBackupTitle;

    public string IncomingBackupTitle => _localizer.Strings.IncomingBackupTitle;

    public string MergeResultTitle => _localizer.Strings.MergeResultTitle;

    public string BaseBackupPath
    {
        get => _baseBackupPath;
        private set => SetField(ref _baseBackupPath, value);
    }

    public string IncomingBackupPath
    {
        get => _incomingBackupPath;
        private set => SetField(ref _incomingBackupPath, value);
    }

    public string MergedOutputPath
    {
        get => _mergedOutputPath;
        private set => SetField(ref _mergedOutputPath, value);
    }

    public string BaseSummaryText
    {
        get => _baseSummaryText;
        private set => SetField(ref _baseSummaryText, value);
    }

    public string IncomingSummaryText
    {
        get => _incomingSummaryText;
        private set => SetField(ref _incomingSummaryText, value);
    }

    public string MergedSummaryText
    {
        get => _mergedSummaryText;
        private set => SetField(ref _mergedSummaryText, value);
    }

    public string MergeReportText
    {
        get => _mergeReportText;
        private set => SetField(ref _mergeReportText, value);
    }

    public IReadOnlyList<NotePreview> BaseNotePreviews
    {
        get => _baseNotePreviews;
        private set => SetField(ref _baseNotePreviews, value);
    }

    public IReadOnlyList<NotePreview> IncomingNotePreviews
    {
        get => _incomingNotePreviews;
        private set => SetField(ref _incomingNotePreviews, value);
    }

    public IReadOnlyList<NotePreview> MergedNotePreviews
    {
        get => _mergedNotePreviews;
        private set => SetField(ref _mergedNotePreviews, value);
    }

    public async Task LoadBaseBackupAsync(string path, CancellationToken cancellationToken = default)
    {
        StatusMessage = _localizer.Strings.StatusLoadingBase;
        BaseBackupPath = path;

        try
        {
            _baseSnapshot = await _importer.ReadSnapshotAsync(path, cancellationToken);
            BaseSummaryText = BuildSummary(_baseSnapshot);
            BaseNotePreviews = BuildNotePreviews(_baseSnapshot);
            StatusMessage = _localizer.Strings.StatusBaseLoaded;
            RefreshMergePreview();
        }
        catch (Exception ex)
        {
            _baseSnapshot = null;
            BaseSummaryText = string.Empty;
            BaseNotePreviews = Array.Empty<NotePreview>();
            StatusMessage = string.Format(CultureInfo.CurrentCulture, _localizer.Strings.StatusFailedLoadBase, ex.Message);
        }
    }

    public async Task LoadIncomingBackupAsync(string path, CancellationToken cancellationToken = default)
    {
        StatusMessage = _localizer.Strings.StatusLoadingIncoming;
        IncomingBackupPath = path;

        try
        {
            _incomingSnapshot = await _importer.ReadSnapshotAsync(path, cancellationToken);
            IncomingSummaryText = BuildSummary(_incomingSnapshot);
            IncomingNotePreviews = BuildNotePreviews(_incomingSnapshot);
            StatusMessage = _localizer.Strings.StatusIncomingLoaded;
            RefreshMergePreview();
        }
        catch (Exception ex)
        {
            _incomingSnapshot = null;
            IncomingSummaryText = string.Empty;
            IncomingNotePreviews = Array.Empty<NotePreview>();
            StatusMessage = string.Format(CultureInfo.CurrentCulture, _localizer.Strings.StatusFailedLoadIncoming, ex.Message);
        }
    }

    public async Task MergeAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        if (_baseSnapshot is null || _incomingSnapshot is null)
        {
            StatusMessage = _localizer.Strings.StatusNeedBoth;
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            StatusMessage = _localizer.Strings.StatusChooseOutput;
            return;
        }

        StatusMessage = _localizer.Strings.StatusMerging;

        try
        {
            var result = await _merger.MergeAsync(BaseBackupPath, IncomingBackupPath, outputPath, cancellationToken);
            _mergedSnapshot = result.Snapshot;
            _mergeReport = result.Report;
            MergedOutputPath = result.OutputPath;
            MergedSummaryText = BuildSummary(result.Snapshot);
            MergedNotePreviews = BuildNotePreviews(result.Snapshot);
            MergeReportText = BuildMergeReport(result.Report);
            StatusMessage = _localizer.Strings.StatusMerged;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, _localizer.Strings.StatusFailedMerge, ex.Message);
        }
    }

    private void RefreshMergePreview()
    {
        if (_baseSnapshot is null || _incomingSnapshot is null)
        {
            MergeReportText = _localizer.Strings.LoadBothToPreview;
            return;
        }

        var preview = new MergeReport(
            _baseSnapshot.Notes.Count,
            _incomingSnapshot.Notes.Count,
            _baseSnapshot.Notes.Count + _incomingSnapshot.Notes.Count,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
        MergeReportText = BuildMergeReport(preview);
    }

    public string OpenBasePickerTitle => _localizer.Strings.OpenBasePickerTitle;

    public string OpenIncomingPickerTitle => _localizer.Strings.OpenIncomingPickerTitle;

    public string SaveMergedPickerTitle => _localizer.Strings.SaveMergedPickerTitle;

    public string BackupFileTypeName => _localizer.Strings.BackupFileTypeName;

    public string NoBaseLoadedText => _localizer.Strings.NoBaseLoaded;

    public string NoIncomingLoadedText => _localizer.Strings.NoIncomingLoaded;

    public string NoMergedBackupText => _localizer.Strings.NoMergedBackup;

    public string LoadBothToPreviewText => _localizer.Strings.LoadBothToPreview;

    private void LocalizerOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocalizationService.Strings) or nameof(LocalizationService.SelectedLanguage))
        {
            RaiseLocalizedProperties();
        }
    }

    private void RaiseLocalizedProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppSubtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppCaption)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageChoices)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguageChoice)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenBaseButtonText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenIncomingButtonText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MergeButtonText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkspaceTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BaseBackupTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncomingBackupTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MergeResultTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenBasePickerTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenIncomingPickerTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveMergedPickerTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackupFileTypeName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoBaseLoadedText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoIncomingLoadedText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoMergedBackupText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadBothToPreviewText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));

        if (_baseSnapshot is null)
        {
            BaseSummaryText = NoBaseLoadedText;
        }
        else
        {
            BaseSummaryText = BuildSummary(_baseSnapshot);
        }

        if (_incomingSnapshot is null)
        {
            IncomingSummaryText = NoIncomingLoadedText;
        }
        else
        {
            IncomingSummaryText = BuildSummary(_incomingSnapshot);
        }

        if (_mergedSnapshot is null)
        {
            MergedSummaryText = NoMergedBackupText;
        }
        else
        {
            MergedSummaryText = BuildSummary(_mergedSnapshot);
        }

        if (_mergeReport is null)
        {
            MergeReportText = _baseSnapshot is null || _incomingSnapshot is null
                ? LoadBothToPreviewText
                : BuildMergeReport(new MergeReport(
                    _baseSnapshot.Notes.Count,
                    _incomingSnapshot.Notes.Count,
                    _baseSnapshot.Notes.Count + _incomingSnapshot.Notes.Count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0));
        }
        else
        {
            MergeReportText = BuildMergeReport(_mergeReport);
        }

        if (_baseSnapshot is not null)
        {
            BaseNotePreviews = BuildNotePreviews(_baseSnapshot);
        }

        if (_incomingSnapshot is not null)
        {
            IncomingNotePreviews = BuildNotePreviews(_incomingSnapshot);
        }

        if (_mergedSnapshot is not null)
        {
            MergedNotePreviews = BuildNotePreviews(_mergedSnapshot);
        }
    }

    private IReadOnlyList<NotePreview> BuildNotePreviews(BackupSnapshot snapshot)
        => snapshot.Notes
            .OrderByDescending(n => n.LastModified)
            .Take(8)
            .Select(n => new NotePreview(
                n.NoteId,
                string.IsNullOrWhiteSpace(n.Title) ? _localizer.Strings.UntitledNote : n.Title!,
                Truncate(string.IsNullOrWhiteSpace(n.Content) ? _localizer.Strings.EmptyContent : n.Content!, 180)))
            .ToArray();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private string BuildSummary(BackupSnapshot snapshot)
    {
        var package = snapshot.Package;
        var strings = _localizer.Strings;
        return
            $"{strings.SummaryManifest}\n" +
            $"- Name: {package.Name}\n" +
            $"- Schema: {package.SchemaVersion}\n" +
            $"- Database: {package.DatabaseName}\n" +
            $"- {strings.SummaryDatabaseHash}: {(package.DatabaseHashMatchesManifest ? strings.SummaryDatabaseHashVerified : strings.SummaryDatabaseHashMismatch)}\n" +
            $"\n{strings.SummaryCounts}\n" +
            $"- {strings.SummaryLocations}: {snapshot.Locations.Count}\n" +
            $"- {strings.SummaryUserMarks}: {snapshot.UserMarks.Count}\n" +
            $"- {strings.SummaryNotes}: {snapshot.Notes.Count}\n" +
            $"- {strings.SummaryBlockRanges}: {snapshot.BlockRanges.Count}\n" +
            $"- {strings.SummaryTags}: {snapshot.Tags.Count}\n" +
            $"- {strings.SummaryTagMaps}: {snapshot.TagMaps.Count}\n" +
            $"- {strings.SummaryBookmarks}: {snapshot.Bookmarks.Count}\n" +
            $"- {strings.SummaryMediaAssets}: {snapshot.MediaAssets.Count}\n" +
            $"- {strings.SummaryInputFields}: {snapshot.InputFields.Count}";
    }

    private string BuildMergeReport(MergeReport report)
    {
        var strings = _localizer.Strings;
        return
            $"{strings.MergeReport}\n" +
            $"- {strings.MergeBaseNotes}: {report.BaseNotes}\n" +
            $"- {strings.MergeIncomingNotes}: {report.IncomingNotes}\n" +
            $"- {strings.MergeMergedNotes}: {report.MergedNotes}\n" +
            $"\n{strings.MergeAdded}\n" +
            $"- {strings.MergeAddedLocations}: {report.AddedLocations}\n" +
            $"- {strings.MergeAddedUserMarks}: {report.AddedUserMarks}\n" +
            $"- {strings.MergeAddedNotes}: {report.AddedNotes}\n" +
            $"- {strings.MergeUpdatedNotes}: {report.UpdatedNotes}\n" +
            $"- {strings.MergeAddedBlockRanges}: {report.AddedBlockRanges}\n" +
            $"- {strings.MergeAddedTags}: {report.AddedTags}\n" +
            $"- {strings.MergeAddedTagMaps}: {report.AddedTagMaps}\n" +
            $"- {strings.MergeAddedBookmarks}: {report.AddedBookmarks}\n" +
            $"- {strings.MergeAddedMediaAssets}: {report.AddedMediaAssets}\n" +
            $"- {strings.MergeAddedInputFields}: {report.AddedInputFields}\n" +
            $"- {strings.MergeSkippedPlaylistTagMaps}: {report.SkippedPlaylistTagMaps}";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record NotePreview(long NoteId, string Title, string Content);
