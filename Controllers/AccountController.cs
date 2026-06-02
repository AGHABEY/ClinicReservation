using ClinicReservation.Data;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace ClinicReservation.Controllers
{
    [Authorize]
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

        [AllowAnonymous]
        public IActionResult Login()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(AccountViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, false);

                if (result.Succeeded)
                {
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    ModelState.AddModelError("", "Username or password is not correct");
                    return View(model);
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
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
            UserListViewModel model = new UserListViewModel();
            model.Users = _context.AppUsers.ToList();
            model.Roles = _context.Roles.ToList();
            model.UserRoles = _context.UserRoles.ToList();
            return View(model);
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
            return RedirectToAction("Index");
        }

        public IActionResult UpdateUser(string id)
        {
            AccountViewModel model = new AccountViewModel();
            model.User = _context.AppUsers.Find(id);
            ViewBag.Roles = _context.Roles.ToList();
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUser(AccountViewModel model)
        {

            AppUser user = _context.AppUsers.Find(model.User.Id);
            user.UserName = model.User.UserName;
            IdentityUserRole<string> userRole = _context.UserRoles.FirstOrDefault(u => u.UserId == model.User.Id);
            if (userRole != null)
            {
                string oldRole = _context.Roles.Find(userRole.RoleId).Name;
                await _userManager.RemoveFromRoleAsync(user, oldRole);
            }

            IdentityRole selectedRole = _context.Roles.Find(model.User.RoleId);

            await _userManager.AddToRoleAsync(user, selectedRole.Name);
            _context.SaveChanges();
            return RedirectToAction("Index", "Account");
        }
    }
}