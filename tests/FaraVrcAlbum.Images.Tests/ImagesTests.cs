using System.Text;
using FaraVrcAlbum.Images;
using Xunit;

namespace FaraVrcAlbum.Images.Tests;

public sealed class XmpAndPngTests
{
    [Fact]
    public void ReadsAttributeFormXmp()
    {
        var info = XmpReader.Parse(TestImages.XmpAttributes());

        Assert.Equal(TestImages.WorldId, info.WorldId);
        Assert.Equal("テストのワールド", info.WorldName);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 21, 33, 12, 345, TimeSpan.FromHours(9)), info.CreateDate);
    }

    [Fact]
    public void XmpAuthorIsNeitherReadNorKept()
    {
        // 撮影者（AuthorID・Author）は読まない（台帳・記事に入れない）。ワールドと撮影日時だけを読む
        var info = XmpReader.Parse(TestImages.XmpAttributes());

        Assert.DoesNotContain(typeof(XmpInfo).GetProperties(), p => p.Name.StartsWith("Author", StringComparison.Ordinal));
        Assert.Equal(TestImages.WorldId, info.WorldId);
        Assert.NotNull(info.CreateDate);
    }

    [Fact]
    public void ReadsElementFormXmp()
    {
        var info = XmpReader.Parse(TestImages.XmpElements());

        Assert.Equal(TestImages.WorldId, info.WorldId);
        Assert.Equal("要素のワールド", info.WorldName);
        Assert.NotNull(info.CreateDate);
    }

    [Fact]
    public void InvalidIdsAreDroppedNotPassedOn()
    {
        var info = XmpReader.Parse(TestImages.XmpAttributes(worldId: "wrld_not-a-uuid"));

        Assert.Null(info.WorldId);
        Assert.Equal("テストのワールド", info.WorldName);
    }

    [Fact]
    public void FindsXmpInPngAndReportsSize()
    {
        var png = TestImages.Png(320, 180, TestImages.XmpAttributes());

        Assert.Equal((320, 180), PngReader.Size(png));
        Assert.Contains("vrc:WorldDisplayName", PngReader.FindXmp(png) ?? "");
    }

    [Fact]
    public void PngWithoutXmpHasNoXmp()
    {
        var png = TestImages.Png(64, 64);

        Assert.Null(PngReader.FindXmp(png));
        Assert.True(PngReader.IsPng(png));
    }

    [Fact]
    public void StreamingScannerAgreesWithInMemoryReader()
    {
        using var dir = new TempDir();
        var path = dir.File("VRChat_2026-10-23_21-33-12.345_320x180.png");
        File.WriteAllBytes(path, TestImages.Png(320, 180, TestImages.XmpAttributes()));

        var header = PngScanner.ScanFile(path);

        Assert.NotNull(header);
        Assert.Equal((320, 180), (header.Width, header.Height));
        Assert.Equal(PngReader.FindXmp(File.ReadAllBytes(path)), header.XmpText);
    }

    [Fact]
    public void ParsesFileNamesAsJst()
    {
        var parsed = PhotoKeys.ParseFileName("VRChat_2026-10-23_21-33-12.345_3840x2160.png");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 21, 33, 12, 345, TimeSpan.FromHours(9)), parsed.Value.TakenAt);
        Assert.Equal((3840, 2160), (parsed.Value.Width, parsed.Value.Height));
        var old = PhotoKeys.ParseFileName("VRChat_3840x2160_2025-11-03_08-05-09.500.png");
        Assert.NotNull(old);
        Assert.Equal(new DateTimeOffset(2025, 11, 3, 8, 5, 9, 500, TimeSpan.FromHours(9)), old.Value.TakenAt);
        Assert.Equal((3840, 2160), (old.Value.Width, old.Value.Height));
        Assert.Null(PhotoKeys.ParseFileName("スクリーンショット.png"));
        Assert.Null(PhotoKeys.ParseFileName("VRChat_2026-13-40_21-33-12.345_3840x2160.png"));
    }

    [Fact]
    public void KeyFormatIsMonthAndJstTimeAndEightHashDigits()
    {
        var utcTime = new DateTimeOffset(2026, 10, 23, 12, 33, 12, 345, TimeSpan.Zero);
        var key = PhotoKeys.Build(utcTime, new string('a', 8) + new string('b', 56));

        Assert.Equal("2026-10/20261023-213312-345_aaaaaaaa", key);
        Assert.True(SiteRules.IsValidPhotoKey(key));
        Assert.Equal("2026-10", PhotoKeys.MonthOf(key));
        Assert.Equal("2026-10-23T21:33:12+09:00", PhotoKeys.TakenAtText(utcTime));
    }
}

public sealed class EncoderTests
{
    [Fact]
    public void ProducesWebpSizesWithoutUpscalingAndNoOgByDefault()
    {
        var encoded = PhotoEncoder.Encode(TestImages.Png(1600, 900, TestImages.XmpAttributes()));

        // OGP はカバー写真だけ（EncodeOg）。登録時の変換には含めない
        Assert.Equal(["640", "1280", "2048"], encoded.Variants.Select(v => v.Name));
        Assert.Equal((1600, 900), (encoded.W, encoded.H));
        Assert.Equal((640, 360), Dimensions(encoded, "640"));
        Assert.Equal((1280, 720), Dimensions(encoded, "1280"));
        Assert.Equal((1600, 900), Dimensions(encoded, "2048"));
        Assert.Equal(6, encoded.Color.Length);
    }

    [Fact]
    public void LargeSourceIsScaledDownToTheLongEdge()
    {
        var png = TestImages.Png(3840, 2160, TestImages.XmpAttributes());
        var encoded = PhotoEncoder.Encode(png);

        Assert.Equal((2048, 1152), (encoded.W, encoded.H));
        Assert.Equal((2048, 1152), Dimensions(encoded, "2048"));
    }

    [Fact]
    public void CoverOgImageIsOnDemandAndStripped()
    {
        var og = PhotoEncoder.EncodeOg(TestImages.Png(3840, 2160, TestImages.XmpAttributes()));

        Assert.Equal("_og.jpg", og.Suffix);
        Assert.Equal("image/jpeg", og.ContentType);
        Assert.Equal((1200, 630), JpegDimensions(og.Data));
        MetadataGuard.EnsureStripped(og.Data, og.ContentType);
    }

    [Fact]
    public void XmpCreateDateWithoutOffsetIsJst()
    {
        var parsed = XmpReader.ParseCreateDate("2026-10-23T21:33:12.345");

        Assert.Equal(new DateTimeOffset(2026, 10, 23, 21, 33, 12, 345, TimeSpan.FromHours(9)), parsed);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 21, 33, 12, TimeSpan.FromHours(9)), XmpReader.ParseCreateDate("2026-10-23T21:33:12+09:00")!.Value.ToOffset(TimeSpan.FromHours(9)));
        Assert.Null(XmpReader.ParseCreateDate("not a date"));
    }

    [Fact]
    public void OutputHasNoMetadataChunks()
    {
        var encoded = PhotoEncoder.Encode(TestImages.Png(640, 360, TestImages.XmpAttributes()));

        foreach (var variant in encoded.Variants.Where(v => v.ContentType == "image/webp"))
        {
            var ids = WebpInspector.ChunkIds(variant.Data);
            Assert.DoesNotContain("EXIF", ids);
            Assert.DoesNotContain("XMP ", ids);
            Assert.DoesNotContain("ICCP", ids);
            Assert.True(variant.Data.AsSpan().IndexOf(Encoding.UTF8.GetBytes("テストのワールド")) < 0, "XMP の文字列が残っています");
        }
    }

    [Fact]
    public void SameInputGivesSameBytes()
    {
        var png = TestImages.Png(800, 600, TestImages.XmpAttributes());

        var first = PhotoEncoder.Encode(png);
        var second = PhotoEncoder.Encode(png);

        Assert.Equal(first.Variants.Select(v => v.Data), second.Variants.Select(v => v.Data));
        Assert.Equal(first.Color, second.Color);
    }

    [Fact]
    public void MetadataGuardRejectsExifInJpeg()
    {
        Assert.Throws<InvalidOperationException>(() => MetadataGuard.EnsureStripped(TestImages.JpegWithExif(), "image/jpeg"));
    }

    [Fact]
    public void MetadataGuardAcceptsStrippedOutput()
    {
        var encoded = PhotoEncoder.Encode(TestImages.Png(200, 100, TestImages.XmpAttributes()));
        foreach (var variant in encoded.Variants) MetadataGuard.EnsureStripped(variant.Data, variant.ContentType);
    }

    [Fact]
    public void WebpDimensionsReadLossyAndLosslessHeaders()
    {
        using var image = NetVips.Image.Black(123, 45).Linear(new double[] { 0, 0, 0 }, new double[] { 1, 2, 3 });
        var lossy = image.WebpsaveBuffer(q: 80, keep: NetVips.Enums.ForeignKeep.None);
        var lossless = image.WebpsaveBuffer(lossless: true, keep: NetVips.Enums.ForeignKeep.None);

        Assert.Equal((123, 45), WebpInspector.Dimensions(lossy));
        Assert.Equal((123, 45), WebpInspector.Dimensions(lossless));
        Assert.Null(WebpInspector.Dimensions(TestImages.Png(10, 10)));
    }

    private static (int W, int H) Dimensions(EncodedPhoto encoded, string name) =>
        WebpInspector.Dimensions(encoded.Variants.Single(v => v.Name == name).Data)
        ?? JpegDimensions(encoded.Variants.Single(v => v.Name == name).Data);

    private static (int W, int H) JpegDimensions(byte[] data)
    {
        var pos = 2;
        while (pos + 9 < data.Length)
        {
            if (data[pos] != 0xFF) throw new InvalidDataException("JPEG のマーカーが不正");
            var marker = data[pos + 1];
            var length = (data[pos + 2] << 8) | data[pos + 3];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                var height = (data[pos + 5] << 8) | data[pos + 6];
                var width = (data[pos + 7] << 8) | data[pos + 8];
                return (width, height);
            }
            pos += 2 + length;
        }
        throw new InvalidDataException("JPEG の寸法が見つからない");
    }
}

public sealed class IngestTests : IDisposable
{
    private const string Name = "VRChat_2026-10-23_21-33-12.345_640x360.png";
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string WritePhoto(string name, byte[] bytes)
    {
        var path = _dir.File(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Photo(string? shadeSeed = null, string created = "2026-10-23T21:33:12.345+09:00") =>
        TestImages.Png(640, 360, TestImages.XmpAttributes(created: created), shade: shadeSeed is null ? 120 : 121);

    private const string FaraXmlDescription =
        "<rdf:Description xmlns:faraxml=\"http://fara-labs.com/ns/faraxml/1.0/\" faraxml:Version=\"1\" faraxml:AvatarName=\"Fara AlmarianDress\" faraxml:AvatarId=\"avtr_00000000-0000-4000-8000-000000000003\" faraxml:Source=\"log\"/>";

    [Fact]
    public void ReadsTheAvatarThatFaraVrcPhotoServerAppended()
    {
        var xmp = TestImages.XmpAttributes().Replace("</rdf:RDF>", FaraXmlDescription + "</rdf:RDF>");
        var info = XmpReader.Parse(xmp);
        Assert.Equal("Fara AlmarianDress", info.AvatarName);
        Assert.Equal("avtr_00000000-0000-4000-8000-000000000003", info.AvatarId);
        Assert.Equal(TestImages.WorldId, info.WorldId);                                                    // 元の項目も、今までどおり読める

        var element = TestImages.XmpAttributes().Replace("</rdf:RDF>",
            "<rdf:Description xmlns:faraxml=\"http://fara-labs.com/ns/faraxml/1.0/\"><faraxml:AvatarName>要素の名前</faraxml:AvatarName></rdf:Description></rdf:RDF>");
        Assert.Equal("要素の名前", XmpReader.Parse(element).AvatarName);
        Assert.Null(XmpReader.Parse(element).AvatarId);

        Assert.Null(XmpReader.Parse(TestImages.XmpAttributes()).AvatarName);                               // 追記が無い写真
        var bad = TestImages.XmpAttributes().Replace("</rdf:RDF>", FaraXmlDescription.Replace("avtr_00000000-0000-4000-8000-000000000003", "usr_00000000-0000-4000-8000-000000000003") + "</rdf:RDF>");
        Assert.Null(XmpReader.Parse(bad).AvatarId);                                                        // avtr_ でない ID は使わない
    }

    private const string FaraTagDescription =
        "<rdf:Description xmlns:faratag=\"http://fara-labs.com/ns/faratag/1.0/\"><faratag:AvatarName>Sample Avatar</faratag:AvatarName><faratag:AvatarID>avtr_00000000-0000-4000-8000-000000000001</faratag:AvatarID><faratag:Source>log</faratag:Source><faratag:Version>2</faratag:Version></rdf:Description>";

    [Fact]
    public void ReadsTheAvatarInTheNewFaratagFormatAndPrefersItOverTheOldOne()
    {
        var xmp = TestImages.XmpAttributes().Replace("</rdf:RDF>", FaraTagDescription + "</rdf:RDF>");
        var info = XmpReader.Parse(xmp);
        Assert.Equal("Sample Avatar", info.AvatarName);
        Assert.Equal("avtr_00000000-0000-4000-8000-000000000001", info.AvatarId);
        Assert.Equal(TestImages.WorldId, info.WorldId);                                                    // 元の項目も、今までどおり読める

        // AvatarID が無いとき（ログで分からなかった）は、名前だけ
        var noId = TestImages.XmpAttributes().Replace("</rdf:RDF>",
            "<rdf:Description xmlns:faratag=\"http://fara-labs.com/ns/faratag/1.0/\"><faratag:AvatarName>名前だけ</faratag:AvatarName><faratag:Source>log</faratag:Source><faratag:Version>2</faratag:Version></rdf:Description></rdf:RDF>");
        Assert.Equal(("名前だけ", null), (XmpReader.Parse(noId).AvatarName, XmpReader.Parse(noId).AvatarId));

        // 新旧が両方ある写真（通常は無い）は、新しい形式を使う
        var both = TestImages.XmpAttributes().Replace("</rdf:RDF>", FaraXmlDescription + FaraTagDescription + "</rdf:RDF>");
        Assert.Equal("Sample Avatar", XmpReader.Parse(both).AvatarName);

        var bad = TestImages.XmpAttributes().Replace("</rdf:RDF>", FaraTagDescription.Replace("avtr_00000000-0000-4000-8000-000000000001", "usr_00000000-0000-4000-8000-000000000001") + "</rdf:RDF>");
        Assert.Null(XmpReader.Parse(bad).AvatarId);                                                        // avtr_ でない ID は使わない
    }

    [Fact]
    public async Task TheAvatarFromThePhotoIsKeptInTheLedgerEntry()
    {
        var withAvatar = TestImages.Png(640, 360, TestImages.XmpAttributes().Replace("</rdf:RDF>", FaraXmlDescription + "</rdf:RDF>"), shade: 90);
        var path = WritePhoto(Name, withAvatar);
        var ledger = PhotoLedger.Empty();

        var result = await new PhotoIngestor(ledger, new MemoryObjectStore()).IngestAsync(path, new IngestOptions { Apply = true });

        Assert.Equal(IngestState.Uploaded, result.State);
        var entry = Assert.Single(ledger.BySha).Value;
        Assert.Equal("Fara AlmarianDress", entry.AvatarName);
        Assert.Equal("avtr_00000000-0000-4000-8000-000000000003", entry.AvatarId);
    }

    [Fact]
    public async Task DryRunWritesNothingAndKeepsLedgerEmpty()
    {
        var path = WritePhoto(Name, Photo());
        var ledger = PhotoLedger.Empty();
        var store = new MemoryObjectStore();

        var result = await new PhotoIngestor(ledger, store).IngestAsync(path, new IngestOptions { Apply = false });

        Assert.Equal(IngestState.Planned, result.State);
        Assert.Equal(0, store.PutCount);
        Assert.Empty(ledger.BySha);
        Assert.False(result.LedgerChanged);
    }

    [Fact]
    public async Task ApplyPutsFourObjectsWithHeadersAndRegistersOnce()
    {
        var path = WritePhoto(Name, Photo());
        var ledger = PhotoLedger.Empty();
        var store = new MemoryObjectStore();
        var ingestor = new PhotoIngestor(ledger, store);

        var first = await ingestor.IngestAsync(path, new IngestOptions { Apply = true });
        var second = await ingestor.IngestAsync(path, new IngestOptions { Apply = true });

        Assert.Equal(IngestState.Uploaded, first.State);
        Assert.Equal(3, first.PutCount);
        Assert.Equal(IngestState.AlreadyUploaded, second.State);
        Assert.Equal(0, second.PutCount);
        Assert.Equal(3, store.PutCount);
        var key = first.Key!;
        Assert.Equal("image/webp", store.Objects[key + "_640.webp"].ContentType);
        Assert.Equal(ObjectHeaders.ImmutableImage, store.Objects[key + "_1280.webp"].CacheControl);
        Assert.DoesNotContain(key + "_og.jpg", store.Objects.Keys);

        var entry = ledger.FindByKey(key)!;
        Assert.Equal(LedgerStates.Uploaded, entry.State);
        Assert.Equal((640, 360), (entry.W, entry.H));
        Assert.Equal(TestImages.WorldId, entry.World!.Id);
        Assert.Equal("テストのワールド", entry.World.Name);
        Assert.Equal(PhotoEncoder.Generation, entry.Encoder);
        Assert.Equal(3, entry.Bytes.Count);
        Assert.False(entry.Bytes.ContainsKey("og"));
        Assert.Equal("2026-10-23T21:33:12+09:00", entry.TakenAt);
    }

    [Fact]
    public async Task PartialFailureLeavesNoLedgerEntryAndRetryFillsTheGaps()
    {
        var path = WritePhoto(Name, Photo());
        var ledger = PhotoLedger.Empty();
        var store = new MemoryObjectStore { OnPut = (_, count) => count == 2 ? new IOException("通信エラー（試験）") : null };
        var ingestor = new PhotoIngestor(ledger, store);

        await Assert.ThrowsAsync<IOException>(() => ingestor.IngestAsync(path, new IngestOptions { Apply = true }));
        Assert.Empty(ledger.BySha);
        Assert.Equal(2, store.Objects.Count);

        store.OnPut = null;
        var retry = await ingestor.IngestAsync(path, new IngestOptions { Apply = true });

        Assert.Equal(IngestState.Uploaded, retry.State);
        Assert.Equal(1, retry.PutCount); // 2 つは残っている（HEAD で補うのは不足分の 1 つだけ）
        Assert.Single(ledger.BySha);
    }

    [Fact]
    public async Task RecoversLedgerEntryWhenObjectsAlreadyExistInR2()
    {
        var path = WritePhoto(Name, Photo());
        var store = new MemoryObjectStore();
        await new PhotoIngestor(PhotoLedger.Empty(), store).IngestAsync(path, new IngestOptions { Apply = true });
        var emptyLedger = PhotoLedger.Empty();

        var result = await new PhotoIngestor(emptyLedger, store).IngestAsync(path, new IngestOptions { Apply = true });

        Assert.Equal(0, result.PutCount);
        Assert.True(result.LedgerChanged);
        Assert.Single(emptyLedger.BySha);
    }

    [Fact]
    public async Task RemovedPhotoIsRefusedUnlessForced()
    {
        var path = WritePhoto(Name, Photo());
        var store = new MemoryObjectStore();
        var ledger = PhotoLedger.Empty();
        var ingestor = new PhotoIngestor(ledger, store);
        await ingestor.IngestAsync(path, new IngestOptions { Apply = true });
        var sha = ledger.BySha.Keys.Single();
        ledger.BySha[sha].State = LedgerStates.Removed;

        var refused = await ingestor.IngestAsync(path, new IngestOptions { Apply = true });
        var forced = await ingestor.IngestAsync(path, new IngestOptions { Apply = true, Force = true });

        Assert.Equal(IngestState.Refused, refused.State);
        Assert.Contains("削除済み", refused.Message);
        Assert.Equal(IngestState.Uploaded, forced.State);
        Assert.Equal(LedgerStates.Uploaded, ledger.BySha[sha].State);
    }

    [Fact]
    public async Task RefusesPhotosBeforeNovember2025()
    {
        var path = WritePhoto("VRChat_2025-10-31_23-59-59.999_640x360.png", Photo(created: "2025-10-31T23:59:59.999+09:00"));

        var result = await new PhotoIngestor(PhotoLedger.Empty(), new MemoryObjectStore()).IngestAsync(path, new IngestOptions { Apply = true });

        Assert.Equal(IngestState.Refused, result.State);
        Assert.Contains("2025 年 11 月より前", result.Message);
    }

    [Fact]
    public async Task RefusesNonPngAndWarnsWhenXmpIsMissing()
    {
        var notPng = WritePhoto("not-png.png", new byte[] { 1, 2, 3 });
        var noXmp = WritePhoto(Name, TestImages.Png(640, 360));

        var refused = await new PhotoIngestor(PhotoLedger.Empty(), null).IngestAsync(notPng, new IngestOptions());
        var planned = await new PhotoIngestor(PhotoLedger.Empty(), null).IngestAsync(noXmp, new IngestOptions());

        Assert.Equal(IngestState.Refused, refused.State);
        Assert.Equal(IngestState.Planned, planned.State);
        Assert.Contains(planned.Warnings, w => w.Contains("XMP が無い"));
        Assert.Contains(planned.Warnings, w => w.Contains("撮影時刻は XMP に無く"));
    }

    [Fact]
    public async Task WarnsWhenFolderMonthDiffersFromTakenMonth()
    {
        var path = WritePhoto(Name, Photo());

        var result = await new PhotoIngestor(PhotoLedger.Empty(), null).IngestAsync(path, new IngestOptions { FolderMonth = "2026-09" });

        Assert.Contains(result.Warnings, w => w.Contains("フォルダ（2026-09）と撮影月（2026-10）"));
        Assert.StartsWith("2026-10/", result.Key);
    }

    [Fact]
    public async Task RefusesKeyCollisionWithAnotherPhoto()
    {
        var path = WritePhoto(Name, Photo());
        var bytes = await File.ReadAllBytesAsync(path);
        var sha = Hashing.Sha256Hex(bytes);
        var key = PhotoKeys.Build(new DateTimeOffset(2026, 10, 23, 21, 33, 12, 345, TimeSpan.FromHours(9)), sha);
        var ledger = PhotoLedger.Empty();
        ledger.Put(new string('0', 64), new LedgerEntry { Key = key, State = LedgerStates.Uploaded });

        var result = await new PhotoIngestor(ledger, new MemoryObjectStore()).IngestAsync(path, new IngestOptions { Apply = true });

        Assert.Equal(IngestState.Refused, result.State);
        Assert.Contains("衝突", result.Message);
    }

    [Fact]
    public void LedgerRoundTripsWithSortedKeysUtf8AndLf()
    {
        var ledger = PhotoLedger.Empty();
        ledger.Put("bbbb", new LedgerEntry { Key = "2026-10/20261024-000000-000_bbbbbbbb", World = new LedgerWorld { Id = TestImages.WorldId, Name = "名前" } });
        ledger.Put("aaaa", new LedgerEntry { Key = "2026-10/20261023-000000-000_aaaaaaaa" });
        var path = _dir.File("catalog/photos.json");

        ledger.Save(path);
        var text = File.ReadAllText(path);
        var reloaded = PhotoLedger.Load(path);

        Assert.DoesNotContain("\r", text);
        Assert.Contains("名前", text);
        Assert.True(text.IndexOf("aaaaaaaa", StringComparison.Ordinal) < text.IndexOf("bbbbbbbb", StringComparison.Ordinal));
        Assert.Equal(2, reloaded.BySha.Count);
        Assert.Equal("名前", reloaded.FindBySha("bbbb")!.World!.Name);
        Assert.Equal("2026-10/20261023-000000-000_aaaaaaaa", reloaded.FindBySha("aaaa")!.Key);
    }

    [Fact]
    public void ObjectHeadersMatchTheDocumentedPolicy()
    {
        Assert.Equal("application/json; charset=utf-8", ObjectHeaders.ContentTypeFor("data/site.json"));
        Assert.Equal("image/webp", ObjectHeaders.ContentTypeFor("2026-10/20261023-213312-345_a1b2c3d4_640.webp"));
    }
}
