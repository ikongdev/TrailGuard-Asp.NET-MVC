namespace TrailGuard.Services
{






    public static class TrailTerrainOptions
    {


        public static readonly string[] AllowedValues =
        {
            "Grassland", "Mossy Forest", "Pine Forest", "River Trek",
            "Rocky", "Rocky / Boulders", "Volcanic", "Muddy Trail", "Mixed Terrain",
        };




        public static IEnumerable<string> Parse(string? storedValue) =>
            (storedValue ?? string.Empty)
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);










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
