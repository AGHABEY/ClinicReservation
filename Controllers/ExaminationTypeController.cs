using ClinicReservation.Data;
using ClinicReservation.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicReservation.Controllers
{
    [Authorize]
    public class ExaminationTypeController : Controller
    {
        private readonly AppDbContext _context;

        public ExaminationTypeController(AppDbContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            var exTypes = _context.ExaminationTypes.ToList();
            return View(exTypes);
        }

        public IActionResult Add()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Add(ExaminationType model) 
        {
            if (ModelState.IsValid)
            {
                _context.ExaminationTypes.Add(model);
                _context.SaveChanges();
                return RedirectToAction("Index");
            }
            ModelState.AddModelError("", "Vacib xanaları doldurun");
            return View(model);
        }


        public IActionResult Edit(int id)
        {
            var et = _context.ExaminationTypes.FirstOrDefault(x => x.Id == id);
            return View(et);
        }

        [HttpPost]
        public IActionResult Edit(ExaminationType model)
        {
            if (ModelState.IsValid)
            {
                _context.ExaminationTypes.Update(model);
                _context.SaveChanges();
                return RedirectToAction("Index");
            }
            ModelState.AddModelError("", "Vacib xanaları doldurun");
            return View(model);
        }
    }
}
