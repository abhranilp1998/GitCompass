using System.Collections.Immutable;
using GitCompass.Domain;

namespace GitCompass.Infrastructure.Git;

public sealed record ParsedStatus(string? HeadId, string? Branch, BranchState BranchState, string? Upstream, int? Ahead, int? Behind, ImmutableArray<FileState> Files);

/// <summary>Parses NUL-delimited porcelain v2. The path is the unsplit remainder of each typed record.</summary>
public static class PorcelainV2Parser
{
    public static ParsedStatus Parse(string payload)
    {
        if (payload.Length != 0 && !payload.EndsWith('\0')) throw new FormatException("Incomplete status output: missing NUL terminator.");
        var records = payload.Split('\0');
        var files = ImmutableArray.CreateBuilder<FileState>();
        string? head = null, branch = null, upstream = null; int? ahead = null, behind = null; var state = BranchState.Unknown;
        for (var i = 0; i < records.Length - 1; i++)
        {
            var line = records[i];
            if (line.Length == 0) throw new FormatException("Empty status record.");
            if (line.StartsWith("# "))
            {
                if (line.StartsWith("# branch.oid ")) { head = line[13..]; if (head == "(initial)") { head = null; state = BranchState.Unborn; } }
                else if (line.StartsWith("# branch.head ")) { branch = line[14..]; if (branch == "(detached)") { branch = null; state = BranchState.Detached; } else if (state != BranchState.Unborn) state = BranchState.Attached; }
                else if (line.StartsWith("# branch.upstream ")) upstream = line[18..];
                else if (line.StartsWith("# branch.ab "))
                {
                    var counts = line[12..].Split(' ');
                    if (counts.Length != 2 || !counts[0].StartsWith('+') || !counts[1].StartsWith('-') || !int.TryParse(counts[0][1..], out var a) || !int.TryParse(counts[1][1..], out var b) || a < 0 || b < 0) throw new FormatException("Invalid branch counts.");
                    ahead = a; behind = b;
                }
                continue; // Git permits new branch headers in later versions.
            }
            switch (line[0])
            {
                case '1':
                {
                    var p = Fields(line, 8); ValidateXY(p[1]);
                    files.Add(new(p[8], null, p[1][0], p[1][1], FileKind.Tracked, p[2])); break;
                }
                case '2':
                {
                    var p = Fields(line, 9); ValidateXY(p[1]);
                    if (++i >= records.Length - 1 || records[i].Length == 0) throw new FormatException("Rename record has no original path.");
                    files.Add(new(p[9], records[i], p[1][0], p[1][1], FileKind.Tracked, p[2])); break;
                }
                case 'u':
                {
                    var p = Fields(line, 10); ValidateXY(p[1]);
                    files.Add(new(p[10], null, p[1][0], p[1][1], FileKind.Conflicted, p[2])); break;
                }
                case '?': case '!':
                    if (line.Length < 3 || line[1] != ' ') throw new FormatException("Invalid untracked path record.");
                    files.Add(new(line[2..], null, line[0], line[0], line[0] == '?' ? FileKind.Untracked : FileKind.Ignored)); break;
                default: throw new FormatException($"Unsupported porcelain record type: {line[0]}.");
            }
        }
        return new(head, branch, state, upstream, ahead, behind, files.ToImmutable());
    }
    private static string[] Fields(string line, int separators)
    {
        var fields = new string[separators + 1]; var start = 0;
        for (var i = 0; i < separators; i++) { var end = line.IndexOf(' ', start); if (end < 0 || end == start) throw new FormatException("Incomplete porcelain record."); fields[i] = line[start..end]; start = end + 1; }
        if (start >= line.Length) throw new FormatException("Missing status path."); fields[separators] = line[start..]; return fields;
    }
    private static void ValidateXY(string xy) { if (xy.Length != 2 || xy.Any(c => !".MADRCUT".Contains(c))) throw new FormatException("Invalid index/worktree status."); }
}
