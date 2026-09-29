using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrailGuard.Data;

namespace TrailGuard.Services;

/// <summary>
/// Serializes final assessment and registration writes for one participant/event pair.
/// PostgreSQL releases this advisory lock automatically when the owning transaction ends.
/// </summary>
public static class ParticipantEventWorkflowLock
{
    public const string ActiveAssessmentUniqueIndexName = "IX_Assessments_EventId_UserId_Active";

    public static async Task AcquireAsync(
        ApplicationDbContext context,
        int eventId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (eventId <= 0) throw new ArgumentOutOfRangeException(nameof(eventId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A participant ID is required.", nameof(userId));

        var userKey = DeriveUserKey(userId);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({eventId}, {userKey})",
            cancellationToken);
    }

    /// <summary>Stable across processes and runtimes; intentionally does not use string.GetHashCode().</summary>
    public static int DeriveUserKey(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var input = Encoding.UTF8.GetBytes("TrailGuard.ParticipantEventWorkflowLock.v1\0" + userId);
        var digest = SHA256.HashData(input);
        return BinaryPrimitives.ReadInt32BigEndian(digest);
    }

    public static bool IsUniqueConstraintConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
