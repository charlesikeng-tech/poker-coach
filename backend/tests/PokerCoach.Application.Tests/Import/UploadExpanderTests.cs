using System.IO.Compression;
using System.Text;
using PokerCoach.Application.Import;

namespace PokerCoach.Application.Tests.Import;

public sealed class UploadExpanderTests
{
    private static readonly ImportOptions Options = new()
    {
        MaxFileBytes = 1_000,
        MaxFilesPerUpload = 3,
        MaxExpandedBytes = 2_500,
    };

    [Fact]
    public async Task Text_files_are_kept_and_other_types_rejected()
    {
        var result = await ExpandAsync(Text("hands.txt", "Winamax"), Text("notes.pdf", "x"));

        Assert.Equal("hands.txt", Assert.Single(result.Files).FileName);
        Assert.Equal(new RejectedUpload("notes.pdf", ImportErrorCodes.UnsupportedFileType), Assert.Single(result.Rejected));
    }

    [Fact]
    public async Task Empty_and_oversized_files_are_rejected()
    {
        var result = await ExpandAsync(Text("empty.txt", ""), Text("big.txt", new string('a', 1_001)));

        Assert.Empty(result.Files);
        Assert.Contains(new RejectedUpload("empty.txt", ImportErrorCodes.EmptyFile), result.Rejected);
        Assert.Contains(new RejectedUpload("big.txt", ImportErrorCodes.FileTooLarge), result.Rejected);
    }

    [Fact]
    public async Task Zip_entries_are_expanded_with_bare_names_and_archive_noise_skipped()
    {
        var zip = Zip(
            ("folder/a.txt", "A"),
            ("__MACOSX/folder/._a.txt", "resource fork"),
            ("folder/", null),
            ("../../etc/b.txt", "B"),
            ("inner.zip", "PK"));

        var result = await ExpandAsync(new UploadSource("upload.zip", () => new MemoryStream(zip)));

        Assert.Equal(["a.txt", "b.txt"], result.Files.Select(f => f.FileName));
        Assert.Equal(new RejectedUpload("inner.zip", ImportErrorCodes.UnsupportedFileType), Assert.Single(result.Rejected));
    }

    [Fact]
    public async Task Decompressed_size_is_bounded_whatever_the_archive_claims()
    {
        var zip = Zip(("1.txt", new string('a', 1_000)), ("2.txt", new string('b', 1_000)), ("3.txt", new string('c', 1_000)));

        var result = await ExpandAsync(new UploadSource("bomb.zip", () => new MemoryStream(zip)));

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(new RejectedUpload("3.txt", ImportErrorCodes.UploadTooLarge), Assert.Single(result.Rejected));
    }

    [Fact]
    public async Task A_corrupt_archive_is_rejected()
    {
        var result = await ExpandAsync(Text("broken.zip", "not a zip"));

        Assert.Equal(new RejectedUpload("broken.zip", ImportErrorCodes.InvalidArchive), Assert.Single(result.Rejected));
    }

    [Fact]
    public async Task Too_many_files_fails_the_whole_upload()
    {
        var result = await ExpandAsync(Text("1.txt", "1"), Text("2.txt", "2"), Text("3.txt", "3"), Text("4.txt", "4"));

        Assert.True(result.TooManyFiles);
        Assert.Empty(result.Files);
    }

    [Theory]
    [InlineData(@"C:\Users\me\hands.txt", "hands.txt")]
    [InlineData("/var/tmp/../hands.txt", "hands.txt")]
    [InlineData("  ", "file")]
    [InlineData(null, "file")]
    [InlineData("a\tb.txt", "ab.txt")]
    public void File_names_are_reduced_to_a_safe_bare_name(string? input, string expected) =>
        Assert.Equal(expected, UploadExpander.SafeFileName(input));

    private static Task<ExpandedUpload> ExpandAsync(params UploadSource[] sources) =>
        UploadExpander.ExpandAsync(sources, Options, TestContext.Current.CancellationToken);

    private static UploadSource Text(string name, string content) =>
        new(name, () => new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static byte[] Zip(params (string Name, string? Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                if (content is not null)
                {
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(content);
                }
            }
        }

        return buffer.ToArray();
    }
}
