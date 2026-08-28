using System.Globalization;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    // Single source of truth for pickup schedule normalization and the
    // canonical "Location — h:mm tt" stored format. Add Event (building new
    // lines from a structured request) and Registration (matching a
    // submission against an event's existing lines) both go through this, so
    // the two can't drift into slightly different rules for the same
    // Event.PickupPoints string.
    public static class PickupScheduleHelper
    {
        public const string Delimiter = " — ";

        private const string InputTimeFormat = "HH:mm";
        private const string StoredTimeFormat = "h:mm tt";

        public sealed class ValidationResult
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public List<string> CanonicalLines { get; set; } = [];
        }

        // Validates a create-request's structured schedules and formats them
        // into canonical "Location — h:mm tt" lines, in submitted order.
        // Never trusts a client-composed display string - only Location and
        // Time (parsed as exact HH:mm, invariant culture) are read from each
        // item.
        public static ValidationResult ValidateAndFormat(IEnumerable<PickupScheduleInputModel>? schedules)
        {
            var input = schedules?.ToList() ?? [];

            if (input.Count == 0)
            {
                return new ValidationResult { Error = "At least one pickup schedule is required." };
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = new List<string>();

            foreach (var schedule in input)
            {
                var location = (schedule.Location ?? string.Empty).Trim();
                if (location.Length == 0)
                {
                    return new ValidationResult { Error = "Every pickup schedule needs a location." };
                }

                // These would make the stored "Location — Time" format
                // ambiguous to re-parse - a line break could be mistaken for a
                // second entry, and an em dash anywhere in the location (not
                // just the padded " — " delimiter) could be mistaken for the
                // separator between this entry's own location and time. The
                // Add Event client-side check rejects the same bare character
                // for the same reason - kept identical so a location the
                // browser accepts can never be rejected by the server, or the
                // other way around.
                if (location.Contains('\n') || location.Contains('\r') || location.Contains('—'))
                {
                    return new ValidationResult { Error = $"\"{location}\" contains characters that aren't allowed in a pickup location." };
                }

                if (!DateTime.TryParseExact(schedule.Time, InputTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                {
                    return new ValidationResult { Error = $"\"{location}\" has an invalid pickup time." };
                }

                var formattedTime = time.ToString(StoredTimeFormat, CultureInfo.InvariantCulture);
                var line = $"{location}{Delimiter}{formattedTime}";

                // Same location at a different time is a legitimate second
                // schedule, so the dedupe key is the pair, not the location alone.
                var dedupeKey = $"{location.ToUpperInvariant()}|{formattedTime.ToUpperInvariant()}";
                if (!seen.Add(dedupeKey))
                {
                    return new ValidationResult { Error = $"\"{line}\" is a duplicate pickup schedule." };
                }

                lines.Add(line);
            }

            return new ValidationResult { Success = true, CanonicalLines = lines };
        }

        // Parses a stored PickupPoints string into trimmed, non-blank,
        // duplicate-free entries in their original order. Works identically
        // for canonical "Location — Time" lines and legacy free-text lines
        // written before this feature existed - both are just newline-
        // delimited text from this method's point of view. Normalizes \r\n
        // and lone \r to \n first, since older stored values may predate a
        // consistent line-ending convention.
        public static List<string> ParseStoredEntries(string? pickupPoints)
        {
            if (string.IsNullOrWhiteSpace(pickupPoints)) return [];

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<string>();

            foreach (var rawLine in pickupPoints.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var trimmed = rawLine.Trim();
                if (trimmed.Length == 0) continue;
                if (seen.Add(trimmed)) entries.Add(trimmed);
            }

            return entries;
        }

        // Finds the stored entry that a registration submission actually
        // refers to - trimmed, case-insensitive - or null if it doesn't match
        // any of this event's real schedules. Callers must store this
        // returned value, never the raw submission, so a registration can
        // never persist a fabricated value or one copied from another event.
        public static string? FindCanonicalMatch(string? pickupPoints, string? submittedValue)
        {
            if (string.IsNullOrWhiteSpace(submittedValue)) return null;

            var trimmedSubmission = submittedValue.Trim();
            return ParseStoredEntries(pickupPoints)
                .FirstOrDefault(entry => string.Equals(entry, trimmedSubmission, StringComparison.OrdinalIgnoreCase));
        }

        // One stored entry as the Edit Event builder needs to load it back
        // into an editable Location + Time pair. Time is null (and
        // RequiresTime is true) for a legacy entry that isn't actually in the
        // canonical "Location — h:mm tt" shape - the whole trimmed line
        // becomes the location rather than guessing or dropping it.
        public sealed class EditablePickupSchedule
        {
            public string Location { get; set; } = string.Empty;

            // "HH:mm" (24-hour), matching the browser's <input type="time">
            // contract - null when RequiresTime is true.
            public string? Time { get; set; }

            public bool RequiresTime { get; set; }
        }

        // Hydrates Edit Event's Pickup Schedules builder from a stored
        // PickupPoints string - one EditablePickupSchedule per entry from
        // ParseStoredEntries, so order, trimming, and de-duplication all stay
        // governed by that one method rather than being re-implemented here.
        public static List<EditablePickupSchedule> ParseForEditing(string? pickupPoints)
        {
            return ParseStoredEntries(pickupPoints)
                .Select(ParseSingleEntryForEditing)
                .ToList();
        }

        // A stored entry is only treated as canonical when it ends with a
        // valid "h:mm tt"/"hh:mm tt" suffix immediately after the exact " — "
        // separator. Tries the *rightmost* delimiter first and walks left
        // through any earlier ones (searchEnd shrinks each iteration) so a
        // legacy location that itself happens to contain " — " as plain text
        // isn't truncated at the first occurrence - only a delimiter whose
        // suffix actually parses as a time counts as the real separator.
        // Anything that never finds such a suffix is preserved whole as a
        // legacy location requiring a time, never dropped or guessed at.
        private static EditablePickupSchedule ParseSingleEntryForEditing(string entry)
        {
            var searchEnd = entry.Length;

            while (searchEnd > 0)
            {
                var delimiterIndex = entry.LastIndexOf(Delimiter, searchEnd - 1, StringComparison.Ordinal);
                if (delimiterIndex <= 0) break;

                var timeText = entry[(delimiterIndex + Delimiter.Length)..];
                if (TryParseStoredTime(timeText, out var parsedTime))
                {
                    return new EditablePickupSchedule
                    {
                        Location = entry[..delimiterIndex].Trim(),
                        Time = parsedTime.ToString(InputTimeFormat, CultureInfo.InvariantCulture),
                        RequiresTime = false
                    };
                }

                searchEnd = delimiterIndex;
            }

            return new EditablePickupSchedule
            {
                Location = entry,
                Time = null,
                RequiresTime = true
            };
        }

        private static bool TryParseStoredTime(string text, out DateTime time)
        {
            return DateTime.TryParseExact(
                text,
                ["h:mm tt", "hh:mm tt"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time);
        }
    }
}
