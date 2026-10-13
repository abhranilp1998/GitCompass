using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using GitCompass.Domain;

namespace GitCompass.Desktop;

/// <summary>Draws only recorded parent relationships. The adjoining commit list provides
/// keyboard navigation and a textual equivalent of every node and relationship.</summary>
public sealed class CommitGraph : FrameworkElement
{
    public const double RowHeight = 46;
    private readonly ImmutableArray<CommitNode> commits;
    private readonly Dictionary<string, int> index;
    private readonly Dictionary<string, int> lanes = [];
    private readonly Brush[] colors = SystemParameters.HighContrast ? [SystemColors.HighlightBrush, SystemColors.WindowTextBrush] : [new SolidColorBrush(Color.FromRgb(39, 135, 235)), new SolidColorBrush(Color.FromRgb(137, 96, 210)), new SolidColorBrush(Color.FromRgb(18, 150, 134)), new SolidColorBrush(Color.FromRgb(190, 112, 20))];
    public CommitGraph(ImmutableArray<CommitNode> commits)
    {
        this.commits = commits;
        index = commits.Select((c, i) => (c.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var active = new List<string?>();
        foreach (var commit in commits)
        {
            var lane = active.IndexOf(commit.Id);
            if (lane < 0) { lane = active.IndexOf(null); if (lane < 0) { lane = active.Count; active.Add(null); } }
            lanes[commit.Id] = lane;
            active[lane] = null;
            for (var p = 0; p < commit.Parents.Length; p++)
            {
                var parent = commit.Parents[p];
                if (active.Contains(parent)) continue;
                var parentLane = p == 0 && active[lane] is null ? lane : active.IndexOf(null);
                if (parentLane < 0) { parentLane = active.Count; active.Add(null); }
                active[parentLane] = parent;
                lanes.TryAdd(parent, parentLane);
            }
        }
        Width = Math.Max(90, (lanes.Values.DefaultIfEmpty(0).Max() + 1) * 22 + 30);
        Height = Math.Max(RowHeight, commits.Length * RowHeight);
        AutomationProperties.SetName(this, "Commit parent graph. Use the adjacent commit list for accessible relationship details.");
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        foreach (var commit in commits)
        {
            var row = index[commit.Id]; var lane = lanes[commit.Id]; var from = new Point(18 + lane * 22, row * RowHeight + RowHeight / 2);
            foreach (var parent in commit.Parents)
            {
                var parentRow = index.GetValueOrDefault(parent, commits.Length); var parentLane = lanes.GetValueOrDefault(parent, lane);
                var target = new Point(18 + parentLane * 22, parentRow * RowHeight + (parentRow == commits.Length ? 0 : RowHeight / 2));
                var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(from, false, false); context.BezierTo(new Point(from.X, from.Y + 23), new Point(target.X, target.Y - 23), target, true, false); }
                var pen = new Pen(colors[lane % colors.Length], 2.4);
                if (parentRow == commits.Length) pen.DashStyle = DashStyles.Dot;
                drawing.DrawGeometry(null, pen, geometry);
            }
        }
        foreach (var commit in commits)
        {
            var point = new Point(18 + lanes[commit.Id] * 22, index[commit.Id] * RowHeight + RowHeight / 2);
            drawing.DrawEllipse(colors[lanes[commit.Id] % colors.Length], new Pen(SystemParameters.HighContrast ? SystemColors.WindowBrush : Brushes.White, 1.5), point, 6.5, 6.5);
        }
    }
}
