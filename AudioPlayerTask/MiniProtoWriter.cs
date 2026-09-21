using System;
using System.IO;
using System.Text;

namespace AudioPlayerTask
{
    /// <summary>
    /// Ultra-lightweight, zero-allocation Protocol Buffers wire-format encoder in pure C# 6.0.
    /// Provides zero-dependency binary serialization for YouTube SABR (VideoPlaybackAbrRequest).
    /// </summary>
    internal sealed class MiniProtoWriter : IDisposable
    {
        private MemoryStream _ms;

        public MiniProtoWriter()
        {
            _ms = new MemoryStream();
        }

        public MiniProtoWriter(int initialCapacity)
        {
            _ms = new MemoryStream(initialCapacity);
        }

        public void Dispose()
        {
            if (_ms != null)
            {
                _ms.Dispose();
                _ms = null;
            }
        }

        public byte[] ToByteArray()
        {
            return _ms.ToArray();
        }

        public void WriteTag(int fieldNumber, int wireType)
        {
            uint tag = (uint)((fieldNumber << 3) | (wireType & 0x07));
            WriteVarint(tag);
        }

        public void WriteVarint(ulong value)
        {
            while (value >= 0x80)
            {
                _ms.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            _ms.WriteByte((byte)(value & 0x7F));
        }

        public void WriteVarintField(int fieldNumber, ulong value)
        {
            WriteTag(fieldNumber, 0);
            WriteVarint(value);
        }

        public void WriteVarintField(int fieldNumber, long value)
        {
            WriteVarintField(fieldNumber, (ulong)value);
        }

        public void WriteBoolField(int fieldNumber, bool value)
        {
            WriteVarintField(fieldNumber, value ? 1UL : 0UL);
        }

        public void WriteFixed32Field(int fieldNumber, uint value)
        {
            WriteTag(fieldNumber, 5);
            byte[] bytes = BitConverter.GetBytes(value);
            _ms.Write(bytes, 0, 4);
        }

        public void WriteFloatField(int fieldNumber, float value)
        {
            WriteTag(fieldNumber, 5);
            byte[] bytes = BitConverter.GetBytes(value);
            _ms.Write(bytes, 0, 4);
        }

        public void WriteBytesField(int fieldNumber, byte[] data)
        {
            if (data == null) return;
            WriteTag(fieldNumber, 2);
            WriteVarint((ulong)data.Length);
            _ms.Write(data, 0, data.Length);
        }

        public void WriteStringField(int fieldNumber, string str)
        {
            if (string.IsNullOrEmpty(str)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            WriteBytesField(fieldNumber, bytes);
        }

        public void WriteSubMessage(int fieldNumber, byte[] subMessageBytes)
        {
            if (subMessageBytes == null || subMessageBytes.Length == 0) return;
            WriteTag(fieldNumber, 2);
            WriteVarint((ulong)subMessageBytes.Length);
            _ms.Write(subMessageBytes, 0, subMessageBytes.Length);
        }

        /// <summary>
        /// Encodes a YouTube VideoPlaybackAbrRequest protobuf payload targeting audio-only playback.
        /// </summary>
        /// <param name="ustreamerConfig">The raw signed ustreamerConfig bytes from YouTube.</param>
        /// <param name="playerTimeMs">Current playback offset in milliseconds.</param>
        /// <param name="playbackRate">Playback rate (e.g. 1.0f).</param>
        /// <param name="preferredItag">Preferred audio itag (e.g. 140 for AAC or 251 for Opus).</param>
        /// <param name="clientNameInt">InnerTube client ID integer (e.g. 3 for ANDROID, 1 for WEB, 67 for WEB_REMIX, 101 for VISIONOS).</param>
        /// <param name="clientVersion">Client version string.</param>
        /// <param name="poToken">Optional PO token byte array.</param>
        /// <returns>Encoded protobuf byte array.</returns>
        public static byte[] BuildAudioAbrRequest(
            byte[] ustreamerConfig,
            long playerTimeMs = 0,
            float playbackRate = 1.0f,
            int preferredItag = 140,
            int clientNameInt = 3,
            string clientVersion = "19.29.35",
            byte[] poToken = null)
        {
            using (var requestWriter = new MiniProtoWriter(512))
            {
                // 1. ClientAbrState submessage (Field 1)
                byte[] clientAbrStateBytes;
                using (var stateWriter = new MiniProtoWriter(128))
                {
                    // Field 21: sticky_resolution = 360
                    stateWriter.WriteVarintField(21, 360);

                    // Field 22: client_viewport_is_flexible = false
                    stateWriter.WriteBoolField(22, false);

                    // Field 28: player_time_ms
                    stateWriter.WriteVarintField(28, (ulong)playerTimeMs);

                    // Field 34: visibility = 1
                    stateWriter.WriteVarintField(34, 1);

                    // Field 35: playback_rate = 1.0f
                    stateWriter.WriteFloatField(35, playbackRate);

                    // Field 40: enabled_track_types_bitfield = 1 (1 = Audio Only, 2 = Video Only, 0 = Both)
                    stateWriter.WriteVarintField(40, 1);

                    clientAbrStateBytes = stateWriter.ToByteArray();
                }
                requestWriter.WriteSubMessage(1, clientAbrStateBytes);

                // 2. Field 5: video_playback_ustreamer_config (Raw bytes)
                if (ustreamerConfig != null && ustreamerConfig.Length > 0)
                {
                    requestWriter.WriteBytesField(5, ustreamerConfig);
                }

                // 3. Field 16: preferred_audio_format_ids (FormatId submessage: field 1 = itag)
                if (preferredItag > 0)
                {
                    byte[] formatIdBytes;
                    using (var fmtWriter = new MiniProtoWriter(16))
                    {
                        fmtWriter.WriteVarintField(1, (ulong)preferredItag);
                        formatIdBytes = fmtWriter.ToByteArray();
                    }
                    requestWriter.WriteSubMessage(16, formatIdBytes);
                }

                // 4. Field 19: streamer_context
                byte[] streamerContextBytes;
                using (var ctxWriter = new MiniProtoWriter(256))
                {
                    // Submessage 1: ClientInfo
                    byte[] clientInfoBytes;
                    using (var infoWriter = new MiniProtoWriter(128))
                    {
                        if (clientNameInt > 0)
                        {
                            infoWriter.WriteVarintField(16, (ulong)clientNameInt);
                        }
                        if (!string.IsNullOrEmpty(clientVersion))
                        {
                            infoWriter.WriteStringField(17, clientVersion);
                        }
                        clientInfoBytes = infoWriter.ToByteArray();
                    }
                    ctxWriter.WriteSubMessage(1, clientInfoBytes);

                    // Field 2: po_token (bytes)
                    if (poToken != null && poToken.Length > 0)
                    {
                        ctxWriter.WriteBytesField(2, poToken);
                    }

                    streamerContextBytes = ctxWriter.ToByteArray();
                }
                requestWriter.WriteSubMessage(19, streamerContextBytes);

                return requestWriter.ToByteArray();
            }
        }

        /// <summary>
        /// Safely decodes a Base64 or Base64Url-encoded string into raw bytes.
        /// Handles URL-safe replacement ('-' to '+', '_' to '/') and missing '=' padding.
        /// </summary>
        public static byte[] Base64UrlDecode(string input)
        {
            if (string.IsNullOrEmpty(input)) return new byte[0];
            try
            {
                string standard = input.Replace('-', '+').Replace('_', '/');
                int rem = standard.Length % 4;
                if (rem > 0)
                {
                    standard += new string('=', 4 - rem);
                }
                return Convert.FromBase64String(standard);
            }
            catch
            {
                return Encoding.UTF8.GetBytes(input);
            }
        }
    }
}
