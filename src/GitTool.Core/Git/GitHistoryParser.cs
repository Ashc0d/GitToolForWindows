using System.Globalization;
using GitTool.Core.Models;

namespace GitTool.Core.Git;

public static class GitHistoryParser
{
    internal const char FieldSeparator = '\u001f';
    internal const char RecordSeparator = '\u001e';

    public static IReadOnlyList<GitCommitInfo> ParseHistory(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var commits = new List<GitCommitInfo>();
        foreach (var record in output.Split(
                     RecordSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedRecord = record.Trim('\r', '\n');
            if (string.IsNullOrWhiteSpace(normalizedRecord))
            {
                continue;
            }

            commits.Add(ParseCommit(normalizedRecord, expectedFieldCount: 8));
        }

        return commits;
    }

    public static GitCommitDetails ParseDetails(string output)
    {
        var metadataEnd = output.IndexOf(RecordSeparator);
        if (metadataEnd < 0)
        {
            throw new FormatException("Git did not return the expected commit-details separator.");
        }

        var metadata = output[..metadataEnd].Trim('\r', '\n');
        var fields = metadata.Split(FieldSeparator, 9);
        if (fields.Length != 9)
        {
            throw new FormatException("Git returned incomplete commit details.");
        }

        var commit = ParseCommit(
            string.Join(FieldSeparator, fields.Take(8)),
            expectedFieldCount: 8);
        var message = fields[8].Trim();
        var files = ParseFileChanges(output[(metadataEnd + 1)..]);

        return new GitCommitDetails(
            commit,
            message,
            files,
            files.Sum(file => file.Additions ?? 0),
            files.Sum(file => file.Deletions ?? 0));
    }

    public static IReadOnlyDictionary<string, CommitSignatureStatus>
        ParseSignaturePresenceBatch(
            string output,
            IReadOnlyList<string> commitHashes)
    {
        var normalizedOutput = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        var statuses = new Dictionary<string, CommitSignatureStatus>(
            StringComparer.OrdinalIgnoreCase);
        var searchStart = 0;

        for (var index = 0; index < commitHashes.Count; index++)
        {
            var hash = commitHashes[index];
            var objectMarker = $"{hash} commit ";
            var objectStart = FindLineStart(
                normalizedOutput,
                objectMarker,
                searchStart);
            if (objectStart < 0)
            {
                throw new FormatException(
                    $"Git did not return commit object '{hash}'.");
            }

            var contentStart = normalizedOutput.IndexOf('\n', objectStart);
            if (contentStart < 0)
            {
                throw new FormatException(
                    $"Git returned an incomplete object header for '{hash}'.");
            }

            contentStart++;
            var objectEnd = normalizedOutput.Length;
            if (index + 1 < commitHashes.Count)
            {
                objectEnd = FindLineStart(
                    normalizedOutput,
                    $"{commitHashes[index + 1]} commit ",
                    contentStart);
                if (objectEnd < 0)
                {
                    throw new FormatException(
                        $"Git did not return commit object '{commitHashes[index + 1]}'.");
                }
            }

            var objectContent = normalizedOutput[contentStart..objectEnd];
            var commitHeaderEnd = objectContent.IndexOf(
                "\n\n",
                StringComparison.Ordinal);
            if (commitHeaderEnd < 0)
            {
                throw new FormatException(
                    $"Git returned incomplete commit headers for '{hash}'.");
            }

            var isSigned = objectContent[..commitHeaderEnd]
                .Split('\n')
                .Any(line =>
                    line.StartsWith("gpgsig ", StringComparison.Ordinal)
                    || line.StartsWith(
                        "gpgsig-sha256 ",
                        StringComparison.Ordinal));
            statuses[hash] = isSigned
                ? CommitSignatureStatus.Signed
                : CommitSignatureStatus.Unsigned;
            searchStart = objectEnd;
        }

        return statuses;
    }

    private static GitCommitInfo ParseCommit(string value, int expectedFieldCount)
    {
        var fields = value.Split(FieldSeparator, expectedFieldCount);
        if (fields.Length != expectedFieldCount)
        {
            throw new FormatException("Git returned an incomplete history record.");
        }

        if (!DateTimeOffset.TryParse(
                fields[5],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var authorDate))
        {
            throw new FormatException($"Git returned an invalid commit date '{fields[5]}'.");
        }

        var parents = fields[2]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var references = fields[6]
            .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new GitCommitInfo(
            fields[0],
            fields[1],
            parents,
            fields[3],
            fields[4],
            authorDate,
            references,
            fields[7]);
    }

    private static IReadOnlyList<GitCommitFileChange> ParseFileChanges(string output)
    {
        var files = new List<GitCommitFileChange>();
        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 3)
            {
                continue;
            }

            files.Add(new GitCommitFileChange(
                string.Join('\t', fields.Skip(2)),
                ParseLineCount(fields[0]),
                ParseLineCount(fields[1])));
        }

        return files;
    }

    private static int? ParseLineCount(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;

    private static int FindLineStart(
        string value,
        string marker,
        int startIndex)
    {
        var markerIndex = startIndex;
        while ((markerIndex = value.IndexOf(
                   marker,
                   markerIndex,
                   StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (markerIndex == 0 || value[markerIndex - 1] == '\n')
            {
                return markerIndex;
            }

            markerIndex += marker.Length;
        }

        return -1;
    }
}
