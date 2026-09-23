using System.Globalization;
using TrailGuard.Models;

namespace TrailGuard.Services
{






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



                var dedupeKey = $"{location.ToUpperInvariant()}|{formattedTime.ToUpperInvariant()}";
                if (!seen.Add(dedupeKey))
                {
                    return new ValidationResult { Error = $"\"{line}\" is a duplicate pickup schedule." };
                }

                lines.Add(line);
            }

            return new ValidationResult { Success = true, CanonicalLines = lines };
        }








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






        public static string? FindCanonicalMatch(string? pickupPoints, string? submittedValue)
        {
            if (string.IsNullOrWhiteSpace(submittedValue)) return null;

            var trimmedSubmission = submittedValue.Trim();
            return ParseStoredEntries(pickupPoints)
                .FirstOrDefault(entry => string.Equals(entry, trimmedSubmission, StringComparison.OrdinalIgnoreCase));
        }






        public sealed class EditablePickupSchedule
        {
            public string Location { get; set; } = string.Empty;



            public string? Time { get; set; }

            public bool RequiresTime { get; set; }
        }





        public static List<EditablePickupSchedule> ParseForEditing(string? pickupPoints)
        {
            return ParseStoredEntries(pickupPoints)
                .Select(ParseSingleEntryForEditing)
                .ToList();
        }










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
