using ClinicReservation.Authorization;
using ClinicReservation.Data;
using ClinicReservation.Services;
using ClinicReservation.Models;
using ClinicReservation.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReservation.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public AccountController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, RoleManager<IdentityRole> roleManager, AppDbContext context, AuditService audit)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
            _audit = audit;
        }

        // ---------- Login / logout ----------

        [AllowAnonymous]
        public IActionResult Login(string? returnUrl)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToLocal(returnUrl);

            ViewBag.ReturnUrl = returnUrl;
            return View(new AccountViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(AccountViewModel model, string? returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            if (!ModelState.IsValid)
                return View(model);

            var result = await _signInManager.PasswordSignInAsync(model.UserName.Trim(), model.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                _audit.Log("Sistemə giriş", AuditEntities.Auth, null, null, model.UserName.Trim());
                return RedirectToLocal(returnUrl);
            }

            _audit.Log(result.IsLockedOut ? "Hesab bloklandı (çox uğursuz cəhd)" : "Uğursuz giriş cəhdi", AuditEntities.Auth, null, null, model.UserName.Trim());

            ModelState.AddModelError("", result.IsLockedOut
                ? "Çox sayda uğursuz cəhd. Bir neçə dəqiqə sonra yenidən yoxlayın."
                : "İstifadəçi adı və ya şifrə yanlışdır");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ---------- Users ----------

        [HasPermission(Permissions.UsersManage)]
        public IActionResult Index()
        {
            var model = new UserListViewModel
            {
                Users = _context.AppUsers.OrderBy(u => u.UserName).ToList(),
                Roles = _context.Roles.ToList(),
                UserRoles = _context.UserRoles.ToList()
            };
            return View(model);
        }

        [HasPermission(Permissions.UsersManage)]
        public IActionResult CreateUser()
        {
            ViewBag.Roles = _context.Roles.OrderBy(r => r.Name).ToList();
            return View(new AccountViewModel());
        }

        [HasPermission(Permissions.UsersManage)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(AccountViewModel model)
        {
            var role = string.IsNullOrEmpty(model.RoleId) ? null : await _roleManager.FindByIdAsync(model.RoleId);
            if (role == null)
                ModelState.AddModelError(nameof(model.RoleId), "Rol seçilməlidir");

            if (ModelState.IsValid)
            {
                var newUser = new AppUser { UserName = model.UserName.Trim() };
                var result = await _userManager.CreateAsync(newUser, model.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(newUser, role!.Name!);
                    _audit.Log("İstifadəçi yaradıldı", AuditEntities.User, newUser.UserName, $"rol: {AppRoles.Label(role.Name)}");
                    TempData["Success"] = $"«{newUser.UserName}» istifadəçisi yaradıldı";
                    return RedirectToAction(nameof(Index));
                }

                foreach (var item in result.Errors)
                    ModelState.AddModelError("", TranslateIdentityError(item));
            }

            ViewBag.Roles = _context.Roles.OrderBy(r => r.Name).ToList();
            return View(model);
        }

        [HasPermission(Permissions.UsersManage)]
        public async Task<IActionResult> UpdateUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            var currentRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault();
            var model = new UpdateUserViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                RoleId = currentRole == null ? null : (await _roleManager.FindByNameAsync(currentRole))?.Id
            };
            ViewBag.Roles = _context.Roles.OrderBy(r => r.Name).ToList();
            return View(model);
        }

        [HasPermission(Permissions.UsersManage)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUser(UpdateUserViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
                return NotFound();

            var role = string.IsNullOrEmpty(model.RoleId) ? null : await _roleManager.FindByIdAsync(model.RoleId);
            if (role == null)
                ModelState.AddModelError(nameof(model.RoleId), "Rol seçilməlidir");

            // Never let an admin remove their own admin rights (would lock everyone out).
            if (role != null && role.Name != AppRoles.Admin && user.Id == _userManager.GetUserId(User))
                ModelState.AddModelError(nameof(model.RoleId), "Öz Admin rolunuzu dəyişə bilməzsiniz");

            if (!ModelState.IsValid)
            {
                model.UserName = user.UserName;
                ViewBag.Roles = _context.Roles.OrderBy(r => r.Name).ToList();
                return View(model);
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, role!.Name!);
            // Forces the user's existing sessions to pick up the new role.
            await _userManager.UpdateSecurityStampAsync(user);
            _audit.Log("Rol dəyişdirildi", AuditEntities.User, user.UserName,
                $"{string.Join(", ", currentRoles.Select(AppRoles.Label))} → {AppRoles.Label(role.Name)}");

            TempData["Success"] = $"«{user.UserName}» üçün rol yeniləndi";
            return RedirectToAction(nameof(Index));
        }

        [HasPermission(Permissions.UsersManage)]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();
            return View(new ResetPasswordViewModel { Id = user.Id, UserName = user.UserName });
        }

        [HasPermission(Permissions.UsersManage)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
                return NotFound();
            model.UserName = user.UserName;

            if (!ModelState.IsValid)
                return View(model);

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword!);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                    ModelState.AddModelError("", TranslateIdentityError(e));
                return View(model);
            }

            // A forgotten password often comes with a lockout — clear it too.
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);

            _audit.Log("Şifrə sıfırlandı", AuditEntities.User, user.UserName);
            TempData["Success"] = $"«{user.UserName}» üçün yeni şifrə təyin edildi";
            return RedirectToAction(nameof(Index));
        }

        [HasPermission(Permissions.UsersManage)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            if (user.Id == _userManager.GetUserId(User))
            {
                TempData["Error"] = "Öz hesabınızı silə bilməzsiniz";
                return RedirectToAction(nameof(Index));
            }

            if (await _userManager.IsInRoleAsync(user, AppRoles.Admin)
                && (await _userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count <= 1)
            {
                TempData["Error"] = "Sistemdə ən azı bir administrator qalmalıdır";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                // A deleted login can't stay linked to a doctor.
                foreach (var d in _context.Doctors.Where(d => d.UserId == user.Id))
                    d.UserId = null;
                _context.SaveChanges();
                _audit.Log("İstifadəçi silindi", AuditEntities.User, user.UserName);
            }
            if (result.Succeeded)
                TempData["Success"] = $"«{user.UserName}» istifadəçisi silindi";
            else
                TempData["Error"] = string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        // ---------- Own password ----------

        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction(nameof(Login));

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword!, model.NewPassword!);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                {
                    if (e.Code == "PasswordMismatch")
                        ModelState.AddModelError(nameof(model.CurrentPassword), "Cari şifrə yanlışdır");
                    else
                        ModelState.AddModelError("", TranslateIdentityError(e));
                }
                return View(model);
            }

            // Keep this session signed in with the refreshed security stamp.
            await _signInManager.RefreshSignInAsync(user);
            _audit.Log("Öz şifrəsini dəyişdi", AuditEntities.User, user.UserName);
            TempData["Success"] = "Şifrəniz dəyişdirildi";
            return RedirectToAction("Index", "Home");
        }

        // ---------- Roles ----------

        [HasPermission(Permissions.UsersManage)]
        public IActionResult CreateRole()
        {
            return View(new AdminRoleViewModel());
        }

        [HasPermission(Permissions.UsersManage)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRole(AdminRoleViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var name = model.Name!.Trim();
            if (await _roleManager.RoleExistsAsync(name))
            {
                ModelState.AddModelError(nameof(model.Name), "Bu adda rol artıq mövcuddur");
                return View(model);
            }

            var result = await _roleManager.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                    ModelState.AddModelError("", e.Description);
                return View(model);
            }

            _audit.Log("Rol yaradıldı", AuditEntities.Role, name);
            TempData["Success"] = $"«{name}» rolu yaradıldı — indi icazələrini seçin";
            return RedirectToAction("Index", "Role");
        }

        // ---------- Helpers ----------

        private IActionResult RedirectToLocal(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Index", "Home");

        private static string TranslateIdentityError(IdentityError e) => e.Code switch
        {
            "DuplicateUserName" => "Bu istifadəçi adı artıq mövcuddur",
            "InvalidUserName" => "İstifadəçi adında yalnız hərf, rəqəm və - . _ @ + simvolları ola bilər",
            "PasswordTooShort" => "Şifrə çox qısadır",
            _ => e.Description
        };
    }
}
