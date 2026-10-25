using PowerTools.Modules.Uninstaller.Residue;

namespace PowerTools.App.Views;

/// <summary>One leftover in the review list: a checkbox (for items that can be removed) plus what it is and why it was suggested.</summary>
public sealed class ResidueRow
{
    public ResidueRow(ResidueItem item)
    {
        Item = item;
        // Only the app's own install folder is pre-selected; name-only matches must be ticked on purpose.
        IsChecked = item.Removable && item.Confidence == ResidueConfidence.High;
    }

    public ResidueItem Item { get; }

    public bool? IsChecked { get; set; }

    public bool CanSelect => Item.Removable;

    public string Path => Item.Path;

    public string Detail
    {
        get
        {
            if (!Item.Removable) return "无法在这里删除，请用注册表编辑器手动处理";

            var confidence = Item.Confidence == ResidueConfidence.High ? "可信度高" : "仅名称匹配，请确认";
            var admin = Item.NeedsElevation ? " · 需要管理员权限" : "";
            return $"{Item.Reason} · {confidence}{admin}";
        }
    }
}
