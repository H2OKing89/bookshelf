using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;

namespace NzbDrone.Core.Backup
{
    internal static class BackupArchivePreflight
    {
        private const int MaximumEntries = 64;
        private const int EndRecordLength = 22;
        private const long MaximumDirectoryLength = MaximumEntries * (46L + 255 + (2L * ushort.MaxValue));

        public static void Validate(Stream stream)
        {
            if (!stream.CanRead || !stream.CanSeek || stream.Length < EndRecordLength)
            {
                throw InvalidArchive();
            }

            var originalPosition = stream.Position;
            try
            {
                var tail = new byte[(int)Math.Min(stream.Length, EndRecordLength + ushort.MaxValue)];
                var tailOffset = stream.Length - tail.Length;
                ReadAt(stream, tailOffset, tail);
                var index = tail.Length - EndRecordLength;
                while (index >= 0 && UInt32(tail, index) != 0x06054b50)
                {
                    index--;
                }

                // Select the same last signature as SharpZipLib, then reject malformed
                // comments rather than skipping a fake record inside the real comment.
                if (index < 0 || UInt16(tail, index + 20) != tail.Length - index - EndRecordLength ||
                    UInt16(tail, index + 4) != 0 || UInt16(tail, index + 6) != 0)
                {
                    throw InvalidArchive();
                }

                var endOffset = tailOffset + index;
                ulong count = UInt16(tail, index + 10);
                ulong diskCount = UInt16(tail, index + 8);
                ulong directoryLength = UInt32(tail, index + 12);
                ulong directoryOffset = UInt32(tail, index + 16);
                var directoryEnd = endOffset;
                var locatorOffset = FindZip64Locator(stream, endOffset);
                if (locatorOffset >= 0)
                {
                    if (locatorOffset != endOffset - 20)
                    {
                        throw InvalidArchive();
                    }

                    var locator = new byte[20];
                    ReadAt(stream, locatorOffset, locator);
                    var recordOffset = UInt64(locator, 8);
                    if (UInt32(locator, 4) != 0 || UInt32(locator, 16) != 1 ||
                        recordOffset > (ulong)locatorOffset || (ulong)locatorOffset - recordOffset < 56)
                    {
                        throw InvalidArchive();
                    }

                    var record = new byte[56];
                    ReadAt(stream, (long)recordOffset, record);
                    var recordLength = UInt64(record, 4);
                    var zip64Count = UInt64(record, 32);
                    var zip64DiskCount = UInt64(record, 24);
                    var zip64DirectoryLength = UInt64(record, 40);
                    var zip64DirectoryOffset = UInt64(record, 48);
                    if (UInt32(record, 0) != 0x06064b50 || recordLength < 44 ||
                        recordLength != (ulong)locatorOffset - recordOffset - 12 ||
                        UInt32(record, 16) != 0 || UInt32(record, 20) != 0 ||
                        (count != ushort.MaxValue && count != zip64Count) ||
                        (diskCount != ushort.MaxValue && diskCount != zip64DiskCount) ||
                        (directoryLength != uint.MaxValue && directoryLength != zip64DirectoryLength) ||
                        (directoryOffset != uint.MaxValue && directoryOffset != zip64DirectoryOffset))
                    {
                        throw InvalidArchive();
                    }

                    count = zip64Count;
                    diskCount = zip64DiskCount;
                    directoryLength = zip64DirectoryLength;
                    directoryOffset = zip64DirectoryOffset;
                    directoryEnd = (long)recordOffset;
                }
                else if (count == ushort.MaxValue || diskCount == ushort.MaxValue ||
                         directoryLength == uint.MaxValue || directoryOffset == uint.MaxValue)
                {
                    throw InvalidArchive();
                }

                // ZipFile allocates its entire entry array in its constructor. These
                // bounds must be checked first, including ZIP64 without legacy sentinels.
                if (count > MaximumEntries || diskCount != count || directoryLength > MaximumDirectoryLength ||
                    directoryOffset > (ulong)directoryEnd || directoryLength != (ulong)directoryEnd - directoryOffset ||
                    directoryLength < count * 46)
                {
                    throw InvalidArchive();
                }

                var cursor = (long)directoryOffset;
                var header = new byte[46];
                for (ulong entry = 0; entry < count; entry++)
                {
                    if (directoryEnd - cursor < header.Length)
                    {
                        throw InvalidArchive();
                    }

                    ReadAt(stream, cursor, header);
                    var nameLength = UInt16(header, 28);
                    var entryLength = 46L + nameLength + UInt16(header, 30) + UInt16(header, 32);
                    if (UInt32(header, 0) != 0x02014b50 || nameLength == 0 || nameLength > 255 ||
                        UInt16(header, 34) != 0 || entryLength > directoryEnd - cursor)
                    {
                        throw InvalidArchive();
                    }

                    cursor += entryLength;
                }

                if (cursor != directoryEnd)
                {
                    throw InvalidArchive();
                }
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static long FindZip64Locator(Stream stream, long endOffset)
        {
            if (endOffset < 20)
            {
                return -1;
            }

            // Like ZipFile, probe only the locator's exact position. Signature bytes
            // inside a locator offset or central-directory comment are not locators.
            var position = endOffset - 20;
            var signature = new byte[4];
            ReadAt(stream, position, signature);
            return UInt32(signature, 0) == 0x07064b50 ? position : -1;
        }

        private static void ReadAt(Stream stream, long position, byte[] buffer)
        {
            stream.Position = position;
            var offset = 0;
            while (offset < buffer.Length)
            {
                var count = stream.Read(buffer, offset, buffer.Length - offset);
                if (count == 0)
                {
                    throw InvalidArchive();
                }

                offset += count;
            }
        }

        private static ushort UInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

        private static uint UInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

        private static ulong UInt64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));

        private static RestoreBackupFailedException InvalidArchive() =>
            new RestoreBackupFailedException(HttpStatusCode.BadRequest, "Backup archive metadata is invalid or exceeds restore limits");
    }
}
