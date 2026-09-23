namespace Bot_Detection_Service.Validators
{
    /// <summary>
    /// Structural validation for <see cref="ScoreRequest"/>. Kept as a plain,
    /// HTTP-agnostic function (dictionary of field -> messages) so it can be unit
    /// tested without spinning up the web host, and so the endpoint filter that
    /// calls it stays a thin adapter.
    ///
    /// Scope is deliberately structural: presence of the required fields that the
    /// scorer dereferences, plus sane length caps on the free-text identifiers.
    /// It does NOT range-check every numeric feature — the scorer only compares
    /// them against thresholds and is unbothered by odd values — though that
    /// would be the natural next layer if we want to reject obviously-garbage
    /// vectors outright.
    ///
    /// The error keys are the camelCase JSON field paths (e.g. "features.environment")
    /// so the caller can map a 400 straight back to the offending field.
    /// </summary>
    public static class ScoreRequestValidator
    {
        // GUIDs are 36 chars; this leaves generous headroom while still bounding
        // how much attacker-controlled text can reach logs or memory.
        public const int MaxIdentifierLength = 200;

        public static IDictionary<string, string[]> Validate(ScoreRequest? request)
        {
            var errors = new Dictionary<string, string[]>();

            if (request is null)
            {
                errors["request"] = new[] { "A JSON request body is required." };
                return errors; // nothing else to inspect
            }

            // sessionId: required, non-blank, bounded.
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                errors["sessionId"] = new[] { "sessionId is required." };
            }
            else if (request.SessionId.Length > MaxIdentifierLength)
            {
                errors["sessionId"] = new[] { $"sessionId must be {MaxIdentifierLength} characters or fewer." };
            }

            // features: required. The scorer dereferences the four sub-objects
            // below, so each must be present too — a `= new()` initializer on the
            // model only covers an OMITTED field, not an explicit JSON null.
            if (request.Features is null)
            {
                errors["features"] = new[] { "features is required." };
            }
            else
            {
                var f = request.Features;
                if (IsMissing(f.Environment)) errors["features.environment"] = new[] { "features.environment is required." };
                if (IsMissing(f.Mouse)) errors["features.mouse"] = new[] { "features.mouse is required." };
                if (IsMissing(f.Keyboard)) errors["features.keyboard"] = new[] { "features.keyboard is required." };
                if (IsMissing(f.Scroll)) errors["features.scroll"] = new[] { "features.scroll is required." };
                // features.requestPattern is intentionally optional (nullable).
            }

            return errors;
        }

        // The feature sub-objects are annotated non-null (they carry `= new()`
        // initializers), so a direct `x is null` would trip the compiler's
        // "always false" nullable analysis. But System.Text.Json does not honor
        // those annotations — an explicit `"environment": null` in the payload
        // binds null anyway — so the runtime check is real. Passing through an
        // `object?` parameter keeps the check while sidestepping the warning.
        private static bool IsMissing(object? value) => value is null;
    }
}
