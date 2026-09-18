using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YTMusicWP.Services.ListenTogether
{
    public static class MessageTypes
    {
        public const string CreateRoom = "create_room";
        public const string JoinRoom = "join_room";
        public const string LeaveRoom = "leave_room";
        public const string ApproveJoin = "approve_join";
        public const string RejectJoin = "reject_join";
        public const string PlaybackAction = "playback_action";
        public const string BufferReady = "buffer_ready";
        public const string KickUser = "kick_user";
        public const string TransferHost = "transfer_host";
        public const string Ping = "ping";
        public const string RequestSync = "request_sync";
        public const string Reconnect = "reconnect";
        public const string ClientCapabilities = "client_capabilities";

        // Server -> Client
        public const string RoomCreated = "room_created";
        public const string JoinRequest = "join_request";
        public const string JoinApproved = "join_approved";
        public const string JoinRejected = "join_rejected";
        public const string UserJoined = "user_joined";
        public const string UserLeft = "user_left";
        public const string SyncPlayback = "sync_playback";
        public const string BufferWait = "buffer_wait";
        public const string BufferComplete = "buffer_complete";
        public const string Error = "error";
        public const string Pong = "pong";
        public const string HostChanged = "host_changed";
        public const string Kicked = "kicked";
        public const string SyncState = "sync_state";
        public const string Reconnected = "reconnected";
        public const string UserReconnected = "user_reconnected";
        public const string UserDisconnected = "user_disconnected";
        public const string ServerCapabilities = "server_capabilities";
    }

    public static class PlaybackActions
    {
        public const string Play = "play";
        public const string Pause = "pause";
        public const string Seek = "seek";
        public const string SkipNext = "skip_next";
        public const string SkipPrev = "skip_prev";
        public const string ChangeTrack = "change_track";
        public const string SyncQueue = "sync_queue";
    }

    #region Models

    public class Envelope
    {
        public string Type { get; set; } = "";
        public byte[] Payload { get; set; } = new byte[0];
        public bool Compressed { get; set; }
    }

    public class ClientCapabilities
    {
        public bool SupportsProtobuf { get; set; } = true;
        public bool SupportsCompression { get; set; } = false;
        public string ClientVersion { get; set; } = "YTMusicWP-2.2";
    }

    public class ServerCapabilities
    {
        public bool SupportsProtobuf { get; set; }
        public bool SupportsCompression { get; set; }
        public string ServerVersion { get; set; } = "";
    }

    public class TrackInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public long Duration { get; set; }
        public string Thumbnail { get; set; } = "";
        public string SuggestedBy { get; set; } = "";
    }

    public class UserInfo
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
        public bool IsHost { get; set; }
        public bool IsConnected { get; set; }
    }

    public class RoomState
    {
        public string RoomCode { get; set; } = "";
        public string HostId { get; set; } = "";
        public List<UserInfo> Users { get; set; } = new List<UserInfo>();
        public TrackInfo CurrentTrack { get; set; }
        public bool IsPlaying { get; set; }
        public long Position { get; set; }
        public long LastUpdate { get; set; }
        public float Volume { get; set; }
        public List<TrackInfo> Queue { get; set; } = new List<TrackInfo>();
        public long Revision { get; set; }
    }

    public class CreateRoomPayload
    {
        public string Username { get; set; } = "";
    }

    public class RoomCreatedPayload
    {
        public string RoomCode { get; set; } = "";
        public string UserId { get; set; } = "";
        public string SessionToken { get; set; } = "";
    }

    public class JoinRoomPayload
    {
        public string RoomCode { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class JoinRequestPayload
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class ApproveJoinPayload
    {
        public string UserId { get; set; } = "";
    }

    public class RejectJoinPayload
    {
        public string UserId { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public class JoinApprovedPayload
    {
        public string RoomCode { get; set; } = "";
        public string UserId { get; set; } = "";
        public string SessionToken { get; set; } = "";
        public RoomState State { get; set; }
    }

    public class JoinRejectedPayload
    {
        public string Reason { get; set; } = "";
    }

    public class UserJoinedPayload
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class UserLeftPayload
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class UserDisconnectedPayload
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class UserReconnectedPayload
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class PingPayload
    {
        public long ClientTime { get; set; }
        public long Sequence { get; set; }
    }

    public class PongPayload
    {
        public long ClientTime { get; set; }
        public long ServerReceiveTime { get; set; }
        public long ServerSendTime { get; set; }
        public long Sequence { get; set; }
    }

    public class BufferWaitPayload
    {
        public string TrackId { get; set; } = "";
        public List<string> WaitingFor { get; set; } = new List<string>();
    }

    public class BufferReadyPayload
    {
        public string TrackId { get; set; } = "";
    }

    public class BufferCompletePayload
    {
        public string TrackId { get; set; } = "";
    }

    public class PlaybackActionPayload
    {
        public string Action { get; set; } = "";
        public string TrackId { get; set; } = "";
        public long Position { get; set; }
        public TrackInfo TrackInfo { get; set; }
        public bool InsertNext { get; set; }
        public List<TrackInfo> Queue { get; set; } = new List<TrackInfo>();
        public string QueueTitle { get; set; } = "";
        public float Volume { get; set; }
        public long ServerTime { get; set; }
        public long Revision { get; set; }
        public long CapturedAtServerTime { get; set; }
    }

    public class SyncStatePayload
    {
        public TrackInfo CurrentTrack { get; set; }
        public bool IsPlaying { get; set; }
        public long Position { get; set; }
        public long LastUpdate { get; set; }
        public List<TrackInfo> Queue { get; set; } = new List<TrackInfo>();
        public float Volume { get; set; }
        public long Revision { get; set; }
    }

    public class ErrorPayload
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class KickUserPayload
    {
        public string UserId { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public class KickedPayload
    {
        public string Reason { get; set; } = "";
    }

    public class TransferHostPayload
    {
        public string NewHostId { get; set; } = "";
    }

    public class HostChangedPayload
    {
        public string NewHostId { get; set; } = "";
        public string NewHostName { get; set; } = "";
    }

    public class ReconnectPayload
    {
        public string SessionToken { get; set; } = "";
    }

    #endregion

    #region Low-level Protobuf Stream Reader & Writer

    public static class ProtobufStream
    {
        public static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)(value & 0x7F));
        }

        public static ulong ReadVarint(Stream stream)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1) throw new EndOfStreamException();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift >= 64) throw new FormatException("Varint exceeds 64 bits");
            }
            return result;
        }

        public static void WriteTag(Stream stream, int fieldNumber, int wireType)
        {
            WriteVarint(stream, (ulong)((fieldNumber << 3) | (wireType & 0x07)));
        }

        public static void WriteString(Stream stream, int fieldNumber, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteTag(stream, fieldNumber, 2);
            WriteVarint(stream, (ulong)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        public static void WriteBytes(Stream stream, int fieldNumber, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            WriteTag(stream, fieldNumber, 2);
            WriteVarint(stream, (ulong)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        public static void WriteInt64(Stream stream, int fieldNumber, long value)
        {
            if (value == 0) return;
            WriteTag(stream, fieldNumber, 0);
            WriteVarint(stream, (ulong)value);
        }

        public static void WriteBool(Stream stream, int fieldNumber, bool value)
        {
            if (!value) return;
            WriteTag(stream, fieldNumber, 0);
            stream.WriteByte(1);
        }

        public static void WriteFloat(Stream stream, int fieldNumber, float value)
        {
            if (Math.Abs(value) < 0.00001f) return;
            WriteTag(stream, fieldNumber, 5);
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, 4);
        }

        public static void WriteMessage(Stream stream, int fieldNumber, byte[] messageBytes)
        {
            if (messageBytes == null || messageBytes.Length == 0) return;
            WriteTag(stream, fieldNumber, 2);
            WriteVarint(stream, (ulong)messageBytes.Length);
            stream.Write(messageBytes, 0, messageBytes.Length);
        }

        public static void SkipField(Stream stream, int wireType)
        {
            switch (wireType)
            {
                case 0: // Varint
                    ReadVarint(stream);
                    break;
                case 1: // 64-bit
                    stream.Seek(8, SeekOrigin.Current);
                    break;
                case 2: // Length-delimited
                    long length = (long)ReadVarint(stream);
                    stream.Seek(length, SeekOrigin.Current);
                    break;
                case 5: // 32-bit
                    stream.Seek(4, SeekOrigin.Current);
                    break;
                default:
                    throw new FormatException("Unknown protobuf wire type: " + wireType);
            }
        }

        public static string ReadString(Stream stream)
        {
            int length = (int)ReadVarint(stream);
            if (length == 0) return "";
            byte[] buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int chunk = stream.Read(buffer, read, length - read);
                if (chunk <= 0) break;
                read += chunk;
            }
            return Encoding.UTF8.GetString(buffer, 0, read);
        }

        public static byte[] ReadBytes(Stream stream)
        {
            int length = (int)ReadVarint(stream);
            if (length == 0) return new byte[0];
            byte[] buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int chunk = stream.Read(buffer, read, length - read);
                if (chunk <= 0) break;
                read += chunk;
            }
            return buffer;
        }

        public static float ReadFloat(Stream stream)
        {
            byte[] buffer = new byte[4];
            stream.Read(buffer, 0, 4);
            return BitConverter.ToSingle(buffer, 0);
        }
    }

    #endregion

    #region Serialization & Deserialization

    public static class ProtobufCodec
    {
        public static byte[] EncodeEnvelope(string msgType, byte[] payloadBytes, bool compressed = false)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, msgType);
                ProtobufStream.WriteBytes(ms, 2, payloadBytes);
                ProtobufStream.WriteBool(ms, 3, compressed);
                return ms.ToArray();
            }
        }

        public static Envelope DecodeEnvelope(byte[] data)
        {
            var env = new Envelope();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);

                    switch (fieldNum)
                    {
                        case 1:
                            env.Type = ProtobufStream.ReadString(ms);
                            break;
                        case 2:
                            env.Payload = ProtobufStream.ReadBytes(ms);
                            break;
                        case 3:
                            env.Compressed = ProtobufStream.ReadVarint(ms) != 0;
                            break;
                        default:
                            ProtobufStream.SkipField(ms, wireType);
                            break;
                    }
                }
            }
            return env;
        }

        public static byte[] EncodeClientCapabilities(ClientCapabilities caps)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteBool(ms, 1, caps.SupportsProtobuf);
                ProtobufStream.WriteBool(ms, 2, caps.SupportsCompression);
                ProtobufStream.WriteString(ms, 3, caps.ClientVersion);
                return ms.ToArray();
            }
        }

        public static ServerCapabilities DecodeServerCapabilities(byte[] data)
        {
            var caps = new ServerCapabilities();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: caps.SupportsProtobuf = ProtobufStream.ReadVarint(ms) != 0; break;
                        case 2: caps.SupportsCompression = ProtobufStream.ReadVarint(ms) != 0; break;
                        case 3: caps.ServerVersion = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return caps;
        }

        public static byte[] EncodeCreateRoom(string username)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, username);
                return ms.ToArray();
            }
        }

        public static RoomCreatedPayload DecodeRoomCreated(byte[] data)
        {
            var p = new RoomCreatedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.RoomCode = ProtobufStream.ReadString(ms); break;
                        case 2: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 3: p.SessionToken = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodeJoinRoom(string roomCode, string username)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, roomCode);
                ProtobufStream.WriteString(ms, 2, username);
                return ms.ToArray();
            }
        }

        public static JoinRequestPayload DecodeJoinRequest(byte[] data)
        {
            var p = new JoinRequestPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 2: p.Username = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodeApproveJoin(string userId)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, userId);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeRejectJoin(string userId, string reason)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, userId);
                ProtobufStream.WriteString(ms, 2, reason);
                return ms.ToArray();
            }
        }

        public static JoinApprovedPayload DecodeJoinApproved(byte[] data)
        {
            var p = new JoinApprovedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.RoomCode = ProtobufStream.ReadString(ms); break;
                        case 2: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 3: p.SessionToken = ProtobufStream.ReadString(ms); break;
                        case 4: p.State = DecodeRoomState(ProtobufStream.ReadBytes(ms)); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static JoinRejectedPayload DecodeJoinRejected(byte[] data)
        {
            var p = new JoinRejectedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.Reason = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static UserJoinedPayload DecodeUserJoined(byte[] data)
        {
            var p = new UserJoinedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 2: p.Username = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static UserLeftPayload DecodeUserLeft(byte[] data)
        {
            var p = new UserLeftPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 2: p.Username = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static UserDisconnectedPayload DecodeUserDisconnected(byte[] data)
        {
            var p = new UserDisconnectedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 2: p.Username = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static UserReconnectedPayload DecodeUserReconnected(byte[] data)
        {
            var p = new UserReconnectedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.UserId = ProtobufStream.ReadString(ms); break;
                        case 2: p.Username = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodePing(long clientTime, long sequence)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteInt64(ms, 1, clientTime);
                ProtobufStream.WriteInt64(ms, 2, sequence);
                return ms.ToArray();
            }
        }

        public static PongPayload DecodePong(byte[] data)
        {
            var p = new PongPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.ClientTime = (long)ProtobufStream.ReadVarint(ms); break;
                        case 2: p.ServerReceiveTime = (long)ProtobufStream.ReadVarint(ms); break;
                        case 3: p.ServerSendTime = (long)ProtobufStream.ReadVarint(ms); break;
                        case 4: p.Sequence = (long)ProtobufStream.ReadVarint(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static BufferWaitPayload DecodeBufferWait(byte[] data)
        {
            var p = new BufferWaitPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.TrackId = ProtobufStream.ReadString(ms); break;
                        case 2: p.WaitingFor.Add(ProtobufStream.ReadString(ms)); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodeBufferReady(string trackId)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, trackId);
                return ms.ToArray();
            }
        }

        public static BufferCompletePayload DecodeBufferComplete(byte[] data)
        {
            var p = new BufferCompletePayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.TrackId = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodePlaybackAction(PlaybackActionPayload payload)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, payload.Action);
                ProtobufStream.WriteString(ms, 2, payload.TrackId);
                ProtobufStream.WriteInt64(ms, 3, payload.Position);
                if (payload.TrackInfo != null)
                {
                    ProtobufStream.WriteMessage(ms, 4, EncodeTrackInfo(payload.TrackInfo));
                }
                ProtobufStream.WriteBool(ms, 5, payload.InsertNext);
                if (payload.Queue != null)
                {
                    foreach (var track in payload.Queue)
                    {
                        ProtobufStream.WriteMessage(ms, 6, EncodeTrackInfo(track));
                    }
                }
                ProtobufStream.WriteString(ms, 7, payload.QueueTitle);
                ProtobufStream.WriteFloat(ms, 8, payload.Volume);
                ProtobufStream.WriteInt64(ms, 9, payload.ServerTime);
                ProtobufStream.WriteInt64(ms, 10, payload.Revision);
                ProtobufStream.WriteInt64(ms, 11, payload.CapturedAtServerTime);
                return ms.ToArray();
            }
        }

        public static PlaybackActionPayload DecodePlaybackAction(byte[] data)
        {
            var p = new PlaybackActionPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.Action = ProtobufStream.ReadString(ms); break;
                        case 2: p.TrackId = ProtobufStream.ReadString(ms); break;
                        case 3: p.Position = (long)ProtobufStream.ReadVarint(ms); break;
                        case 4: p.TrackInfo = DecodeTrackInfo(ProtobufStream.ReadBytes(ms)); break;
                        case 5: p.InsertNext = ProtobufStream.ReadVarint(ms) != 0; break;
                        case 6: p.Queue.Add(DecodeTrackInfo(ProtobufStream.ReadBytes(ms))); break;
                        case 7: p.QueueTitle = ProtobufStream.ReadString(ms); break;
                        case 8: p.Volume = ProtobufStream.ReadFloat(ms); break;
                        case 9: p.ServerTime = (long)ProtobufStream.ReadVarint(ms); break;
                        case 10: p.Revision = (long)ProtobufStream.ReadVarint(ms); break;
                        case 11: p.CapturedAtServerTime = (long)ProtobufStream.ReadVarint(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static SyncStatePayload DecodeSyncState(byte[] data)
        {
            var p = new SyncStatePayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.CurrentTrack = DecodeTrackInfo(ProtobufStream.ReadBytes(ms)); break;
                        case 2: p.IsPlaying = ProtobufStream.ReadVarint(ms) != 0; break;
                        case 3: p.Position = (long)ProtobufStream.ReadVarint(ms); break;
                        case 4: p.LastUpdate = (long)ProtobufStream.ReadVarint(ms); break;
                        case 5: p.Queue.Add(DecodeTrackInfo(ProtobufStream.ReadBytes(ms))); break;
                        case 6: p.Volume = ProtobufStream.ReadFloat(ms); break;
                        case 7: p.Revision = (long)ProtobufStream.ReadVarint(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodeTrackInfo(TrackInfo track)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, track.Id);
                ProtobufStream.WriteString(ms, 2, track.Title);
                ProtobufStream.WriteString(ms, 3, track.Artist);
                ProtobufStream.WriteString(ms, 4, track.Album);
                ProtobufStream.WriteInt64(ms, 5, track.Duration);
                ProtobufStream.WriteString(ms, 6, track.Thumbnail);
                ProtobufStream.WriteString(ms, 7, track.SuggestedBy);
                return ms.ToArray();
            }
        }

        public static TrackInfo DecodeTrackInfo(byte[] data)
        {
            var t = new TrackInfo();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: t.Id = ProtobufStream.ReadString(ms); break;
                        case 2: t.Title = ProtobufStream.ReadString(ms); break;
                        case 3: t.Artist = ProtobufStream.ReadString(ms); break;
                        case 4: t.Album = ProtobufStream.ReadString(ms); break;
                        case 5: t.Duration = (long)ProtobufStream.ReadVarint(ms); break;
                        case 6: t.Thumbnail = ProtobufStream.ReadString(ms); break;
                        case 7: t.SuggestedBy = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return t;
        }

        public static byte[] EncodeUserInfo(UserInfo u)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, u.UserId);
                ProtobufStream.WriteString(ms, 2, u.Username);
                ProtobufStream.WriteBool(ms, 3, u.IsHost);
                ProtobufStream.WriteBool(ms, 4, u.IsConnected);
                return ms.ToArray();
            }
        }

        public static UserInfo DecodeUserInfo(byte[] data)
        {
            var u = new UserInfo();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: u.UserId = ProtobufStream.ReadString(ms); break;
                        case 2: u.Username = ProtobufStream.ReadString(ms); break;
                        case 3: u.IsHost = ProtobufStream.ReadVarint(ms) != 0; break;
                        case 4: u.IsConnected = ProtobufStream.ReadVarint(ms) != 0; break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return u;
        }

        public static RoomState DecodeRoomState(byte[] data)
        {
            var s = new RoomState();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: s.RoomCode = ProtobufStream.ReadString(ms); break;
                        case 2: s.HostId = ProtobufStream.ReadString(ms); break;
                        case 3: s.Users.Add(DecodeUserInfo(ProtobufStream.ReadBytes(ms))); break;
                        case 4: s.CurrentTrack = DecodeTrackInfo(ProtobufStream.ReadBytes(ms)); break;
                        case 5: s.IsPlaying = ProtobufStream.ReadVarint(ms) != 0; break;
                        case 6: s.Position = (long)ProtobufStream.ReadVarint(ms); break;
                        case 7: s.LastUpdate = (long)ProtobufStream.ReadVarint(ms); break;
                        case 8: s.Volume = ProtobufStream.ReadFloat(ms); break;
                        case 9: s.Queue.Add(DecodeTrackInfo(ProtobufStream.ReadBytes(ms))); break;
                        case 10: s.Revision = (long)ProtobufStream.ReadVarint(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return s;
        }

        public static byte[] EncodeKickUser(string userId, string reason = "")
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, userId);
                ProtobufStream.WriteString(ms, 2, reason);
                return ms.ToArray();
            }
        }

        public static KickedPayload DecodeKicked(byte[] data)
        {
            var p = new KickedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.Reason = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodeTransferHost(string newHostId)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, newHostId);
                return ms.ToArray();
            }
        }

        public static HostChangedPayload DecodeHostChanged(byte[] data)
        {
            var p = new HostChangedPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.NewHostId = ProtobufStream.ReadString(ms); break;
                        case 2: p.NewHostName = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }

        public static byte[] EncodeReconnect(string sessionToken)
        {
            using (var ms = new MemoryStream())
            {
                ProtobufStream.WriteString(ms, 1, sessionToken);
                return ms.ToArray();
            }
        }

        public static ErrorPayload DecodeError(byte[] data)
        {
            var p = new ErrorPayload();
            using (var ms = new MemoryStream(data))
            {
                while (ms.Position < ms.Length)
                {
                    ulong tag = ProtobufStream.ReadVarint(ms);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x07);
                    switch (fieldNum)
                    {
                        case 1: p.Code = ProtobufStream.ReadString(ms); break;
                        case 2: p.Message = ProtobufStream.ReadString(ms); break;
                        default: ProtobufStream.SkipField(ms, wireType); break;
                    }
                }
            }
            return p;
        }
    }

    #endregion
}
