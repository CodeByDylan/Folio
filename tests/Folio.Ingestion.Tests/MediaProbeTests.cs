using System.Net;
using Folio.Domain.Model;
using Folio.Ingestion.Media;

namespace Folio.Ingestion.Tests;

/// <summary>Answers range requests from canned bytes, recording what was asked for.</summary>
internal sealed class RangeStub(byte[] body, HttpStatusCode status = HttpStatusCode.PartialContent)
    : HttpMessageHandler
{
    private Exception? _fault;
    private CountingStream? _counter;

    /// <summary>Gets the URL and requested range of every call, in order.</summary>
    public List<(Uri Url, long? From, long? To)> Calls { get; } = [];

    /// <summary>Gets how many body bytes the caller actually pulled from the stream.</summary>
    public long BytesRead => _counter?.Consumed ?? 0;

    /// <summary>Makes every later request throw.</summary>
    /// <param name="fault">The exception to throw.</param>
    public void Throw(Exception fault) => _fault = fault;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        System.Net.Http.Headers.RangeItemHeaderValue? range = request.Headers.Range?.Ranges.FirstOrDefault();
        Calls.Add((request.RequestUri!, range?.From, range?.To));

        if (_fault is not null)
        {
            throw _fault;
        }

        int length = status is HttpStatusCode.PartialContent
            ? (int)Math.Min(body.Length, (range?.To ?? body.Length - 1) + 1)
            : body.Length;

        _counter = new CountingStream(body[..length]);

        return Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(_counter) });
    }

}

/// <summary>A stream that records how much of it was actually read.</summary>
internal sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
{
    /// <summary>Gets the number of bytes read so far.</summary>
    public long Consumed { get; private set; }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        int read = base.Read(buffer);
        Consumed += read;
        return read;
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = base.Read(buffer.Span);
        Consumed += read;
        return ValueTask.FromResult(read);
    }
}

public sealed class MediaProbeTests
{
    [Test]
    public async Task A_Png_Is_Measured_From_One_Kilobyte()
    {
        RangeStub stub = new(Images.Png(1280, 720));

        MediaSize? size = await Measure(stub);

        await Assert.That(size!.Width).IsEqualTo(1280);
        await Assert.That(stub.Calls).Count().IsEqualTo(1);
        await Assert.That(stub.Calls[0].To).IsEqualTo(ImageHeader.ProbeLength - 1L);
    }

    [Test]
    public async Task The_Url_Names_The_Pinned_Commit_On_The_Raw_Host()
    {
        RangeStub stub = new(Images.Png(1, 1));

        _ = await Measure(stub);

        await Assert.That(stub.Calls[0].Url)
            .IsEqualTo(new Uri("https://raw.githubusercontent.com/dutchy/folio/abc123/.folio/media/hero.png"));
    }

    [Test]
    public async Task A_Jpeg_Beyond_The_First_Read_Earns_Exactly_One_More()
    {
        RangeStub stub = new(Images.Padded(1600, 2400, 1200));

        MediaSize? size = await Measure(stub);

        await Assert.That(size!.Height).IsEqualTo(2400);
        await Assert.That(stub.Calls.Select(call => call.To))
            .IsEquivalentTo(
                [(long?)(ImageHeader.ProbeLength - 1), ImageHeader.ExtendedProbeLength - 1],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Png_Never_Earns_A_Second_Read()
    {
        RangeStub stub = new(Images.Png(10, 10));

        _ = await Measure(stub);

        await Assert.That(stub.Calls).Count().IsEqualTo(1);
    }

    [Test]
    public async Task An_Origin_That_Ignores_Range_Is_Not_Buffered_Whole()
    {
        // A 200 means the whole file arrives; only the probe length may be read from it.
        byte[] huge = [.. Images.Png(640, 480), .. new byte[4 * 1024 * 1024]];
        RangeStub stub = new(huge, HttpStatusCode.OK);

        MediaSize? size = await Measure(stub);

        await Assert.That(size!.Width).IsEqualTo(640);
        await Assert.That(stub.BytesRead).IsLessThanOrEqualTo(ImageHeader.ProbeLength);
    }

    [Test]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task An_Unsuccessful_Response_Measures_Nothing(HttpStatusCode status)
    {
        await Assert.That(await Measure(new RangeStub([], status))).IsNull();
    }

    [Test]
    public async Task A_Transport_Fault_Measures_Nothing_Rather_Than_Throwing()
    {
        RangeStub stub = new(Images.Png(1, 1));
        stub.Throw(new HttpRequestException("unreachable"));

        await Assert.That(await Measure(stub)).IsNull();
    }

    [Test]
    public async Task A_Reset_Mid_Body_Measures_Nothing_Rather_Than_Throwing()
    {
        RangeStub stub = new(Images.Png(1, 1));
        stub.Throw(new IOException("connection reset"));

        await Assert.That(await Measure(stub)).IsNull();
    }

    [Test]
    public async Task A_Probe_Timeout_Measures_Nothing_Rather_Than_Throwing()
    {
        RangeStub stub = new(Images.Png(1, 1));
        stub.Throw(new TaskCanceledException("timed out"));

        await Assert.That(await Measure(stub)).IsNull();
    }

    [Test]
    public async Task Bytes_That_Match_No_Format_Measure_Nothing()
    {
        await Assert.That(await Measure(new RangeStub([1, 2, 3, 4, 5, 6, 7, 8]))).IsNull();
    }

    private static async Task<MediaSize?> Measure(RangeStub stub)
    {
        using HttpClient client = new(stub);

        return await new MediaProbe(client)
            .MeasureAsync("dutchy/folio", "abc123", ".folio/media/hero.png", CancellationToken.None);
    }
}
