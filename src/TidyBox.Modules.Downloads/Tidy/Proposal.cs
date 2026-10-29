using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tidy;

public enum ProposalKind
{
    /// <summary>Move the file into its category folder.</summary>
    Move,

    /// <summary>The category folder already holds an identical file; suggest recycling this copy.</summary>
    RecycleDuplicate,

    /// <summary>No rule matches; the file stays where it is.</summary>
    Untouched,
}

/// <param name="DestinationPath">For <see cref="ProposalKind.Move"/>, where the file would go. For <see cref="ProposalKind.RecycleDuplicate"/>, the identical file already there. Null when untouched.</param>
/// <param name="Reason">A short sentence for the user.</param>
public sealed record Proposal(
    DownloadFile File, ProposalKind Kind, string? CategoryId, string? DestinationPath, string Reason, bool DefaultChecked, bool IsSuspicious);
