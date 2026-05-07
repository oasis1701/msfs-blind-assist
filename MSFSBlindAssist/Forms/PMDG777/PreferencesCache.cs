namespace MSFSBlindAssist.Forms.PMDG777
{
    /// <summary>
    /// Singleton cache of EFB preferences (units). Populated from the bridge's
    /// "preferences" state update. Panels consult this for formatting/parsing so
    /// there's one source of truth for unit selection across the form.
    /// </summary>
    public static class PreferencesCache
    {
        public static string WeightUnit { get; private set; } = "kg";
        public static string DistanceUnit { get; private set; } = "nm";
        public static string AltitudeUnit { get; private set; } = "ft";
        public static string TemperatureUnit { get; private set; } = "C";
        public static string PressureUnit { get; private set; } = "hPa";
        public static string LengthUnit { get; private set; } = "ft";
        public static string SpeedUnit { get; private set; } = "kph";
        public static string AirspeedUnit { get; private set; } = "kts";
        public static string SimBriefId { get; private set; } = "";
        public static string WeatherSource { get; private set; } = "";

        public static event EventHandler? PreferencesChanged;

        public static void Update(Dictionary<string, string> data)
        {
            if (data.TryGetValue("weight_unit", out var w) && !string.IsNullOrEmpty(w)) WeightUnit = w;
            if (data.TryGetValue("distance_unit", out var d) && !string.IsNullOrEmpty(d)) DistanceUnit = d;
            if (data.TryGetValue("altitude_unit", out var a) && !string.IsNullOrEmpty(a)) AltitudeUnit = a;
            if (data.TryGetValue("temperature_unit", out var t) && !string.IsNullOrEmpty(t)) TemperatureUnit = t;
            if (data.TryGetValue("pressure_unit", out var p) && !string.IsNullOrEmpty(p)) PressureUnit = p;
            if (data.TryGetValue("length_unit", out var l) && !string.IsNullOrEmpty(l)) LengthUnit = l;
            if (data.TryGetValue("speed_unit", out var s) && !string.IsNullOrEmpty(s)) SpeedUnit = s;
            if (data.TryGetValue("airspeed_unit", out var asp) && !string.IsNullOrEmpty(asp)) AirspeedUnit = asp;
            if (data.TryGetValue("simbrief_id", out var sb) && sb != null) SimBriefId = sb;
            if (data.TryGetValue("weather_source", out var ws) && !string.IsNullOrEmpty(ws)) WeatherSource = ws;

            PreferencesChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
