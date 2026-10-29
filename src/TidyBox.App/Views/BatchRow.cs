using Microsoft.UI.Xaml;
using TidyBox.Modules.Downloads.Journal;

namespace TidyBox.App.Views;

/// <summary>One line of the history. Only moves can be undone here; recycled files are restored from the Recycle Bin, and the line says so.</summary>
public sealed class BatchRow
{
    public BatchRow(Batch batch) => Batch = batch;

    public Batch Batch { get; }

    public string Title
        => $"{Batch.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {(Batch.Kind == BatchKind.Move ? "整理" : "移到回收站")} · {Batch.Entries.Count} 项";

    public string Note => Batch.Kind == BatchKind.Recycle ? "这些文件在系统回收站里，请在回收站中还原。" : string.Empty;

    public Visibility UndoVisibility => Batch.Kind == BatchKind.Move ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoteVisibility => Batch.Kind == BatchKind.Recycle ? Visibility.Visible : Visibility.Collapsed;
}
