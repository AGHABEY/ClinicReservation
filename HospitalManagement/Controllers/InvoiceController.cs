using HospitalManagement.Data;
using HospitalManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Controllers;

public class InvoiceController:Controller
{
    private readonly ApplicationDbContext _context;

    public InvoiceController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var invoices = _context.Invoices
            .Include(i => i.Appointment)
            .ThenInclude(a => a.Patient);
        return View(await invoices.ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();
        var invoice=_context.Invoices
            .Include(i=>i.Appointment)
            .ThenInclude(p=>p.Patient)
            .Include(i=>i.InvoiceItems)
            .FirstOrDefaultAsync(m=>m.Id==id);

        if (invoice == null)
            return NotFound();
        return View(invoice);
    }

    public IActionResult Create()
    {
        ViewData["AppointmentId"] = new SelectList(_context.Appointments.Include(a => a.Patient), "Id", "Patient.Name");
        return View();
    }
    [HttpPost]
    public async Task<IActionResult> Create([Bind("AppointmentId,TotalAmount,InvoiceDate,Status")] Invoice invoice)
    {
        if (ModelState.IsValid)
        {
            _context.Add(invoice);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        ViewData["AppointmentId"] = new SelectList(_context.Appointments.Include(a => a.Patient), "Id", "Patient.Name", invoice.AppointmentId);
        return View(invoice);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();
        var invoice = _context.Invoices.FindAsync(id);
        if (invoice == null)
            return NotFound();
        
            return View(invoice);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id,
        [Bind("Id,AppointmentId,TotalAmount,InvoiceDate,Status")] Invoice invoice)
    {
        if (id != invoice.Id)
            return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(invoice);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InvoiceExists(invoice.Id))
                    return NotFound();
                else
                    throw;
            }
            return RedirectToAction(nameof(Index));
        }
        ViewData["AppointmentId"] = new SelectList(_context.Appointments.Include(a => a.Patient), "Id", "Patient.Name", invoice.AppointmentId);
        return View(invoice);
    }
    
    private bool InvoiceExists(int id)
    {
        return _context.Invoices.Any(e => e.Id == id);
    }
}