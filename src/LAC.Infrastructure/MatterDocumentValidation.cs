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
        // Inspect for WordDocument stream marker in UTF-16LE
        var wordMarker = Encoding.Unicode.GetBytes("WordDocument");
        if (!ContainsByteSequence(stream, wordMarker))
        {
            throw new MatterWorkflowException("File content is not a valid Microsoft Word legacy document (.doc).", 400);
        }
    }

    private static void ValidateLegacyXls(Stream stream)
    {
        ValidateOleCompoundHeader(stream);
        // Inspect for Workbook or Book stream marker in UTF-16LE
        var workbookMarker = Encoding.Unicode.GetBytes("Workbook");
        var bookMarker = Encoding.Unicode.GetBytes("Book");
        if (!ContainsByteSequence(stream, workbookMarker) && !ContainsByteSequence(stream, bookMarker))
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
        var buffer = new byte[Math.Min(stream.Length, 65536)];
        var read = stream.Read(buffer, 0, buffer.Length);

        for (int i = 0; i < read; i++)
        {
            var b = buffer[i];
            if (b == 0x00) // NUL byte
            {
                throw new MatterWorkflowException("Binary or NUL control characters are prohibited in plain text files.", 400);
            }
            // Disallow non-printable control characters except \t (9), \n (10), \r (13)
            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
            {
                throw new MatterWorkflowException("Binary control characters are prohibited in plain text files.", 400);
            }
        }
    }

    private static bool ContainsByteSequence(Stream stream, byte[] sequence)
    {
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        var buffer = new byte[4096];
        int bufferCount;
        var seqLen = sequence.Length;
        var matchIndex = 0;

        while ((bufferCount = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bufferCount; i++)
            {
                if (buffer[i] == sequence[matchIndex])
                {
                    matchIndex++;
                    if (matchIndex == seqLen) return true;
                }
                else
                {
                    matchIndex = buffer[i] == sequence[0] ? 1 : 0;
                }
            }
        }
        return false;
    }
}
