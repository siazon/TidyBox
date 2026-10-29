using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidyBox.Core.Feedback;
using TidyBox.Core.Logging;

namespace TidyBox.App.Feedback;

public sealed partial class FeedbackDialog : ContentDialog
{
    private readonly FeedbackClient _client;
    private readonly string _logs; // what would be attached: redacted first, then cut to the size limit

    public FeedbackDialog(FeedbackClient client)
    {
        _client = client;
        InitializeComponent();

        _logs = FeedbackLimits.TrimLogs(LogRedactor.RedactForSending(AppLog.Default.ReadRecent(FeedbackLimits.MaxLogBytes)));
        LogPreview.Text = _logs.Length == 0 ? "（暂无日志）" : _logs;
    }

    /// <summary>True when the feedback went through and the dialog closed because of it.</summary>
    public bool WasSent { get; private set; }

    private FeedbackKind SelectedKind =>
        KindCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<FeedbackKind>(tag, out var kind) ? kind : FeedbackKind.Suggestion;

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogPanel is null) return; // raised once while the XAML is still being built
        LogPanel.Visibility = SelectedKind == FeedbackKind.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMessageChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(MessageBox.Text);
    }

    private async void OnSendClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The dialog stays open (with everything the user typed) unless the feedback was sent.
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            SetBusy(true);
            var attachLogs = SelectedKind == FeedbackKind.Error && AttachLogsBox.IsChecked == true && _logs.Length > 0;
            var submission = new FeedbackSubmission(SelectedKind, MessageBox.Text, ContactBox.Text, AppVersion.Current, attachLogs ? _logs : null);

            var result = await _client.SendAsync(submission, CancellationToken.None);
            if (result.Outcome == FeedbackOutcome.Sent)
            {
                WasSent = true;
                args.Cancel = false;
                return;
            }

            StatusText.Text = result.Outcome switch
            {
                FeedbackOutcome.Rejected => "内容不符合要求，请检查后再发送。",
                FeedbackOutcome.RateLimited => "发送太频繁，请稍后再试。",
                _ => "无法连接到反馈服务器，请检查网络后重试。内容已保留。",
            };
            StatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            SetBusy(false);
            deferral.Complete();
        }
    }

    private void SetBusy(bool busy)
    {
        KindCombo.IsEnabled = !busy;
        MessageBox.IsEnabled = !busy;
        ContactBox.IsEnabled = !busy;
        AttachLogsBox.IsEnabled = !busy;
        IsPrimaryButtonEnabled = !busy && !string.IsNullOrWhiteSpace(MessageBox.Text);
        if (busy)
        {
            StatusText.Text = "正在发送…";
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
