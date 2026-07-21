using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VibeHub.Core.Distill;
using VibeHub.Core.Vault;

namespace VibeHub.App;

public partial class ReviewWindow : Window
{
    private readonly Distiller _distiller;
    private readonly Harvester _harvester;
    public ObservableCollection<Row> Items { get; } = new();

    public ReviewWindow(Distiller distiller, Harvester harvester)
    {
        InitializeComponent();
        _distiller = distiller;
        _harvester = harvester;
        ArtifactList.ItemsSource = Items;
        Refresh();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => Refresh();
    private void Filter_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    private void Refresh()
    {
        Items.Clear();
        var pendingOnly = PendingOnlyBox.IsChecked == true;
        foreach (var a in _distiller.Queue.List())
        {
            if (pendingOnly && a.Status != ReviewStatus.Pending) continue;
            Items.Add(new Row(a));
        }

        MetaText.Text = Items.Count == 0 ? "队列为空" : $"{Items.Count} 条";
    }

    private void ArtifactList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArtifactList.SelectedItem is not Row row)
        {
            ContentBox.Text = "";
            return;
        }

        MetaText.Text =
            $"{row.Artifact.Status} · {row.Artifact.Kind} · {row.Artifact.ProjectId}/{row.Artifact.SessionId} · {row.Artifact.Id[..8]}…";
        ContentBox.Text = row.Artifact.Content;
    }

    private void Approve_OnClick(object sender, RoutedEventArgs e)
    {
        if (ArtifactList.SelectedItem is not Row row) return;
        _distiller.Queue.UpdateContent(row.Artifact.Id, ContentBox.Text);
        _distiller.Queue.Decide(row.Artifact.Id, approve: true);
        if (!_distiller.ApplyApproved(row.Artifact.Id, _harvester))
        {
            MessageBox.Show("批准失败：状态或 Kind 不匹配", "审阅");
            return;
        }

        MessageBox.Show("已写入 vault summary.md", "审阅");
        Refresh();
    }

    private void Reject_OnClick(object sender, RoutedEventArgs e)
    {
        if (ArtifactList.SelectedItem is not Row row) return;
        _distiller.Queue.Decide(row.Artifact.Id, approve: false);
        Refresh();
    }

    public sealed class Row
    {
        public DistillArtifact Artifact { get; }
        public string Display => $"{Artifact.Status} · {Artifact.SessionId} · {Artifact.Id[..8]}";
        public Row(DistillArtifact a) => Artifact = a;
    }
}
