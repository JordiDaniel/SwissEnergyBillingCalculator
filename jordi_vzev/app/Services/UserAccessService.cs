using System.Text.Json;

namespace ZevEbixWeb.Services;

public sealed class UserAccessService
{
    public const string AdminRole = "admin";
    public const string PropertyUserRole = "property_user";

    private const string OptionsPath = "/data/options.json";

    public UserValidationResult ValidateUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return UserValidationResult.Invalid;
        }

        var users = LoadUsers();
        var user = users.FirstOrDefault(x =>
            string.Equals(x.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Password, password, StringComparison.Ordinal));

        if (user is null)
        {
            return UserValidationResult.Invalid;
        }

        return new UserValidationResult(true, user.Username, user.Role, user.PropertyName);
    }

    private static List<AppUser> LoadUsers()
    {
        if (!File.Exists(OptionsPath))
        {
            return [new AppUser("admin", "admin", AdminRole, null)];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(OptionsPath));
            if (!doc.RootElement.TryGetProperty("users", out var usersProp) || usersProp.ValueKind != JsonValueKind.Array)
            {
                return [new AppUser("admin", "admin", AdminRole, null)];
            }

            var result = new List<AppUser>();
            foreach (var element in usersProp.EnumerateArray())
            {
                var username = element.TryGetProperty("username", out var u) ? (u.GetString() ?? string.Empty).Trim() : string.Empty;
                var password = element.TryGetProperty("password", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                var role = element.TryGetProperty("role", out var r) ? (r.GetString() ?? PropertyUserRole).Trim().ToLowerInvariant() : PropertyUserRole;
                var property = element.TryGetProperty("property", out var pr) ? pr.GetString()?.Trim() : null;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    continue;
                }

                if (role is not (AdminRole or PropertyUserRole))
                {
                    role = PropertyUserRole;
                }

                result.Add(new AppUser(username, password, role, property));
            }

            if (result.Count == 0)
            {
                result.Add(new AppUser("admin", "admin", AdminRole, null));
            }

            return result;
        }
        catch
        {
            return [new AppUser("admin", "admin", AdminRole, null)];
        }
    }

    private sealed record AppUser(string Username, string Password, string Role, string? PropertyName);
}

public readonly record struct UserValidationResult(bool IsValid, string Username, string Role, string? PropertyName)
{
    public static UserValidationResult Invalid => new(false, string.Empty, string.Empty, null);
}
