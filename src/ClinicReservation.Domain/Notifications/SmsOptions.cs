namespace ClinicReservation.Models
{
    /// <summary>"Sms" section of appsettings.json.</summary>
    public class SmsOptions
    {
        /// <summary>Turns the automatic reminder job on/off.</summary>
        public bool RemindersEnabled { get; set; } = true;

        /// <summary>"Log" (test mode — nothing leaves the server) or "Http" (real provider).</summary>
        public string Provider { get; set; } = "Log";

        /// <summary>How long before the appointment the reminder goes out.</summary>
        public int ReminderHoursBefore { get; set; } = 24;

        /// <summary>Bookings made closer than this to the visit don't get a reminder.</summary>
        public int MinLeadHours { get; set; } = 3;

        public int CheckIntervalMinutes { get; set; } = 5;

        /// <summary>No automatic SMS between these hours (local time).</summary>
        public string QuietStart { get; set; } = "21:00";
        public string QuietEnd { get; set; } = "09:00";

        /// <summary>Public address of the app, used in the confirm/cancel link.</summary>
        public string PublicBaseUrl { get; set; } = "http://localhost:5097";

        public string ClinicName { get; set; } = "MedClinic";

        /// <summary>Placeholders: {ad} {tarix} {saat} {hekim} {klinika} {link}.</summary>
        public string ReminderTemplate { get; set; } =
            "Hormetli {ad}, {tarix} saat {saat} {hekim} qebulunuz var ({klinika}). Tesdiq ve ya legv: {link}";

        /// <summary>Sent when lab results are validated. Placeholders: {ad} {klinika} {link}.</summary>
        public string LabResultTemplate { get; set; } =
            "Hormetli {ad}, analiz neticeleriniz hazirdir ({klinika}). Baxmaq ucun: {link}";

        /// <summary>A freed time offered to a waiting patient. Placeholders: {ad} {tarix} {saat} {hekim} {klinika}.</summary>
        public string WaitlistTemplate { get; set; } =
            "Hormetli {ad}, {tarix} saat {saat} {hekim} qebulunda bos vaxt yarandi ({klinika}). Rezerv ucun klinikaya zeng edin.";

        /// <summary>Follow-up reminder. Placeholders: {ad} {tarix} {hekim} {klinika}.</summary>
        public string FollowUpTemplate { get; set; } =
            "Hormetli {ad}, {hekim} {tarix} tarixinde kontrol qebulu teyin edib ({klinika}). Yazilmaq ucun klinikaya zeng edin.";

        /// <summary>Replace Azerbaijani letters (ə, ş, ç …) with Latin ones: 160 chars per SMS instead of 70.</summary>
        public bool AsciiOnly { get; set; } = true;

        public HttpSmsOptions Http { get; set; } = new();
    }

    /// <summary>
    /// Generic HTTP gateway: a GET request to Url with {phone} and {text}
    /// replaced (URL-encoded). Works with most Azerbaijani SMS providers.
    /// </summary>
    public class HttpSmsOptions
    {
        public string? Url { get; set; }
        /// <summary>Optional: the response must contain this text to count as sent.</summary>
        public string? SuccessContains { get; set; }
    }
}
