namespace TrailGuard.Services
{
    // Single source of truth for the Terrain multi-select on Add/Edit Trail. Both
    // modals render their checkboxes from AllowedValues, and the controller
    // validates submissions against the same list - no separate hardcoded option
    // list to drift out of sync with this one. Trail.Terrain itself stays a plain
    // comma-separated string; this class only owns parsing it for read-only
    // display and normalizing a submitted selection back into that format.
    public static class TrailTerrainOptions
    {
        // Unchanged from the values previously hardcoded into the Add/Edit
        // <select> options - not a new vocabulary, just centralized.
        public static readonly string[] AllowedValues =
        {
            "Grassland", "Mossy Forest", "Pine Forest", "River Trek",
            "Rocky", "Rocky / Boulders", "Volcanic", "Muddy Trail", "Mixed Terrain",
        };

        // Same parsing convention used for read-only display across the app
        // (Participant/Trails.cshtml, the trail card, Trail Details): split on
        // comma, trim, drop empty pieces.
        public static IEnumerable<string> Parse(string? storedValue) =>
            (storedValue ?? string.Empty)
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);

        // Validates a submitted checkbox selection and joins it back into the
        // stored comma-separated format. A value survives only if it is one of
        // AllowedValues, or if it was already present on the trail being edited
        // (existingStoredValue) - a legacy value nobody offers as a checkbox
        // can still be preserved by resubmitting it, but a client can't invent
        // an arbitrary new value that was never actually on the record. Trims,
        // dedupes (case-insensitive), and orders deterministically: AllowedValues
        // in their defined order first, then any surviving legacy values in the
        // order they appeared on the existing record.
        public static string Normalize(IEnumerable<string>? submittedValues, string? existingStoredValue = null)
        {
            var existingValues = Parse(existingStoredValue).ToList();

            var trusted = new HashSet<string>(AllowedValues, StringComparer.OrdinalIgnoreCase);
            foreach (var v in existingValues)
            {
                trusted.Add(v);
            }

            var submitted = new HashSet<string>(
                (submittedValues ?? Enumerable.Empty<string>())
                    .Select(v => (v ?? string.Empty).Trim())
                    .Where(v => v.Length > 0 && trusted.Contains(v)),
                StringComparer.OrdinalIgnoreCase);

            var ordered = new List<string>(AllowedValues.Where(submitted.Contains));
            foreach (var legacy in existingValues)
            {
                if (submitted.Contains(legacy) && !ordered.Contains(legacy, StringComparer.OrdinalIgnoreCase))
                {
                    ordered.Add(legacy);
                }
            }

            return string.Join(", ", ordered);
        }
    }
}
