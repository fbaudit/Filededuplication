using FileDedup.Core.Dedup;
using FileDedup.Core.Scanning;

namespace FileDedup.Core.Tests;

public class SelectionRulesTests
{
    private static FileRecord Record(string path, DateTime modified) => new()
    {
        Entry = new FileEntry(path, Path.GetFileName(path), 100,
            DateTime.UtcNow, modified),
    };

    private static DuplicateGroup Group(params FileRecord[] files)
        => new() { Files = files };

    [Fact]
    public void 최신_보존_정책은_가장_최근_파일만_남긴다()
    {
        var oldFile = Record("/data/old.txt", new DateTime(2020, 1, 1));
        var newFile = Record("/data/new.txt", new DateTime(2025, 1, 1));

        var toRemove = SelectionRules.Apply([Group(oldFile, newFile)], KeepPolicy.Newest);

        Assert.Contains(oldFile, toRemove);
        Assert.DoesNotContain(newFile, toRemove);
    }

    [Fact]
    public void 짧은_경로_보존_정책()
    {
        var shortPath = Record("/a/f.txt", DateTime.UtcNow);
        var longPath = Record("/a/deep/nested/f.txt", DateTime.UtcNow);

        var toRemove = SelectionRules.Apply([Group(shortPath, longPath)], KeepPolicy.ShortestPath);

        Assert.Contains(longPath, toRemove);
        Assert.DoesNotContain(shortPath, toRemove);
    }

    [Fact]
    public void 폴더_우선_보존은_해당_폴더_파일을_남긴다()
    {
        var inside = Record("/keep/f.txt", new DateTime(2020, 1, 1));
        var outside = Record("/other/f.txt", new DateTime(2025, 1, 1));

        var toRemove = SelectionRules.Apply(
            [Group(inside, outside)], KeepPolicy.PreferFolder, "/keep");

        Assert.Contains(outside, toRemove);
        Assert.DoesNotContain(inside, toRemove);
    }

    [Fact]
    public void 어떤_정책이든_그룹당_1개는_반드시_보존된다()
    {
        var groups = new[]
        {
            Group(Record("/a/1.txt", DateTime.UtcNow), Record("/a/2.txt", DateTime.UtcNow),
                  Record("/a/3.txt", DateTime.UtcNow)),
            Group(Record("/b/1.txt", DateTime.UtcNow), Record("/b/2.txt", DateTime.UtcNow)),
        };

        foreach (var policy in Enum.GetValues<KeepPolicy>())
        {
            var toRemove = SelectionRules.Apply(groups, policy, "/nonexistent");
            foreach (var group in groups)
                Assert.True(group.Files.Count(f => !toRemove.Contains(f)) >= 1,
                    $"{policy}: 그룹 전체가 삭제 대상이 되면 안 됨");
        }
    }

    [Fact]
    public void 패턴_선택은_그룹_전체_매칭_시_1개를_보호한다()
    {
        var f1 = Record("/backup/a.tmp", new DateTime(2020, 1, 1));
        var f2 = Record("/backup/b.tmp", new DateTime(2025, 1, 1));

        var toRemove = SelectionRules.SelectByPattern([Group(f1, f2)], "*.tmp");

        Assert.Single(toRemove);
        Assert.Contains(f1, toRemove); // 최신(f2)이 보존됨
    }
}
