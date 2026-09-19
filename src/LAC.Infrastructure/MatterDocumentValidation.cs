namespace LAC.Infrastructure;

using System.IO.Compression;
using System.Text;

public static class MatterDocumentValidation
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".doc", ".xls", ".docx", ".xlsx", ".txt"
    };

    public static string GetServerDerivedMimeType(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tif" or ".tiff" => "image/tiff",
            ".doc" => "application/msword",
            ".xls" => "application/vnd.ms-excel",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    public static bool IsInlineDisposition(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext is ".pdf" or ".png" or ".jpg" or ".jpeg";
    }

    public static void ValidateFileContent(Stream stream, string extension)
    {
        if (stream.Length == 0)
            throw new MatterWorkflowException("Cannot upload an empty file (0 bytes).", 400);

        var ext = extension.ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
            throw new MatterWorkflowException($"Unsupported file format '{extension}'. Supported formats: PDF, PNG, JPEG, TIFF, DOC, XLS, DOCX, XLSX, TXT.", 400);

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            switch (ext)
            {
                case ".pdf":
                    ValidatePdf(stream);
                    break;
                case ".png":
                    ValidatePng(stream);
                    break;
                case ".jpg":
                case ".jpeg":
                    ValidateJpeg(stream);
                    break;
                case ".tif":
                case ".tiff":
                    ValidateTiff(stream);
                    break;
                case ".doc":
                    ValidateLegacyDoc(stream);
                    break;
                case ".xls":
                    ValidateLegacyXls(stream);
                    break;
                case ".docx":
                    ValidateDocx(stream);
                    break;
                case ".xlsx":
                    ValidateXlsx(stream);
                    break;
                case ".txt":
                    ValidatePlainText(stream);
                    break;
                default:
                    throw new MatterWorkflowException($"Unsupported file format '{extension}'.", 400);
            }
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static void ValidatePdf(Stream stream)
    {
        var header = new byte[5];
        var read = stream.Read(header, 0, header.Length);
        if (read < 5 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46 || header[4] != 0x2D)
        {
            throw new MatterWorkflowException("File content does not match standard PDF signature (%PDF-).", 400);
        }
    }

    private static void ValidatePng(Stream stream)
    {
        var header = new byte[8];
        var read = stream.Read(header, 0, header.Length);
        if (read < 8
            || header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47
            || header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A)
        {
            throw new MatterWorkflowException("File content does not match standard PNG signature.", 400);
        }
    }

    private static void ValidateJpeg(Stream stream)
    {
        var header = new byte[3];
        var read = stream.Read(header, 0, header.Length);
        if (read < 3 || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF)
        {
            throw new MatterWorkflowException("File content does not match standard JPEG signature.", 400);
        }
    }

    private static void ValidateTiff(Stream stream)
    {
        var header = new byte[4];
        var read = stream.Read(header, 0, header.Length);
        var isLittleEndian = read >= 4 && header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00;
        var isBigEndian = read >= 4 && header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A;

        if (!isLittleEndian && !isBigEndian)
        {
            throw new MatterWorkflowException("File content does not match standard TIFF signature.", 400);
        }
    }

    private static void ValidateLegacyDoc(Stream stream)
    {
        ValidateOleCompoundHeader(stream);
        if (!HasCfbStream(stream, "WordDocument"))
        {
            throw new MatterWorkflowException("File content is not a valid Microsoft Word legacy document (.doc).", 400);
        }
    }

    private static void ValidateLegacyXls(Stream stream)
    {
        ValidateOleCompoundHeader(stream);
        if (!HasCfbStream(stream, "Workbook", "Book"))
        {
            throw new MatterWorkflowException("File content is not a valid Microsoft Excel legacy spreadsheet (.xls).", 400);
        }
    }

    private static void ValidateOleCompoundHeader(Stream stream)
    {
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        var header = new byte[8];
        var read = stream.Read(header, 0, header.Length);
        if (read < 8
            || header[0] != 0xD0 || header[1] != 0xCF || header[2] != 0x11 || header[3] != 0xE0
            || header[4] != 0xA1 || header[5] != 0xB1 || header[6] != 0x1A || header[7] != 0xE1)
        {
            throw new MatterWorkflowException("File content does not match standard OLE Compound File signature.", 400);
        }
    }

    private static bool HasCfbStream(Stream stream, params string[] targetStreamNames)
    {
        if (stream.Length < 512) return false;
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);

        var header = new byte[512];
        var read = stream.Read(header, 0, 512);
        if (read < 512) return false;

        // Verify OLE signature
        if (header[0] != 0xD0 || header[1] != 0xCF || header[2] != 0x11 || header[3] != 0xE0 ||
            header[4] != 0xA1 || header[5] != 0xB1 || header[6] != 0x1A || header[7] != 0xE1)
        {
            return false;
        }

        var sectorShift = BitConverter.ToUInt16(header, 30);
        if (sectorShift != 9 && sectorShift != 12) return false;
        var sectorSize = 1 << sectorShift;

        var numFatSectors = BitConverter.ToUInt32(header, 44);
        var firstDirSector = BitConverter.ToUInt32(header, 48);

        // Read FAT sector locations from header DIFAT array (bytes 76..511: 109 uint32s)
        var maxHeaderFat = Math.Min(109, numFatSectors);
        var fatSectors = new List<uint>();
        for (int i = 0; i < maxHeaderFat; i++)
        {
            var fatSecId = BitConverter.ToUInt32(header, 76 + i * 4);
            if (fatSecId >= 0xFFFFFFFA) break;
            fatSectors.Add(fatSecId);
        }

        // Build FAT lookup map
        var fat = new Dictionary<uint, uint>();
        var maxEntriesPerSector = sectorSize / 4;
        var sectorBuffer = new byte[sectorSize];

        foreach (var fatSecId in fatSectors)
        {
            var fatOffset = (long)(fatSecId + 1) * sectorSize;
            if (fatOffset + sectorSize > stream.Length) return false;

            stream.Seek(fatOffset, SeekOrigin.Begin);
            if (stream.Read(sectorBuffer, 0, sectorSize) < sectorSize) return false;

            var baseSecId = (uint)fat.Count;
            for (int i = 0; i < maxEntriesPerSector; i++)
            {
                var nextSec = BitConverter.ToUInt32(sectorBuffer, i * 4);
                fat[baseSecId + (uint)i] = nextSec;
            }
        }

        // Traverse directory sector chain starting at firstDirSector
        var currDirSec = firstDirSector;
        var visited = new HashSet<uint>();
        var maxDirSectors = Math.Min(1000, (int)(stream.Length / sectorSize) + 1);

        var targetSet = new HashSet<string>(targetStreamNames, StringComparer.OrdinalIgnoreCase);

        while (currDirSec < 0xFFFFFFFA && visited.Count < maxDirSectors)
        {
            if (!visited.Add(currDirSec)) break; // Prevent infinite cycle

            var dirOffset = (long)(currDirSec + 1) * sectorSize;
            if (dirOffset + sectorSize > stream.Length) return false;

            stream.Seek(dirOffset, SeekOrigin.Begin);
            if (stream.Read(sectorBuffer, 0, sectorSize) < sectorSize) return false;

            var entriesPerSector = sectorSize / 128;
            for (int e = 0; e < entriesPerSector; e++)
            {
                var entryOffset = e * 128;
                var objectType = sectorBuffer[entryOffset + 66];
                // Object Type 2 = Stream
                if (objectType == 2)
                {
                    var nameLen = BitConverter.ToUInt16(sectorBuffer, entryOffset + 64);
                    if (nameLen >= 2 && nameLen <= 64)
                    {
                        var name = Encoding.Unicode.GetString(sectorBuffer, entryOffset, nameLen - 2);
                        if (targetSet.Contains(name))
                        {
                            return true;
                        }
                    }
                }
            }

            if (fat.TryGetValue(currDirSec, out var nextSec))
            {
                currDirSec = nextSec;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private static void ValidateDocx(Stream stream)
    {
        ValidateZipHeader(stream);
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var hasContentTypes = archive.GetEntry("[Content_Types].xml") != null;
            var hasDocumentXml = archive.GetEntry("word/document.xml") != null;

            if (!hasContentTypes || !hasDocumentXml)
            {
                throw new MatterWorkflowException("File content is not a valid DOCX document package.", 400);
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                {
                    throw new MatterWorkflowException("Macro-enabled Office documents are strictly prohibited.", 400);
                }
            }
        }
        catch (InvalidDataException)
        {
            throw new MatterWorkflowException("File content is not a valid ZIP-based Office Open XML document.", 400);
        }
    }

    private static void ValidateXlsx(Stream stream)
    {
        ValidateZipHeader(stream);
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var hasContentTypes = archive.GetEntry("[Content_Types].xml") != null;
            var hasWorkbookXml = archive.GetEntry("xl/workbook.xml") != null;

            if (!hasContentTypes || !hasWorkbookXml)
            {
                throw new MatterWorkflowException("File content is not a valid XLSX document package.", 400);
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                {
                    throw new MatterWorkflowException("Macro-enabled Office documents are strictly prohibited.", 400);
                }
            }
        }
        catch (InvalidDataException)
        {
            throw new MatterWorkflowException("File content is not a valid ZIP-based Office Open XML document.", 400);
        }
    }

    private static void ValidateZipHeader(Stream stream)
    {
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        var header = new byte[4];
        var read = stream.Read(header, 0, header.Length);
        if (read < 4 || header[0] != 0x50 || header[1] != 0x4B || header[2] != 0x03 || header[3] != 0x04)
        {
            throw new MatterWorkflowException("File content does not match standard ZIP/OOXML signature.", 400);
        }
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
    }

    private static void ValidatePlainText(Stream stream)
    {
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        var bytes = memoryStream.ToArray();

        if (bytes.Length == 0) return;

        // Check for NUL byte
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new MatterWorkflowException("Binary or NUL control characters are prohibited in plain text files.", 400);
        }

        // Strict UTF-8 decoding
        string text;
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            text = encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new MatterWorkflowException("File content is not valid UTF-8 text.", 400);
        }

        // Disallow non-printable control characters except \t (9), \n (10), \r (13)
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
            {
                throw new MatterWorkflowException("Binary control characters are prohibited in plain text files.", 400);
            }
        }
    }
}
