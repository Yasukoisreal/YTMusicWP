using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Pure C# MP4/M4A ISO Base Media File Format metadata tagger.
    /// Injects an iTunes-compatible 'udta' box (moov -> udta -> meta -> ilst)
    /// containing Title (©nam), Artist (©ART), Album (©alb), and Cover Art (covr).
    /// Dynamically shifts sample chunk offsets in 'stco' and 'co64' when 'moov' precedes 'mdat'.
    /// </summary>
    public static class M4aMetadataWriter
    {
        public static async Task<bool> WriteMetadataAsync(
            StorageFile targetFile,
            string title,
            string artist,
            string album,
            byte[] coverBytes)
        {
            if (targetFile == null) return false;

            StorageFile tempFile = null;
            try
            {
                // 1. Build udta box in memory
                byte[] udtaBox = BuildUdtaBox(title, artist, album, coverBytes);
                if (udtaBox == null || udtaBox.Length == 0) return false;

                // 2. Open source file stream
                using (var winrtSrcStream = await targetFile.OpenReadAsync())
                using (var srcStream = winrtSrcStream.AsStreamForRead())
                {
                    long fileSize = srcStream.Length;
                    if (fileSize < 64) return false;

                    long moovOffset = -1;
                    long moovSize = 0;
                    long mdatOffset = -1;
                    long mdatSize = 0;

                    byte[] headerBuf = new byte[8];
                    while (srcStream.Position + 8 <= fileSize)
                    {
                        long curPos = srcStream.Position;
                        int read = await srcStream.ReadAsync(headerBuf, 0, 8);
                        if (read < 8) break;

                        uint bSize = ReadUInt32BE(headerBuf, 0);
                        long actualBoxSize = bSize;

                        if (bSize == 1) // 64-bit box size
                        {
                            byte[] extBuf = new byte[8];
                            if (await srcStream.ReadAsync(extBuf, 0, 8) < 8) break;
                            actualBoxSize = (long)ReadUInt64BE(extBuf, 0);
                        }
                        else if (bSize == 0) // Box extends to end of file
                        {
                            actualBoxSize = fileSize - curPos;
                        }

                        if (actualBoxSize < 8) break;

                        // Check fourcc
                        if (headerBuf[4] == 'm' && headerBuf[5] == 'o' && headerBuf[6] == 'o' && headerBuf[7] == 'v')
                        {
                            moovOffset = curPos;
                            moovSize = actualBoxSize;
                        }
                        else if (headerBuf[4] == 'm' && headerBuf[5] == 'd' && headerBuf[6] == 'a' && headerBuf[7] == 't')
                        {
                            mdatOffset = curPos;
                            mdatSize = actualBoxSize;
                        }

                        long nextPos = curPos + actualBoxSize;
                        if (nextPos >= fileSize || nextPos <= curPos) break;
                        srcStream.Seek(nextPos, SeekOrigin.Begin);
                    }

                    if (moovOffset < 0 || moovSize <= 8 || moovSize > 20 * 1024 * 1024)
                    {
                        // moov not found or unreasonably large for audio metadata
                        return false;
                    }

                    // 3. Read moov box into memory
                    srcStream.Seek(moovOffset, SeekOrigin.Begin);
                    byte[] moovBytes = new byte[moovSize];
                    int moovRead = 0;
                    while (moovRead < moovSize)
                    {
                        int r = await srcStream.ReadAsync(moovBytes, moovRead, (int)moovSize - moovRead);
                        if (r <= 0) break;
                        moovRead += r;
                    }
                    if (moovRead < moovSize) return false;

                    // 4. Check if moov already contains an existing udta box
                    int oldUdtaOffsetInMoov = -1;
                    int oldUdtaSize = 0;
                    int subPos = 8; // skip moov header (size + 'moov')
                    while (subPos + 8 <= moovBytes.Length)
                    {
                        uint subSize = ReadUInt32BE(moovBytes, subPos);
                        if (subSize < 8 || subPos + subSize > moovBytes.Length) break;

                        if (moovBytes[subPos + 4] == 'u' && moovBytes[subPos + 5] == 'd' &&
                            moovBytes[subPos + 6] == 't' && moovBytes[subPos + 7] == 'a')
                        {
                            oldUdtaOffsetInMoov = subPos;
                            oldUdtaSize = (int)subSize;
                            break;
                        }
                        subPos += (int)subSize;
                    }

                    long delta = (long)udtaBox.Length - oldUdtaSize;

                    // 5. If moov comes before mdat, shift stco/co64 sample chunk offsets
                    if (moovOffset < mdatOffset && delta != 0)
                    {
                        ShiftSampleOffsets(moovBytes, mdatOffset, delta);
                    }

                    // 6. Update moov 4-byte size
                    uint newMoovSize = (uint)(moovSize + delta);
                    WriteUInt32BE(moovBytes, 0, newMoovSize);

                    // 7. Assemble new moov box
                    byte[] newMoovBytes = new byte[newMoovSize];
                    if (oldUdtaOffsetInMoov >= 0)
                    {
                        // Replace existing udta
                        Buffer.BlockCopy(moovBytes, 0, newMoovBytes, 0, oldUdtaOffsetInMoov);
                        Buffer.BlockCopy(udtaBox, 0, newMoovBytes, oldUdtaOffsetInMoov, udtaBox.Length);
                        int postOldUdtaOffset = oldUdtaOffsetInMoov + oldUdtaSize;
                        int tailLen = (int)moovSize - postOldUdtaOffset;
                        if (tailLen > 0)
                        {
                            Buffer.BlockCopy(moovBytes, postOldUdtaOffset, newMoovBytes, oldUdtaOffsetInMoov + udtaBox.Length, tailLen);
                        }
                    }
                    else
                    {
                        // Append udta at the end of moov
                        Buffer.BlockCopy(moovBytes, 0, newMoovBytes, 0, (int)moovSize);
                        Buffer.BlockCopy(udtaBox, 0, newMoovBytes, (int)moovSize, udtaBox.Length);
                    }

                    // 8. Write to temporary file
                    string tempName = targetFile.Name + ".tagging";
                    tempFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(tempName, CreationCollisionOption.ReplaceExisting);

                    using (var winrtDstStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                    using (var dstStream = winrtDstStream.AsStreamForWrite())
                    {
                        // Write everything before moov
                        srcStream.Seek(0, SeekOrigin.Begin);
                        byte[] copyBuf = new byte[64 * 1024];
                        long bytesBeforeMoov = moovOffset;
                        while (bytesBeforeMoov > 0)
                        {
                            int toRead = (int)Math.Min(copyBuf.Length, bytesBeforeMoov);
                            int r = await srcStream.ReadAsync(copyBuf, 0, toRead);
                            if (r <= 0) break;
                            await dstStream.WriteAsync(copyBuf, 0, r);
                            bytesBeforeMoov -= r;
                        }

                        // Write new moov
                        await dstStream.WriteAsync(newMoovBytes, 0, newMoovBytes.Length);

                        // Write everything after moov
                        srcStream.Seek(moovOffset + moovSize, SeekOrigin.Begin);
                        long bytesAfterMoov = fileSize - (moovOffset + moovSize);
                        while (bytesAfterMoov > 0)
                        {
                            int toRead = (int)Math.Min(copyBuf.Length, bytesAfterMoov);
                            int r = await srcStream.ReadAsync(copyBuf, 0, toRead);
                            if (r <= 0) break;
                            await dstStream.WriteAsync(copyBuf, 0, r);
                            bytesAfterMoov -= r;
                        }
                        await dstStream.FlushAsync();
                    }
                }

                // 9. Atomic replace
                await tempFile.MoveAndReplaceAsync(targetFile);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[M4aMetadataWriter] WriteMetadataAsync error: " + ex.Message);
                if (tempFile != null)
                {
                    try { await tempFile.DeleteAsync(); } catch { }
                }
                return false;
            }
        }

        private static void ShiftSampleOffsets(byte[] moovBytes, long mdatOffset, long delta)
        {
            // Scan for 'stco' (32-bit chunk offsets)
            for (int i = 4; i <= moovBytes.Length - 12; i++)
            {
                if (moovBytes[i] == 's' && moovBytes[i + 1] == 't' &&
                    moovBytes[i + 2] == 'c' && moovBytes[i + 3] == 'o')
                {
                    uint entryCount = ReadUInt32BE(moovBytes, i + 8);
                    int offsetStart = i + 12;
                    if (offsetStart + entryCount * 4 <= moovBytes.Length)
                    {
                        for (int e = 0; e < entryCount; e++)
                        {
                            int curOffPos = offsetStart + e * 4;
                            uint offsetVal = ReadUInt32BE(moovBytes, curOffPos);
                            if (offsetVal >= (uint)mdatOffset)
                            {
                                offsetVal = (uint)(offsetVal + delta);
                                WriteUInt32BE(moovBytes, curOffPos, offsetVal);
                            }
                        }
                    }
                }
                else if (moovBytes[i] == 'c' && moovBytes[i + 1] == 'o' &&
                         moovBytes[i + 2] == '6' && moovBytes[i + 3] == '4')
                {
                    uint entryCount = ReadUInt32BE(moovBytes, i + 8);
                    int offsetStart = i + 12;
                    if (offsetStart + entryCount * 8 <= moovBytes.Length)
                    {
                        for (int e = 0; e < entryCount; e++)
                        {
                            int curOffPos = offsetStart + e * 8;
                            ulong offsetVal = ReadUInt64BE(moovBytes, curOffPos);
                            if (offsetVal >= (ulong)mdatOffset)
                            {
                                offsetVal = (ulong)((long)offsetVal + delta);
                                WriteUInt64BE(moovBytes, curOffPos, offsetVal);
                            }
                        }
                    }
                }
            }
        }

        private static byte[] BuildUdtaBox(string title, string artist, string album, byte[] coverBytes)
        {
            using (var ilstStream = new MemoryStream())
            {
                // ©nam (Title)
                if (!string.IsNullOrEmpty(title))
                {
                    byte[] titleBytes = Encoding.UTF8.GetBytes(title);
                    byte[] dataBox = CreateDataBox(titleBytes, 1);
                    byte[] tagBox = CreateTagBox(new byte[] { 0xA9, (byte)'n', (byte)'a', (byte)'m' }, dataBox);
                    ilstStream.Write(tagBox, 0, tagBox.Length);
                }

                // ©ART (Artist)
                if (!string.IsNullOrEmpty(artist))
                {
                    byte[] artistBytes = Encoding.UTF8.GetBytes(artist);
                    byte[] dataBox = CreateDataBox(artistBytes, 1);
                    byte[] tagBox = CreateTagBox(new byte[] { 0xA9, (byte)'A', (byte)'R', (byte)'T' }, dataBox);
                    ilstStream.Write(tagBox, 0, tagBox.Length);
                }

                // ©alb (Album)
                string albumName = string.IsNullOrEmpty(album) ? "YTMusicWP" : album;
                byte[] albumBytes = Encoding.UTF8.GetBytes(albumName);
                byte[] dataBoxAlb = CreateDataBox(albumBytes, 1);
                byte[] tagBoxAlb = CreateTagBox(new byte[] { 0xA9, (byte)'a', (byte)'l', (byte)'b' }, dataBoxAlb);
                ilstStream.Write(tagBoxAlb, 0, tagBoxAlb.Length);

                // covr (Album Artwork)
                if (coverBytes != null && coverBytes.Length > 0)
                {
                    // Flag 14 = PNG, Flag 13 = JPEG
                    bool isPng = coverBytes.Length > 4 && coverBytes[0] == 0x89 && coverBytes[1] == 0x50 && coverBytes[2] == 0x4E && coverBytes[3] == 0x47;
                    uint typeFlag = isPng ? 14u : 13u;
                    byte[] dataBoxCovr = CreateDataBox(coverBytes, typeFlag);
                    byte[] tagBoxCovr = CreateTagBox(new byte[] { (byte)'c', (byte)'o', (byte)'v', (byte)'r' }, dataBoxCovr);
                    ilstStream.Write(tagBoxCovr, 0, tagBoxCovr.Length);
                }

                byte[] ilstContent = ilstStream.ToArray();
                byte[] ilstBox = new byte[8 + ilstContent.Length];
                WriteUInt32BE(ilstBox, 0, (uint)ilstBox.Length);
                ilstBox[4] = (byte)'i'; ilstBox[5] = (byte)'l'; ilstBox[6] = (byte)'s'; ilstBox[7] = (byte)'t';
                Buffer.BlockCopy(ilstContent, 0, ilstBox, 8, ilstContent.Length);

                // hdlr box (33 bytes)
                byte[] hdlrBox = CreateHdlrBox();

                // meta box (FullBox: size(4) + 'meta'(4) + ver/flags(4) + hdlr + ilst)
                int metaSize = 12 + hdlrBox.Length + ilstBox.Length;
                byte[] metaBox = new byte[metaSize];
                WriteUInt32BE(metaBox, 0, (uint)metaSize);
                metaBox[4] = (byte)'m'; metaBox[5] = (byte)'e'; metaBox[6] = (byte)'t'; metaBox[7] = (byte)'a';
                metaBox[8] = 0; metaBox[9] = 0; metaBox[10] = 0; metaBox[11] = 0;
                Buffer.BlockCopy(hdlrBox, 0, metaBox, 12, hdlrBox.Length);
                Buffer.BlockCopy(ilstBox, 0, metaBox, 12 + hdlrBox.Length, ilstBox.Length);

                // udta box (size(4) + 'udta'(4) + metaBox)
                int udtaSize = 8 + metaBox.Length;
                byte[] udtaBox = new byte[udtaSize];
                WriteUInt32BE(udtaBox, 0, (uint)udtaSize);
                udtaBox[4] = (byte)'u'; udtaBox[5] = (byte)'d'; udtaBox[6] = (byte)'t'; udtaBox[7] = (byte)'a';
                Buffer.BlockCopy(metaBox, 0, udtaBox, 8, metaBox.Length);

                return udtaBox;
            }
        }

        private static byte[] CreateDataBox(byte[] payload, uint typeFlags)
        {
            int boxSize = 16 + payload.Length;
            byte[] box = new byte[boxSize];
            WriteUInt32BE(box, 0, (uint)boxSize);
            box[4] = (byte)'d'; box[5] = (byte)'a'; box[6] = (byte)'t'; box[7] = (byte)'a';
            box[8] = 0; // version
            box[9] = (byte)((typeFlags >> 16) & 0xFF);
            box[10] = (byte)((typeFlags >> 8) & 0xFF);
            box[11] = (byte)(typeFlags & 0xFF);
            box[12] = 0; box[13] = 0; box[14] = 0; box[15] = 0; // locale
            Buffer.BlockCopy(payload, 0, box, 16, payload.Length);
            return box;
        }

        private static byte[] CreateTagBox(byte[] fourCc, byte[] dataBox)
        {
            int boxSize = 8 + dataBox.Length;
            byte[] box = new byte[boxSize];
            WriteUInt32BE(box, 0, (uint)boxSize);
            Buffer.BlockCopy(fourCc, 0, box, 4, 4);
            Buffer.BlockCopy(dataBox, 0, box, 8, dataBox.Length);
            return box;
        }

        private static byte[] CreateHdlrBox()
        {
            byte[] hdlr = new byte[33];
            WriteUInt32BE(hdlr, 0, 33);
            hdlr[4] = (byte)'h'; hdlr[5] = (byte)'d'; hdlr[6] = (byte)'l'; hdlr[7] = (byte)'r';
            // 8-11: version=0, flags=0
            // 12-15: pre_defined=0
            // 16-19: 'mdir'
            hdlr[16] = (byte)'m'; hdlr[17] = (byte)'d'; hdlr[18] = (byte)'i'; hdlr[19] = (byte)'r';
            // 20-23: 'appl'
            hdlr[20] = (byte)'a'; hdlr[21] = (byte)'p'; hdlr[22] = (byte)'p'; hdlr[23] = (byte)'l';
            // 24-31: reserved = 0
            // 32: name = 0
            return hdlr;
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static void WriteUInt32BE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)((value >> 24) & 0xFF);
            data[offset + 1] = (byte)((value >> 16) & 0xFF);
            data[offset + 2] = (byte)((value >> 8) & 0xFF);
            data[offset + 3] = (byte)(value & 0xFF);
        }

        private static ulong ReadUInt64BE(byte[] data, int offset)
        {
            return ((ulong)data[offset] << 56) |
                   ((ulong)data[offset + 1] << 48) |
                   ((ulong)data[offset + 2] << 40) |
                   ((ulong)data[offset + 3] << 32) |
                   ((ulong)data[offset + 4] << 24) |
                   ((ulong)data[offset + 5] << 16) |
                   ((ulong)data[offset + 6] << 8) |
                   (ulong)data[offset + 7];
        }

        private static void WriteUInt64BE(byte[] data, int offset, ulong value)
        {
            data[offset] = (byte)((value >> 56) & 0xFF);
            data[offset + 1] = (byte)((value >> 48) & 0xFF);
            data[offset + 2] = (byte)((value >> 40) & 0xFF);
            data[offset + 3] = (byte)((value >> 32) & 0xFF);
            data[offset + 4] = (byte)((value >> 24) & 0xFF);
            data[offset + 5] = (byte)((value >> 16) & 0xFF);
            data[offset + 6] = (byte)((value >> 8) & 0xFF);
            data[offset + 7] = (byte)(value & 0xFF);
        }
    }
}
