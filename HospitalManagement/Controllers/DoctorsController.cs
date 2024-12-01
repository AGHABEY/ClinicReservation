using HospitalManagement.Data;
using HospitalManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Controllers;

public class DoctorsController : Controller
{
    private readonly ApplicationDbContext _context;

    public DoctorsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _context.Doctors.ToListAsync());
    }
    
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();
        var doctor = await _context.Doctors.FirstOrDefaultAsync(m => m.Id == id);
        if (doctor == null)
            return NotFound();
        return View(doctor);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create([Bind("Name,Specialization,PhoneNumber,Email")] Doctor doctor)
    {
        if (ModelState.IsValid)
        {
            _context.Add(doctor);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        return View(doctor);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();
        var doctor = await _context.Doctors.FindAsync(id);
        if (doctor == null)
            return NotFound();
        return View(doctor);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Specialization,PhoneNumber,Email")] Doctor doctor)
    {
        if (id != doctor.Id)
            return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(doctor);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DoctorExists(doctor.Id))
                    return NotFound();
                else
                    throw;
            }
            return RedirectToAction(nameof(Index));
        }

        return View(doctor);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == id);
        if (doctor == null)
            return NotFound();
        return View(doctor);
    }
    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var doctor = await _context.Doctors.FindAsync(id);
        _context.Doctors.Remove(doctor);
        await _context.SaveChangesAsync();
        return RedirectToAction("Index");
    }
private bool DoctorExists(int id)
    {
        return _context.Doctors.Any(e => e.Id == id);
    }
}