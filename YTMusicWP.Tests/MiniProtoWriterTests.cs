using System;
using System.Text;
using AudioPlayerTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class MiniProtoWriterTests
    {
        [TestMethod]
        public void WriteVarint_SingleByteValues_EncodesCorrectly()
        {
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarint(0);
                CollectionAssert.AreEqual(new byte[] { 0x00 }, writer.ToByteArray());
            }

            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarint(1);
                CollectionAssert.AreEqual(new byte[] { 0x01 }, writer.ToByteArray());
            }

            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarint(127);
                CollectionAssert.AreEqual(new byte[] { 0x7F }, writer.ToByteArray());
            }
        }

        [TestMethod]
        public void WriteVarint_MultiByteValues_EncodesCorrectly()
        {
            // 128 = 0x80 -> 1000 0000 -> 0x80, 0x01
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarint(128);
                CollectionAssert.AreEqual(new byte[] { 0x80, 0x01 }, writer.ToByteArray());
            }

            // 300 = 0x012C -> 0xAC, 0x02
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarint(300);
                CollectionAssert.AreEqual(new byte[] { 0xAC, 0x02 }, writer.ToByteArray());
            }
        }

        [TestMethod]
        public void WriteTag_EncodesFieldNumberAndWireType()
        {
            // Field 1, WireType 0 (Varint): (1 << 3) | 0 = 8 = 0x08
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteTag(1, 0);
                CollectionAssert.AreEqual(new byte[] { 0x08 }, writer.ToByteArray());
            }

            // Field 2, WireType 2 (Length-delimited): (2 << 3) | 2 = 18 = 0x12
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteTag(2, 2);
                CollectionAssert.AreEqual(new byte[] { 0x12 }, writer.ToByteArray());
            }
        }

        [TestMethod]
        public void WriteVarintField_CombinesTagAndValue()
        {
            // Field 1, Value 150 -> Tag 0x08, Value 0x96, 0x01
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteVarintField(1, 150);
                CollectionAssert.AreEqual(new byte[] { 0x08, 0x96, 0x01 }, writer.ToByteArray());
            }
        }

        [TestMethod]
        public void WriteStringField_SerializesLengthAndUtf8Bytes()
        {
            // Field 2, "hello" -> Tag 0x12, Length 5 (0x05), UTF-8 bytes
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteStringField(2, "hello");
                byte[] expected = new byte[] { 0x12, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F };
                CollectionAssert.AreEqual(expected, writer.ToByteArray());
            }
        }

        [TestMethod]
        public void WriteStringField_NullOrEmpty_DoesNotWriteBytes()
        {
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteStringField(1, null);
                Assert.AreEqual(0, writer.ToByteArray().Length);

                writer.WriteStringField(2, "");
                Assert.AreEqual(0, writer.ToByteArray().Length);
            }
        }

        [TestMethod]
        public void WriteBytesField_SerializesLengthAndRawBytes()
        {
            byte[] raw = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            using (var writer = new MiniProtoWriter())
            {
                writer.WriteBytesField(3, raw);
                byte[] result = writer.ToByteArray();

                // Tag: (3 << 3) | 2 = 26 = 0x1A, Length: 4 = 0x04, Bytes: DE AD BE EF
                byte[] expected = new byte[] { 0x1A, 0x04, 0xDE, 0xAD, 0xBE, 0xEF };
                CollectionAssert.AreEqual(expected, result);
            }
        }

        [TestMethod]
        public void BuildAudioAbrRequest_LiveStreamParams_GeneratesValidProtobuf()
        {
            byte[] dummyConfig = new byte[] { 0x01, 0x02 };
            byte[] cookie = new byte[] { 0xAA, 0xBB };

            byte[] request = MiniProtoWriter.BuildAudioAbrRequest(
                ustreamerConfig: dummyConfig,
                playerTimeMs: 12000,
                playbackRate: 1.0f,
                preferredItag: 140,
                clientNameInt: 3,
                clientVersion: "19.29.35",
                poToken: null,
                playbackCookie: cookie,
                formatsInitialized: true,
                bufferedDurationMs: 10000,
                startSegmentIndex: 649264,
                endSegmentIndex: 649265,
                startTimeMs: 3246320);

            Assert.IsNotNull(request);
            Assert.IsTrue(request.Length > 0);
        }
    }
}
