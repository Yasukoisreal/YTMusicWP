using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioPlayerTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class UmpParserTests
    {
        [TestMethod]
        public void ChunkedStreamReader_ReadExactBytes_ReadsAccurately()
        {
            byte[] data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            using (var ms = new MemoryStream(data))
            {
                var reader = new ChunkedStreamReader(ms, 4);
                byte[] read = reader.ReadExactBytesAsync(5, CancellationToken.None).GetAwaiter().GetResult();

                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, read);
            }
        }

        [TestMethod]
        public void ReadVarintAsync_OneByteVarint_ParsesCorrectly()
        {
            // Value 42 < 128 -> Single byte 0x2A
            byte[] streamData = new byte[] { 42 };
            using (var ms = new MemoryStream(streamData))
            {
                var reader = new ChunkedStreamReader(ms);
                long val = UmpParser.ReadVarintAsync(reader, CancellationToken.None).GetAwaiter().GetResult();
                Assert.AreEqual(42, val);
            }
        }

        [TestMethod]
        public void ReadVarintAsync_TwoByteVarint_ParsesCorrectly()
        {
            // For b0 in [128, 191]: val = (b0 & 0x3F) + 64 * b1
            // Let b0 = 128 (0x80), b1 = 2 -> (128 & 0x3F) + 64 * 2 = 0 + 128 = 128
            byte[] streamData = new byte[] { 0x80, 0x02 };
            using (var ms = new MemoryStream(streamData))
            {
                var reader = new ChunkedStreamReader(ms);
                long val = UmpParser.ReadVarintAsync(reader, CancellationToken.None).GetAwaiter().GetResult();
                Assert.AreEqual(128, val);
            }
        }

        [TestMethod]
        public void ProcessStreamAsync_ValidUmpParts_DeliversAllParts()
        {
            // Part 1: Type 20 (MEDIA_HEADER), Size 3, Data [1, 2, 3]
            // Part 2: Type 21 (MEDIA), Size 2, Data [0xAA, 0xBB]
            byte[] streamData = new byte[]
            {
                20, 3, 1, 2, 3,
                21, 2, 0xAA, 0xBB
            };

            var receivedParts = new List<UmpPart>();

            using (var ms = new MemoryStream(streamData))
            {
                UmpParser.ProcessStreamAsync(ms, part => receivedParts.Add(part), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            Assert.AreEqual(2, receivedParts.Count);
            
            // Check Part 1
            Assert.AreEqual(UmpPartId.MEDIA_HEADER, receivedParts[0].Type);
            Assert.AreEqual(3, receivedParts[0].Size);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, receivedParts[0].Data);

            // Check Part 2
            Assert.AreEqual(UmpPartId.MEDIA, receivedParts[1].Type);
            Assert.AreEqual(2, receivedParts[1].Size);
            CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, receivedParts[1].Data);
        }

        [TestMethod]
        public void ProcessStreamAsync_EmptyStream_DoesNotThrow()
        {
            var receivedParts = new List<UmpPart>();
            using (var ms = new MemoryStream(new byte[0]))
            {
                UmpParser.ProcessStreamAsync(ms, part => receivedParts.Add(part), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            Assert.AreEqual(0, receivedParts.Count);
        }

        [TestMethod]
        public void ExtractLiveMetadata_ValidProtobuf_ExtractsHeadSeqAndTimes()
        {
            // Build a small protobuf with:
            // Field 3 (head_sequence_number) = 649264
            // Field 4 (head_time_ms) = 1710000000000
            // Field 5 (wall_time_ms) = 1710000005000
            byte[] payload;
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarintField(3, 649264);
                writer.WriteVarintField(4, 1710000000000L);
                writer.WriteVarintField(5, 1710000005000L);
                payload = writer.ToByteArray();
            }

            var meta = UmpParser.ExtractLiveMetadata(payload);
            Assert.AreEqual(649264L, meta.HeadSequenceNumber);
            Assert.AreEqual(1710000000000L, meta.HeadTimeMs);
            Assert.AreEqual(1710000005000L, meta.WallTimeMs);
        }

        [TestMethod]
        public void ExtractLiveMetadata_NullOrEmpty_ReturnsDefault()
        {
            var metaNull = UmpParser.ExtractLiveMetadata(null);
            Assert.AreEqual(-1L, metaNull.HeadSequenceNumber);

            var metaEmpty = UmpParser.ExtractLiveMetadata(new byte[0]);
            Assert.AreEqual(-1L, metaEmpty.HeadSequenceNumber);
        }

        [TestMethod]
        public void ExtractNextRequestPolicy_ValidProtobuf_ExtractsCookieAndBackoff()
        {
            // Field 4 (backoff_time_ms) = 2500
            // Field 7 (playback_cookie) = [0xAA, 0xBB, 0xCC]
            byte[] cookieData = new byte[] { 0xAA, 0xBB, 0xCC };
            byte[] payload;
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarintField(4, 2500);
                writer.WriteBytesField(7, cookieData);
                payload = writer.ToByteArray();
            }

            var policy = UmpParser.ExtractNextRequestPolicy(payload);
            Assert.AreEqual(2500, policy.BackoffTimeMs);
            CollectionAssert.AreEqual(cookieData, policy.PlaybackCookie);
        }

        [TestMethod]
        public void ExtractNextRequestPolicy_NullOrEmpty_ReturnsDefault()
        {
            var policyNull = UmpParser.ExtractNextRequestPolicy(null);
            Assert.AreEqual(0, policyNull.BackoffTimeMs);
            Assert.IsNull(policyNull.PlaybackCookie);

            var policyEmpty = UmpParser.ExtractNextRequestPolicy(new byte[0]);
            Assert.AreEqual(0, policyEmpty.BackoffTimeMs);
            Assert.IsNull(policyEmpty.PlaybackCookie);
        }
    }
}
