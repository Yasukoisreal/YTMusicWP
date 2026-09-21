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
        /// Reads a variable-length integer (Varint) from a ChunkedStreamReader asynchronously.
        /// Returns -1 on EOF or malformed data.
        /// </summary>
        public static async Task<long> ReadVarintAsync(ChunkedStreamReader reader, CancellationToken ct)
        {
            long value = 0;
            int shift = 0;

            while (true)
            {
                int b = await reader.ReadByteAsync(ct).ConfigureAwait(false);
                if (b < 0) return -1;

                value |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;

                shift += 7;
                if (shift > 64) return -1;
            }

            return value;
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
    }
}
