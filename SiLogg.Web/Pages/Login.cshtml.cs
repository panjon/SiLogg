using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SiLogg.Web.Pages;

[AllowAnonymous]
public class LoginModel(IConfiguration configuration) : PageModel
{
    [BindProperty]
    public string? Username { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        var configuredUsername = configuration["SILOGG_AUTH_USERNAME"];
        var configuredPassword = configuration["SILOGG_AUTH_PASSWORD"];

        if (string.IsNullOrEmpty(configuredUsername) || string.IsNullOrEmpty(configuredPassword) ||
            !FixedTimeEquals(Username ?? string.Empty, configuredUsername) ||
            !FixedTimeEquals(Password ?? string.Empty, configuredPassword))
        {
            ErrorMessage = "Fel användarnamn eller lösenord.";
            return Page();
        }

        var claims = new[] { new Claim(ClaimTypes.Name, configuredUsername) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
