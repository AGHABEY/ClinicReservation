using ClinicReservation.Modules;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ClinicReservation.Web
{
    public static class ResultExtensions
    {
        /// <summary>Puts a use case's errors on the form (field errors next to the field, others in the summary).</summary>
        public static void AddErrors(this ModelStateDictionary modelState, Result result)
        {
            foreach (var (field, message) in result.Errors)
                modelState.AddModelError(field ?? "", message);
        }
    }
}
