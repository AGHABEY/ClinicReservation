using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _context;

    public AccountController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, RoleManager<IdentityRole> roleManager, AppDbContext context)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _context = context;
    }

    public IActionResult CreateUser()
    {
        AccountViewModel model = new AccountViewModel();
        ViewBag.Roles = _context.Roles.ToList();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(AccountViewModel model)
    {
        if (ModelState.IsValid)
        {
            AppUser newUser = new AppUser()
            {
                UserName = model.UserName,
            };
            var result = await _userManager.CreateAsync(newUser, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction("Index");
            }
            else
            {
                foreach (var item in result.Errors)
                {
                    ModelState.AddModelError("", item.Description);
                }

                return View(model);
            }
        }
        return View(model);
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.ToListAsync();
        return View(users);
    }


    public IActionResult CreateRole()
    {
        AdminRoleViewModel model = new AdminRoleViewModel();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole(AdminRoleViewModel model)
    {
        await _roleManager.CreateAsync(model.Role);
        return RedirectToAction("Roles");
    }
}
