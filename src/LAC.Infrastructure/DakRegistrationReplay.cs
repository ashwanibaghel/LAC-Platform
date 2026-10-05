namespace LAC.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed partial class DakWorkflowService
{
    private async Task<Dak?> FindRegistrationReplayAsync(RegisterDakCommand command, Guid userId, string hash, CancellationToken ct)
    {
        if (!command.RequestId.HasValue) return null;
        var existing = await db.Daks.AsNoTracking().SingleOrDefaultAsync(d =>
            d.RegisteredByUserId == userId && d.RegistrationRequestId == command.RequestId, ct);
        if (existing is null) return null;
        if (existing.RegistrationRequestHash != hash)
            throw new DakWorkflowException("This Idempotency-Key was used for a different registration. Use a new key for a new receipt.", 409);
        return existing;
    }

    private static async Task<string> RegistrationHashAsync(RegisterDakCommand command, CancellationToken ct)
    {
        string? documentHash = null;
        if (command.DocumentStream is not null)
        {
            // Intake streams from multipart files are seekable. Preserve the exact file bytes for storage.
            if (!command.DocumentStream.CanSeek)
                throw new DakWorkflowException("Registration document stream must support safe retry.");
            var position = command.DocumentStream.Position;
            documentHash = Convert.ToHexString(await SHA256.HashDataAsync(command.DocumentStream, ct));
            command.DocumentStream.Position = position;
        }
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var json = JsonSerializer.Serialize(new
        {
            DiaryNumber = DakDiaryNumber.Normalize(command.DiaryNumber.Trim()), command.ReceivedDate,
            Subject = command.Subject.Trim(), SenderName = command.SenderName.Trim(),
            SenderDesignation = Optional(command.SenderDesignation), SenderDepartment = Optional(command.SenderDepartment),
            SenderAddress = Optional(command.SenderAddress), SenderReferenceNumber = Optional(command.SenderReferenceNumber),
            command.SenderLetterDate, InwardMode = Optional(command.InwardMode) ?? "Physical", command.Priority,
            command.DueDate, command.CategoryId, command.WorkstreamId, command.DocumentFileName,
            command.DocumentContentType, documentHash
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
