using GitTool.Core.Models;

namespace GitTool.Core.Git;

public static class GitHistoryGraphBuilder
{
    private const double LaneSpacing = 20;
    private const double GraphPadding = 24;

    public static IReadOnlyList<GitHistoryGraphRow> Build(
        IReadOnlyList<GitCommitInfo> commits)
    {
        var activeLanes = new List<string?>();
        var rows = new List<GitHistoryGraphRow>(commits.Count);
        var maximumLaneCount = 1;

        foreach (var commit in commits)
        {
            var nodeLane = activeLanes.FindIndex(hash =>
                hash?.Equals(commit.Hash, StringComparison.OrdinalIgnoreCase) == true);
            var hasIncomingEdge = nodeLane >= 0;

            if (nodeLane < 0)
            {
                nodeLane = activeLanes.FindIndex(hash => hash is null);
                if (nodeLane < 0)
                {
                    nodeLane = activeLanes.Count;
                    activeLanes.Add(null);
                }
            }

            var continuingLanes = activeLanes
                .Select((hash, lane) => (hash, lane))
                .Where(item => item.hash is not null && item.lane != nodeLane)
                .Select(item => item.lane)
                .ToArray();

            var nextLanes = activeLanes.ToList();
            nextLanes[nodeLane] = null;
            var parentLanes = new List<int>(commit.ParentHashes.Count);

            for (var parentIndex = 0; parentIndex < commit.ParentHashes.Count; parentIndex++)
            {
                var parentHash = commit.ParentHashes[parentIndex];
                var parentLane = nextLanes.FindIndex(hash =>
                    hash?.Equals(parentHash, StringComparison.OrdinalIgnoreCase) == true);

                if (parentLane < 0)
                {
                    parentLane = parentIndex == 0 && nextLanes[nodeLane] is null
                        ? nodeLane
                        : nextLanes.FindIndex(hash => hash is null);

                    if (parentLane < 0)
                    {
                        parentLane = nextLanes.Count;
                        nextLanes.Add(null);
                    }

                    nextLanes[parentLane] = parentHash;
                }

                parentLanes.Add(parentLane);
            }

            while (nextLanes.Count > 0 && nextLanes[^1] is null)
            {
                nextLanes.RemoveAt(nextLanes.Count - 1);
            }

            var laneCount = Math.Max(
                nodeLane + 1,
                Math.Max(activeLanes.Count, nextLanes.Count));
            maximumLaneCount = Math.Max(maximumLaneCount, laneCount);
            rows.Add(new GitHistoryGraphRow(
                commit,
                nodeLane,
                hasIncomingEdge,
                continuingLanes,
                parentLanes,
                laneCount,
                0));

            activeLanes = nextLanes;
        }

        var graphWidth = GraphPadding + (maximumLaneCount * LaneSpacing);
        return rows
            .Select(row => row with { GraphWidth = graphWidth })
            .ToArray();
    }
}
