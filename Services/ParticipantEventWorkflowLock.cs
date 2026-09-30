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
    private const long EventCapacityLockNamespace = 0x54474343L; // "TGCC"

    /// <summary>
    /// Serializes capacity-changing writes for one event. PostgreSQL's single-bigint
    /// advisory-lock namespace is distinct from the existing two-int participant/event lock.
    /// </summary>
    public static async Task AcquireEventCapacityAsync(
        ApplicationDbContext context,
        int eventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (eventId <= 0) throw new ArgumentOutOfRangeException(nameof(eventId));

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({DeriveEventCapacityKey(eventId)})",
            cancellationToken);
    }

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

    public static long DeriveEventCapacityKey(int eventId)
    {
        if (eventId <= 0) throw new ArgumentOutOfRangeException(nameof(eventId));
        return (EventCapacityLockNamespace << 32) | (uint)eventId;
    }

    public static bool IsUniqueConstraintConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
