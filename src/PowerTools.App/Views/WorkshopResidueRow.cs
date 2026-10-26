using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.App.Views;

/// <summary>
/// One candidate in the workshop-residue review list. Unchecked by default: some users intentionally keep
/// subscribed content around for a game they plan to reinstall.
/// </summary>
public sealed class WorkshopResidueRow
{
    public WorkshopResidueRow(WorkshopResidueItem item) => Item = item;

    public WorkshopResidueItem Item { get; }

    public bool IsChecked { get; set; }

    public string Name => Item.GameName ?? $"appid:{Item.AppId}";

    public string Detail => $"{ByteSizeFormatter.Format(Item.SizeBytes)} · 该游戏当前未安装 · 删除后如重装并重新订阅，需要重新下载";
}
