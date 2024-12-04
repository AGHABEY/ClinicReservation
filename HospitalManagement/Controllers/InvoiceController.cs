using HospitalManagement.Data;
using HospitalManagement.Models;
using HospitalManagement.ViewModel;
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

    public IActionResult Create()
    {
        var viewModel = new InvoiceCreateModel
        {
            Invoice = new Invoice(),
            Appointments = _context.Appointments
                .Include(a => a.Patient)
                .Select(a => new SelectListItem
                {
                    Value = a.Id.ToString(),
                    Text = $"{a.Patient.FullName} - {a.AppointmentDate.ToString("dd.MM.yyyy HH:mm")}"
                }).ToList()
        };
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> Create(InvoiceCreateModel viewModel)
    {
        if (ModelState.IsValid)
        {
            _context.Invoices.Add(viewModel.Invoice);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        viewModel.Appointments = _context.Appointments
            .Include(a => a.Patient)
            .Select(a => new SelectListItem
            {
                Value = a.Id.ToString(),
                Text = $"{a.Patient.FullName} - {a.AppointmentDate.ToString("dd.MM.yyyy HH:mm")}"
            }).ToList();
        
        return View(viewModel);
    }

    public async Task<IActionResult> Index()
    {
        var invoices = _context.Invoices
            .Include(a => a.Appointment)
            .ThenInclude(p => p.ToString())
            .Include(a => a.Appointment).ThenInclude(d => d.Doctor);
        return View(await invoices.ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();
        var invoice = _context.Invoices
            .Include(a => a.Appointment)
            .ThenInclude(p => p.Patient)
            .Include(a => a.Appointment)
            .ThenInclude(d => d.Doctor);
        
        if(invoice == null)
            return NotFound();
        
        return View(invoice);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if(id==null)
            return NotFound();
        var invoice =await _context.Invoices.FindAsync(id);
        if(invoice == null)
            return NotFound();

        var viewMode = new InvoiceCreateModel
        {
            Invoice = invoice,
            Appointments = _context.Appointments
                .Include(p => p.Patient)
                .Select(a => new SelectListItem
                {
                    Value = a.Id.ToString(),
                    Text = $"{a.Patient.FullName} - {a.AppointmentDate.ToString("dd.MM.yyyy HH:mm")}",
                    Selected = a.Id == invoice.AppointmentId
                }).ToList()
        };
        return View(viewMode);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, InvoiceCreateModel viewModel)
    {
        if(id!=viewModel.Invoice.Id)
            return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
               _context.Update(viewModel.Invoice);
               await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InvoiceExists(viewModel.Invoice.Id))
                    return NotFound();
                else
                    throw;
            }
            return RedirectToAction(nameof(Index));
        }

        viewModel.Appointments = _context.Appointments
            .Include(a => a.Patient)
            .Select(a => new SelectListItem
            {
                Value = a.Id.ToString(),
                Text = $"{a.Patient.FullName} - {a.AppointmentDate.ToString("dd.MM.yyyy HH:mm")}",
                Selected = a.Id == viewModel.Invoice.AppointmentId
            }).ToList();
        
        return View(viewModel);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();
        var invoice = _context.Invoices
            .Include(a => a.Appointment)
            .ThenInclude(p => p.Patient)
            .Include(a => a.Appointment).ThenInclude(d => d.Doctor);
        if(invoice == null)
            return NotFound();
        return View(invoice);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var invoice = _context.Invoices.Find(id);
        _context.Invoices.Remove(invoice);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
    
    private bool InvoiceExists(int id)
    {
        return _context.Invoices.Any(e => e.Id == id);
    }
}