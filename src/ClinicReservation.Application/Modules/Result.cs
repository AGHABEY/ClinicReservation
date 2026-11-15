namespace ClinicReservation.Modules
{
    /// <summary>
    /// Outcome of a use case: errors (optionally tied to a form field) or a success message and id.
    /// Controllers turn it into ModelState / flash messages; an API would turn it into JSON.
    /// </summary>
    public sealed class Result
    {
        public List<(string? Field, string Message)> Errors { get; } = new();
        public bool Ok => Errors.Count == 0;
        /// <summary>Text for the success flash.</summary>
        public string? Message { get; set; }
        /// <summary>Id of what was created or changed.</summary>
        public int? Id { get; set; }
        /// <summary>Extra warning shown next to the success (e.g. a stock shortage).</summary>
        public string? Warning { get; set; }

        public Result Fail(string message, string? field = null)
        {
            Errors.Add((field, message));
            return this;
        }

        public string ErrorText => string.Join(" ", Errors.Select(e => e.Message));

        public static Result Success(string? message = null, int? id = null) => new() { Message = message, Id = id };
        public static Result Failure(string message, string? field = null) => new Result().Fail(message, field);
    }
}
