using System.Text;
using FdeTracker.Api.Services;

namespace FdeTracker.Tests;

/// <summary>What a file really is is decided from its bytes, never from its name or the type the browser claims.</summary>
public sealed class FileSnifferTests
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public void The_type_comes_from_the_bytes()
    {
        Assert.Equal("image/png", FileSniffer.Detect(Png)!.ContentType);
        Assert.Equal("application/pdf", FileSniffer.Detect(Encoding.ASCII.GetBytes("%PDF-1.7 rest"))!.ContentType);
        Assert.Equal("image/jpeg", FileSniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0])!.ContentType);
        Assert.Equal("application/zip", FileSniffer.Detect([0x50, 0x4B, 0x03, 0x04, 0])!.ContentType);
        Assert.StartsWith("text/plain", FileSniffer.Detect(Encoding.UTF8.GetBytes("# Notes\nhello"))!.ContentType);
    }

    [Fact]
    public void Web_pages_are_only_ever_text_and_programs_and_junk_are_refused()
    {
        Assert.False(FileSniffer.Detect(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"))!.ShowInline); // text, shown as a download
        Assert.Null(FileSniffer.Detect([0x4D, 0x5A, 0x90, 0x00, 0x03])); // a Windows program
        Assert.Null(FileSniffer.Detect([]));
        Assert.Null(FileSniffer.Detect([0xFF, 0xFE, 0xFD, 0xFC]));       // not valid text
        Assert.True(FileSniffer.Detect(Png)!.ShowInline);                // only images are shown in the page
    }

    [Fact]
    public void Names_are_made_safe()
    {
        var text = FileSniffer.Detect(Encoding.UTF8.GetBytes("hello"))!;
        var png = FileSniffer.Detect(Png)!;
        Assert.Equal("evil.png", FileSniffer.SafeName("../../evil.exe", png));   // folders dropped, extension follows the real type
        Assert.Equal("proof.png", FileSniffer.SafeName("", png));
        Assert.Equal("notes.md", FileSniffer.SafeName("notes.md", text));        // a text-like extension the person chose is kept
        Assert.Equal("evil.txt", FileSniffer.SafeName("evil.sh", text));
        Assert.DoesNotContain("<", FileSniffer.SafeName("a<b>\"c.png", png));
        Assert.True(FileSniffer.SafeName(new string('a', 500) + ".png", png).Length <= 100);
    }
}
