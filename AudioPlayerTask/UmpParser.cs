using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AudioPlayerTask
{
    /// <summary>
    /// UMP Part Type IDs as defined by YouTube's video_streaming/ump_part_id.proto
    /// </summary>
    internal static class UmpPartId
    {
        public const int UNKNOWN = 0;
        public const int ONESIE_HEADER = 10;
        public const int ONESIE_DATA = 11;
        public const int ONESIE_ENCRYPTED_MEDIA = 12;
        public const int MEDIA_HEADER = 20;
        public const int MEDIA = 21;
        public const int MEDIA_END = 22;
        public const int CONFIG = 30;
        public const int LIVE_METADATA = 31;
        public const int NEXT_REQUEST_POLICY = 35;
        public const int FORMAT_INITIALIZATION_METADATA = 42;
        public const int SABR_REDIRECT = 43;
        public const int SABR_ERROR = 44;
        public const int SABR_SEEK = 45;
        public const int RELOAD_PLAYER_RESPONSE = 46;
        public const int STREAM_PROTECTION_STATUS = 58;
    }

    /// <summary>
    /// Represents a parsed UMP (Universal Media Player) stream part.
    /// </summary>
    internal struct UmpPart
    {
        public int Type;
        public int Size;
        public byte[] Data;
    }

    /// <summary>
    /// Fast in-memory stream buffer reader for WP8.1 (replaces unsupported BufferedStream).
    /// </summary>
    internal sealed class ChunkedStreamReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer;
        private int _offset;
        private int _count;

        public ChunkedStreamReader(Stream stream, int bufferSize = 32768)
        {
            _stream = stream;
            _buffer = new byte[bufferSize];
            _offset = 0;
            _count = 0;
        }

        public async Task<int> ReadByteAsync(CancellationToken ct)
        {
            if (_offset >= _count)
            {
                _offset = 0;
                _count = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);
                if (_count <= 0) return -1;
            }
            return _buffer[_offset++];
        }

        public async Task<byte[]> ReadExactBytesAsync(int length, CancellationToken ct)
        {
            if (length <= 0) return new byte[0];

            byte[] result = new byte[length];
            int written = 0;

            while (written < length)
            {
                int available = _count - _offset;
                if (available > 0)
                {
                    int toCopy = Math.Min(available, length - written);
                    System.Buffer.BlockCopy(_buffer, _offset, result, written, toCopy);
                    _offset += toCopy;
                    written += toCopy;
                }
                else
                {
                    _offset = 0;
                    _count = 0;
                    if (length - written >= _buffer.Length)
                    {
                        int read = await _stream.ReadAsync(result, written, length - written, ct).ConfigureAwait(false);
                        if (read <= 0) break;
                        written += read;
                    }
                    else
                    {
                        _count = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);
                        if (_count <= 0) break;
                    }
                }
            }

            if (written < length)
            {
                byte[] truncated = new byte[written];
                System.Buffer.BlockCopy(result, 0, truncated, 0, written);
                return truncated;
            }

            return result;
        }
    }

    /// <summary>
    /// High-performance, streaming parser for YouTube's application/vnd.yt-ump binary format in pure C# 6.0.
    /// </summary>
    internal static class UmpParser
    {
        /// <summary>
        /// Reads a UMP prefix-coded variable-length integer (Varint) from a ChunkedStreamReader asynchronously.
        /// UMP specification (googlevideo / YouTube):
        ///   b0 < 128 (1 byte):  val = b0
        ///   b0 < 192 (2 bytes): val = (b0 & 0x3F) + 64 * b1
        ///   b0 < 224 (3 bytes): val = (b0 & 0x1F) + 32 * (b1 + 256 * b2)
        ///   b0 < 240 (4 bytes): val = (b0 & 0x0F) + 16 * (b1 + 256 * (b2 + 256 * b3))
        ///   else     (5 bytes): 32-bit unsigned little-endian integer
        /// Returns -1 on EOF or malformed data.
        /// </summary>
        public static async Task<long> ReadVarintAsync(ChunkedStreamReader reader, CancellationToken ct)
        {
            int b0 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
            if (b0 < 0) return -1;

            if (b0 < 128)
            {
                return b0;
            }
            if (b0 < 192)
            {
                int b1 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b1 < 0) return -1;
                return (b0 & 0x3F) + 64 * b1;
            }
            if (b0 < 224)
            {
                int b1 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b1 < 0) return -1;
                int b2 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b2 < 0) return -1;
                return (b0 & 0x1F) + 32 * (b1 + 256 * b2);
            }
            if (b0 < 240)
            {
                int b1 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b1 < 0) return -1;
                int b2 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b2 < 0) return -1;
                int b3 = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b3 < 0) return -1;
                return (b0 & 0x0F) + 16 * (b1 + 256 * (b2 + 256 * b3));
            }

            // 5-byte format: 1 prefix byte + 4 bytes uint32 little endian
            byte[] buf = await reader.ReadExactBytesAsync(4, ct).ConfigureAwait(false);
            if (buf == null || buf.Length < 4) return -1;
            return (long)BitConverter.ToUInt32(buf, 0);
        }

        /// <summary>
        /// Continuously reads UMP parts from the HTTP response stream and delivers them via callback.
        /// </summary>
        public static async Task ProcessStreamAsync(
            Stream stream,
            Action<UmpPart> onPartReceived,
            CancellationToken ct)
        {
            if (stream == null || onPartReceived == null) return;

            var reader = new ChunkedStreamReader(stream, 32768);

            while (!ct.IsCancellationRequested)
            {
                long partType = await ReadVarintAsync(reader, ct).ConfigureAwait(false);
                if (partType < 0) break; // EOF or stream closed

                long partSize = await ReadVarintAsync(reader, ct).ConfigureAwait(false);
                if (partSize < 0) break;

                byte[] payload = null;
                if (partSize > 0)
                {
                    // Sanity check to protect WP8.1 512MB RAM from malformed huge payloads
                    if (partSize > 10 * 1024 * 1024)
                    {
                        break;
                    }

                    payload = await reader.ReadExactBytesAsync((int)partSize, ct).ConfigureAwait(false);
                    if (payload.Length < partSize)
                    {
                        break; // Premature EOF
                    }
                }
                else
                {
                    payload = new byte[0];
                }

                var part = new UmpPart
                {
                    Type = (int)partType,
                    Size = (int)partSize,
                    Data = payload
                };

                onPartReceived(part);
            }
        }

        /// <summary>
        /// Extracts the redirect URL from a SABR_REDIRECT (type 43) protobuf payload.
        /// Wire format: tag 1, wireType 2 => 0x0A [len] [utf8 url]
        /// </summary>
        public static string ExtractSabrRedirectUrl(byte[] data)
        {
            if (data == null || data.Length < 3) return null;
            try
            {
                int idx = 0;
                while (idx < data.Length)
                {
                    byte tag = data[idx++];
                    int fieldNum = tag >> 3;
                    int wireType = tag & 0x07;

                    if (fieldNum == 1 && wireType == 2)
                    {
                        int len = 0;
                        int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }

                        if (len > 0 && idx + len <= data.Length)
                        {
                            return Encoding.UTF8.GetString(data, idx, len);
                        }
                    }
                    else if (wireType == 0)
                    {
                        while (idx < data.Length && (data[idx++] & 0x80) != 0) { }
                    }
                    else if (wireType == 2)
                    {
                        int len = 0;
                        int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        idx += len;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Extracts error details from a SABR_ERROR (type 44) protobuf payload.
        /// Wire format:
        ///   field 1 (string type): 0x0A [len] [utf8 string]
        ///   field 2 (int32 code):  0x10 [varint]
        /// </summary>
        public static string ExtractSabrError(byte[] data)
        {
            if (data == null || data.Length == 0) return "Empty SABR error payload";
            try
            {
                int idx = 0;
                string type = null;
                int? code = null;

                while (idx < data.Length)
                {
                    byte tag = data[idx++];
                    int fieldNum = tag >> 3;
                    int wireType = tag & 0x07;

                    if (fieldNum == 1 && wireType == 2)
                    {
                        int len = 0;
                        int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }

                        if (len > 0 && idx + len <= data.Length)
                        {
                            type = Encoding.UTF8.GetString(data, idx, len);
                            idx += len;
                        }
                    }
                    else if (fieldNum == 2 && wireType == 0)
                    {
                        int val = 0;
                        int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            val |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        code = val;
                    }
                    else if (wireType == 0)
                    {
                        while (idx < data.Length && (data[idx++] & 0x80) != 0) { }
                    }
                    else if (wireType == 2)
                    {
                        int len = 0;
                        int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        idx += len;
                    }
                    else
                    {
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(type) || code.HasValue)
                {
                    return string.Format("{0} (code {1})", type ?? "Unknown", code.HasValue ? code.Value.ToString() : "?");
                }
            }
            catch (Exception ex)
            {
                return "Error parsing SABR error: " + ex.Message;
            }
            return "Unparseable SABR error payload (" + data.Length + " bytes: " + BitConverter.ToString(data) + ")";
        }

        internal struct ParsedLiveMetadata
        {
            public long HeadSequenceNumber;
            public long HeadTimeMs;
            public long WallTimeMs;
        }

        /// <summary>
        /// Extracts head_sequence_number and timestamps from a LIVE_METADATA (type 31) protobuf payload.
        /// </summary>
        internal static ParsedLiveMetadata ExtractLiveMetadata(byte[] data)
        {
            var result = new ParsedLiveMetadata { HeadSequenceNumber = -1, HeadTimeMs = -1, WallTimeMs = -1 };
            if (data == null || data.Length == 0) return result;
            try
            {
                int idx = 0;
                while (idx < data.Length)
                {
                    byte tag = data[idx++];
                    int fieldNum = tag >> 3;
                    int wireType = tag & 0x07;

                    if (wireType == 0) // varint
                    {
                        long val = 0; int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            val |= (long)(b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        if (fieldNum == 3) result.HeadSequenceNumber = val;
                        else if (fieldNum == 4) result.HeadTimeMs = val;
                        else if (fieldNum == 5) result.WallTimeMs = val;
                    }
                    else if (wireType == 2)
                    {
                        int len = 0; int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        idx += len;
                    }
                    else if (wireType == 1) { idx += 8; }
                    else if (wireType == 5) { idx += 4; }
                    else break;
                }
            }
            catch { }
            return result;
        }

        internal struct ParsedNextRequestPolicy
        {
            public byte[] PlaybackCookie;
            public int BackoffTimeMs;
        }

        /// <summary>
        /// Extracts playback cookie and backoff policy from a NEXT_REQUEST_POLICY (type 35) protobuf payload.
        /// </summary>
        internal static ParsedNextRequestPolicy ExtractNextRequestPolicy(byte[] data)
        {
            var result = new ParsedNextRequestPolicy();
            if (data == null || data.Length == 0) return result;
            try
            {
                int idx = 0;
                while (idx < data.Length)
                {
                    byte tag = data[idx++];
                    int fieldNum = tag >> 3;
                    int wireType = tag & 0x07;

                    if (wireType == 0)
                    {
                        long val = 0; int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            val |= (long)(b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        if (fieldNum == 4) result.BackoffTimeMs = (int)val;
                    }
                    else if (wireType == 2)
                    {
                        int len = 0; int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }

                        if (fieldNum == 7 && len > 0 && idx + len <= data.Length)
                        {
                            result.PlaybackCookie = new byte[len];
                            Buffer.BlockCopy(data, idx, result.PlaybackCookie, 0, len);
                        }
                        idx += len;
                    }
                    else if (wireType == 1) { idx += 8; }
                    else if (wireType == 5) { idx += 4; }
                    else break;
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Extracts the playback cookie bytes from a NEXT_REQUEST_POLICY (type 35) protobuf payload.
        /// Wire format: field 7 (PlaybackCookie) wireType 2: tag = (7 << 3) | 2 = 0x3A (58)
        /// </summary>
        public static byte[] ExtractPlaybackCookie(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                int idx = 0;
                while (idx < data.Length)
                {
                    int tag = data[idx++];
                    int fieldNum = tag >> 3;
                    int wireType = tag & 0x07;

                    if (wireType == 0)
                    {
                        while (idx < data.Length && (data[idx++] & 0x80) != 0) { }
                    }
                    else if (wireType == 2)
                    {
                        int len = 0;
                        int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }

                        if (fieldNum == 7 && len > 0 && idx + len <= data.Length)
                        {
                            byte[] cookie = new byte[len];
                            Buffer.BlockCopy(data, idx, cookie, 0, len);
                            return cookie;
                        }
                        idx += len;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Extracts the status integer from a STREAM_PROTECTION_STATUS (type 58) protobuf payload.
        /// Protobuf schema: optional int32 status = 1 (tag 0x08).
        /// Values: 1 = OK (verified/authorized), 2 = Attestation pending, 3 = Attestation required.
        /// </summary>
        public static int ExtractStreamProtectionStatus(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            try
            {
                int idx = 0;
                while (idx < data.Length)
                {
                    byte tag = data[idx++];
                    int fieldNum = tag >> 3;
                    int wireType = tag & 0x07;

                    if (fieldNum == 1 && wireType == 0)
                    {
                        long val = 0; int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            val |= (long)(b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        return (int)val;
                    }
                    else if (wireType == 0)
                    {
                        while (idx < data.Length && (data[idx++] & 0x80) != 0) { }
                    }
                    else if (wireType == 2)
                    {
                        int len = 0; int shift = 0;
                        while (idx < data.Length)
                        {
                            byte b = data[idx++];
                            len |= (b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }
                        idx += len;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}
