using HospitalManagement.Data;
using HospitalManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Controllers;

public class AppointmentController:Controller
{
    private readonly ApplicationDbContext _context;

    public AppointmentController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var appDbContext = _context.Appointments
            .Include(d => d.Doctor)
            .Include(p => p.Patient);
        return View(await appDbContext.ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();
        var appointment = await _context.Appointments
            .Include(d => d.Doctor)
            .Include(p => p.Patient)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (appointment == null)
            return NotFound();

        return View(appointment);
    }
    public IActionResult Create()
    {
        ViewBag.DoctorId = new SelectList(_context.Doctors, "Id", "Name");
        ViewBag.PatientId = new SelectList(_context.Patients, "Id", "FullName");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create([Bind("AppointmentDate,DoctorId,PatientId,Notes")] Appointment appointment)
    {
        if (ModelState.IsValid)
        {
            _context.Add(appointment);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        ViewBag.DoctorId = new SelectList(_context.Doctors, "Id", "Name", appointment.DoctorId);
        ViewBag.PatientId = new SelectList(_context.Patients, "Id", "FullName", appointment.PatientId);
        return View(appointment);
    }


    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var appointment = await _context.Appointments.FindAsync(id);
        if (appointment == null)
            return NotFound();
        ViewBag.DoctorId = new SelectList(_context.Doctors, "Id", "Name", appointment.DoctorId);
        ViewBag.PatientId = new SelectList(_context.Patients, "Id", "FullName", appointment.PatientId);    
        return View(appointment);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id,[Bind("Id,AppointmentDateTime,DoctorId,PatientId,Notes")] Appointment appointment)
    {
        if (id != appointment.Id)
            return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(appointment);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AppointmentExists(appointment.Id))
                    return NotFound();
                else
                    throw;
            } return RedirectToAction(nameof(Index)); 
        }
        ViewBag.DoctorId = new SelectList(_context.Doctors, "Id", "Name", appointment.DoctorId);
        ViewBag.PatientId = new SelectList(_context.Patients, "Id", "FullName", appointment.PatientId);    
        return View(appointment);
        }
    
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();

        var appointment = await _context.Appointments
            .Include(a => a.Doctor)
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (appointment == null)
            return NotFound();

        return View(appointment);
    }
    [HttpPost]
    
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var appointment = await _context.Appointments.FindAsync(id);
        _context.Appointments.Remove(appointment);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
    private bool AppointmentExists(int id)
    {
        return _context.Appointments.Any(e => e.Id == id);
    }

}
