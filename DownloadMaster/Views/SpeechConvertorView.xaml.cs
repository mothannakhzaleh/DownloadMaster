using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DownloadMaster.Models;
using DownloadMaster.Services;
using Microsoft.Win32;

namespace DownloadMaster.Views;

public partial class SpeechConvertorView : UserControl
{
    private readonly SpeechSynthesisService _speechService = new();
    private readonly ObservableCollection<ConversionResultItem> _speechResults = [];
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _exportCancellation;
    private LocalizationService _loc = new();

    public SpeechConvertorView()
    {
        InitializeComponent();
        Unloaded += SpeechConvertorView_Unloaded;
        SpeechResultsList.ItemsSource = _speechResults;
        SpeechOutputFolderBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        PopulateVoices();
        UpdateSpeechFormatHint();
        WireUiEvents();
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        SpeechFormatLabel.Text = loc.Get("SpeechConvertExportFormat");
        SpeechTextTitle.Text = loc.Get("SpeechConvertTextTitle");
        SpeechTextHint.Text = loc.Get("SpeechConvertTextHint");
        SpeechVoiceTitle.Text = loc.Get("SpeechConvertVoiceTitle");
        SpeechVoiceLabel.Text = loc.Get("SpeechConvertVoice");
        SpeechRateLabel.Text = loc.Get("SpeechConvertSpeed");
        SpeechVolumeLabel.Text = loc.Get("SpeechConvertVolume");
        SpeechPreviewButton.Content = loc.Get("SpeechConvertPreview");
        SpeechStopButton.Content = loc.Get("SpeechConvertStop");
        SpeechOutputTitle.Text = loc.Get("SpeechConvertOutput");
        SpeechFileNameLabel.Text = loc.Get("SpeechConvertFileName");
        SpeechSaveToLabel.Text = loc.Get("SpeechConvertSaveFolder");
        SpeechBrowseOutputButton.Content = loc.Get("Browse");
        SpeechInstallHint.Text = loc.Get("SpeechConvertInstallHint");
        SpeechResultsTitle.Text = loc.Get("SpeechConvertResult");
        SpeechClearResultsButton.Content = loc.Get("ClearList");
        SpeechExportButton.Content = loc.Get("SpeechConvertButton");
        SpeechResultStatusCol.Header = loc.Get("ResultColStatus");
        SpeechResultLabelCol.Header = loc.Get("ResultColLabel");
        SpeechResultSizeCol.Header = loc.Get("ResultColOutputSize");
        SpeechResultDetailsCol.Header = loc.Get("ResultColDetails");
        SpeechResultsSummaryText.Text = loc.Get("SpeechConvertNoResult");
        SpeechStatusText.Text = loc.Get("SpeechConvertReady");
        UpdateSpeechFormatHint();
        UpdateCharacterCount();
    }

    private void WireUiEvents()
    {
        SpeechWavRadio.Checked += SpeechFormat_Changed;
        SpeechMp3Radio.Checked += SpeechFormat_Changed;
        SpeechM4aRadio.Checked += SpeechFormat_Changed;
    }

    private void SpeechConvertorView_Unloaded(object sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel();
        _exportCancellation?.Cancel();
        _speechService.StopPreview();
    }

    private void PopulateVoices()
    {
        var voices = _speechService.GetInstalledVoices();
        SpeechVoiceCombo.ItemsSource = voices;

        if (voices.Count > 0)
        {
            SpeechVoiceCombo.SelectedIndex = 0;
            UpdateSpeechFormatHint();
            return;
        }

        SpeechVoiceStatus.Text = _loc.Get("SpeechConvertNoVoices");
        SpeechPreviewButton.IsEnabled = false;
        SpeechExportButton.IsEnabled = false;
    }

    private void SpeechFormat_Changed(object sender, RoutedEventArgs e) => UpdateSpeechFormatHint();

    private void UpdateSpeechFormatHint()
    {
        var voiceLine = GetVoiceStatusLine();
        if (GetSelectedOutputFormat() == SpeechOutputFormat.Wav)
            SpeechVoiceStatus.Text = voiceLine + " " + _loc.Get("SpeechConvertWavHint");
        else
            SpeechVoiceStatus.Text = voiceLine + " " + _loc.Get("SpeechConvertFfmpegHint");
    }

    private string GetVoiceStatusLine()
    {
        var count = SpeechVoiceCombo.Items.Count;
        return count > 0
            ? string.Format(_loc.Get("SpeechConvertVoicesAvailable"), count)
            : _loc.Get("SpeechConvertNoVoicesShort");
    }

    private void SpeechTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCharacterCount();

    private void UpdateCharacterCount()
    {
        var length = SpeechTextBox.Text.Length;
        SpeechCharacterCount.Text = length == 1
            ? _loc.Get("SpeechConvertOneCharacter")
            : string.Format(_loc.Get("SpeechConvertCharacterCount"), length);
    }

    private void BrowseSpeechOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _loc.Get("SavePath"),
            InitialDirectory = SpeechOutputFolderBox.Text
        };

        if (dialog.ShowDialog() == true)
            SpeechOutputFolderBox.Text = dialog.FolderName;
    }

    private void PreviewSpeech_Click(object sender, RoutedEventArgs e) => _ = PreviewSpeechAsync();

    private async Task PreviewSpeechAsync()
    {
        if (!TryBuildPreviewSettings(out var text, out var voiceName, out var rate, out var volume, out var error))
        {
            MessageBox.Show(error, _loc.Get("TabSpeechConvert"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation = new CancellationTokenSource();

        SetPreviewBusy(true);
        SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewSpeaking");

        try
        {
            await _speechService.PreviewAsync(
                text,
                voiceName,
                rate,
                volume,
                _previewCancellation.Token);

            SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewDone");
        }
        catch (OperationCanceledException)
        {
            SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewStopped");
        }
        catch (Exception ex)
        {
            SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewFailed");
            MessageBox.Show(ex.Message, _loc.Get("SpeechConvertPreviewFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPreviewBusy(false);
        }
    }

    private void StopSpeech_Click(object sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel();
        _speechService.StopPreview();
        SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewStopped");
        SetPreviewBusy(false);
    }

    private void ExportSpeech_Click(object sender, RoutedEventArgs e) => _ = ExportSpeechAsync();

    private async Task ExportSpeechAsync()
    {
        if (!TryBuildExportSettings(out var settings, out var error))
        {
            MessageBox.Show(error, _loc.Get("TabSpeechConvert"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _exportCancellation?.Cancel();
        _exportCancellation = new CancellationTokenSource();

        var label = BuildResultLabel(settings.Text);
        _speechResults.Clear();
        _speechResults.Add(new ConversionResultItem
        {
            SourcePath = label,
            FileName = label,
            Status = "Exporting",
            SizeChange = "—",
            Detail = _loc.Get("SpeechConvertGenerating"),
            IsSuccess = false
        });

        SetExportBusy(true);
        SpeechResultsSummaryText.Text = string.Format(_loc.Get("SpeechConvertExporting"), GetFormatLabel(settings.OutputFormat));
        SpeechStatusText.Text = _loc.Get("SpeechConvertExportingStatus");

        try
        {
            var progress = new Progress<double>(percent =>
            {
                SpeechProgressBar.Value = percent;
                SpeechResultsProgressBar.Value = percent;
                SpeechStatusText.Text = $"{_loc.Get("SpeechConvertExportingStatus")} {percent:F0}%";
            });

            var result = await _speechService.ExportAsync(
                settings,
                progress,
                _exportCancellation.Token);

            SetSpeechResult(ConversionResultItem.FromSpeechResult(result, label));
            SpeechStatusText.Text = _loc.Get("SpeechConvertDone");
            SpeechResultsSummaryText.Text = string.Format(_loc.Get("SpeechConvertExported"), Path.GetFileName(result.OutputPath));

            var prompt = MessageBox.Show(
                _loc.Get("SpeechConvertOpenFolderPrompt"),
                _loc.Get("SpeechConvertDone"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (prompt == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", settings.OutputFolder) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            SpeechStatusText.Text = _loc.Get("SpeechConvertCancelled");
            SpeechResultsSummaryText.Text = _loc.Get("SpeechConvertCancelled");
        }
        catch (Exception ex)
        {
            SpeechStatusText.Text = _loc.Get("SpeechConvertFailed");
            SpeechResultsSummaryText.Text = _loc.Get("SpeechConvertFailed");
            SetSpeechResult(new ConversionResultItem
            {
                SourcePath = label,
                FileName = label,
                Status = "Failed",
                SizeChange = "—",
                Detail = ex.Message,
                IsSuccess = false
            });
            MessageBox.Show(ex.Message, _loc.Get("SpeechConvertFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetExportBusy(false);
        }
    }

    private bool TryBuildPreviewSettings(
        out string text,
        out string voiceName,
        out int rate,
        out int volume,
        out string error)
    {
        text = SpeechTextBox.Text.Trim();
        voiceName = GetSelectedVoiceName();
        rate = (int)SpeechRateSlider.Value;
        volume = (int)SpeechVolumeSlider.Value;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = _loc.Get("SpeechConvertEnterText");
            return false;
        }

        if (string.IsNullOrWhiteSpace(voiceName))
        {
            error = _loc.Get("SpeechConvertSelectVoice");
            return false;
        }

        return true;
    }

    private bool TryBuildExportSettings(out SpeechConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        if (!TryBuildPreviewSettings(out var text, out var voiceName, out var rate, out var volume, out error))
            return false;

        var outputFolder = SpeechOutputFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            error = _loc.Get("SpeechConvertSelectOutput");
            return false;
        }

        if (!Directory.Exists(outputFolder))
        {
            error = _loc.Get("SpeechConvertOutputMissing");
            return false;
        }

        var format = GetSelectedOutputFormat();
        if (format != SpeechOutputFormat.Wav && !FfmpegToolHelper.TryConfigure())
        {
            error = _loc.Get("SpeechConvertFfmpegMissing");
            return false;
        }

        settings = new SpeechConversionSettings
        {
            Text = text,
            VoiceName = voiceName,
            Rate = rate,
            Volume = volume,
            OutputFormat = format,
            OutputFolder = outputFolder,
            OutputBaseName = SpeechOutputNameBox.Text.Trim()
        };

        return true;
    }

    private string GetSelectedVoiceName()
    {
        if (SpeechVoiceCombo.SelectedItem is SpeechVoiceInfo voice)
            return voice.Name;

        return string.Empty;
    }

    private SpeechOutputFormat GetSelectedOutputFormat()
    {
        if (SpeechWavRadio.IsChecked == true)
            return SpeechOutputFormat.Wav;
        if (SpeechM4aRadio.IsChecked == true)
            return SpeechOutputFormat.M4a;

        return SpeechOutputFormat.Mp3;
    }

    private static string GetFormatLabel(SpeechOutputFormat format) => format switch
    {
        SpeechOutputFormat.Wav => "WAV",
        SpeechOutputFormat.M4a => "M4A",
        _ => "MP3"
    };

    private static string BuildResultLabel(string text)
    {
        var preview = text.Length <= 48 ? text : text[..45] + "...";
        return preview.Replace('\r', ' ').Replace('\n', ' ');
    }

    private void SetSpeechResult(ConversionResultItem item)
    {
        _speechResults.Clear();
        _speechResults.Add(item);
    }

    private void SetPreviewBusy(bool busy)
    {
        SpeechPreviewButton.IsEnabled = !busy;
        SpeechStopButton.IsEnabled = busy;
        SpeechExportButton.IsEnabled = !busy;
        SpeechPreviewButton.Content = busy ? _loc.Get("SpeechConvertPreviewSpeaking") : _loc.Get("SpeechConvertPreview");
    }

    private void SetExportBusy(bool busy)
    {
        SpeechExportButton.IsEnabled = !busy;
        SpeechPreviewButton.IsEnabled = !busy;
        SpeechExportButton.Content = busy ? _loc.Get("SpeechConvertExportingStatus") : _loc.Get("SpeechConvertButton");
        SpeechProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SpeechResultsProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (!busy)
        {
            SpeechProgressBar.Value = 0;
            SpeechResultsProgressBar.Value = 0;
        }
    }

    private void ClearSpeechResults_Click(object sender, RoutedEventArgs e)
    {
        _speechResults.Clear();
        SpeechStatusText.Text = _loc.Get("SpeechConvertReady");
        SpeechResultsSummaryText.Text = _loc.Get("SpeechConvertNoResult");
    }
}
