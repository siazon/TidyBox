using TidyBox.Modules.DiskAnalyzer;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.App.Views;

/// <summary>One line of the tidy list. It starts checked or not as the proposal says; a file that stays where it is cannot be checked at all.</summary>
public sealed class ProposalRow
{
    public ProposalRow(Proposal proposal, string? sourceDomain)
    {
        Proposal = proposal;
        IsChecked = proposal.DefaultChecked;
        SourceDomain = sourceDomain;
    }

    public Proposal Proposal { get; }

    public string? SourceDomain { get; }

    public bool IsChecked { get; set; }

    public bool CanCheck => Proposal.Kind != ProposalKind.Untouched;

    public string Name => Proposal.File.Name;

    public string Detail
    {
        get
        {
            var parts = new List<string> { ByteSizeFormatter.Format(Proposal.File.SizeBytes) };
            if (SourceDomain is not null) parts.Add($"来源 {SourceDomain}");
            parts.Add(Proposal.Reason);
            return string.Join(" · ", parts);
        }
    }
}

public sealed record ProposalGroup(string Title, IReadOnlyList<ProposalRow> Rows);
