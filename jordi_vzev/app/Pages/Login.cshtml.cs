using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZevEbixWeb.Services;

namespace ZevEbixWeb.Pages;

public class LoginModel(UserAccessService userAccessService) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var validation = userAccessService.ValidateUser(Username, Password);
        if (!validation.IsValid)
        {
            ErrorMessage = "Ungültiger Benutzername oder Passwort.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, validation.Username),
            new(ClaimTypes.Role, validation.Role)
        };

        if (!string.IsNullOrWhiteSpace(validation.PropertyName))
        {
            claims.Add(new("property", validation.PropertyName));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (validation.Role == UserAccessService.AdminRole)
        {
            return RedirectToPage("/Admin");
        }

        return RedirectToPage("/Dashboard");
    }
}
