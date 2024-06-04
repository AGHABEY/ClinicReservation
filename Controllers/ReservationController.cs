using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    public class ReservationController : Controller
    {
        private readonly AppDbContext _context;

        public ReservationController(AppDbContext context) 
        { 
            _context = context;
        }
        public IActionResult Index()
        {
            var reserves = _context.Reserves.Include(r=>r.Status).ToList();
            return View(reserves);
        }

        public IActionResult Add()
        {
            ViewBag.ExaminationTypes = _context.ExaminationTypes.ToList();
            return View();
        }

        [HttpPost]
        public IActionResult Add(Reserve reserve)
        {
            ViewBag.ExaminationTypes = _context.ExaminationTypes.ToList();

            if (ModelState.IsValid)
            {
                reserve.AddedDate = DateTime.Now;
                reserve.Status = _context.ReserveStatuses.FirstOrDefault(rs => rs.Name.Contains("Wait"));
                _context.Reserves.Add(reserve);
                _context.SaveChanges();

                foreach (var item in reserve.ReserveExaminationTypesId)
                {
                    ReserveExaminationType reserveExaminationType = new ReserveExaminationType();
                    reserveExaminationType.ReserveId = reserve.Id;
                    reserveExaminationType.ExaminationTypeId = item;
                    _context.ReserveExaminationTypes.Add(reserveExaminationType);
                    _context.SaveChanges();
                }
                return RedirectToAction("Index");
            }
            ModelState.AddModelError("", "Vacib xanaları doldurun");
            return View(reserve);
        }
    }
}
