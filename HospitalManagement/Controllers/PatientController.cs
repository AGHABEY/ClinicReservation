using HospitalManagement.Data;
using HospitalManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Controllers;

public class PatientController:Controller
{
    private readonly ApplicationDbContext _context;

    public PatientController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _context.Patients.ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == id);
        if (patient == null)
            return NotFound();
        return View(patient);
    }

    public IActionResult Create()
    {
        return View();
    }
    [HttpPost]
    public async Task<IActionResult> Create([Bind("FullName,BirthDate,PhoneNumber,Address")] Patient patient)
    {
        if (ModelState.IsValid)
        {
            _context.Add(patient);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        return View(patient);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();
        var patient = await _context.Patients.FindAsync(id);
        if (patient == null)
            return NotFound();
        return View(patient);
    }
    
   [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,BirthDate,PhoneNumber,Email,Address")] Patient patient)
    {
        if (id != patient.Id)
            return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(patient);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PatientExists(patient.Id))
                    return NotFound();
                else
                    throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(patient);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == id);
        if (patient == null)
            return NotFound();
        return View(patient);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var patients = await _context.Patients.FindAsync(id);
        _context.Patients.Remove(patients);
        await _context.SaveChangesAsync();
        return RedirectToAction("Index");
    }
    private bool PatientExists(int id)
    {
        return _context.Patients.Any(e => e.Id == id);
    }
}