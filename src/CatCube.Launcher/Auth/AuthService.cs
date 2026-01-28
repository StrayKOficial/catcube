using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using System.Text.Json;

namespace CatCube.Launcher.Auth;

public class AuthService
{
    public static Supabase.Client? Client { get; private set; }
    private static Session? _currentSession;
    
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".catcube", "session.json");

    public static bool IsLoggedIn => _currentSession != null;
    public static string? Username 
    {
        get 
        {
            if (_isGuest) return _guestUsername;
            if (_currentSession?.User?.UserMetadata != null && 
                _currentSession.User.UserMetadata.TryGetValue("username", out var username))
            {
                return username?.ToString();
            }
            return _currentSession?.User?.Email?.Split('@')[0];
        }
    }
    private static string? _guestUsername;
    public static string? UserId => _isGuest ? "guest-id" : _currentSession?.User?.Id;
    private static bool _isGuest = false;
    public static bool IsGuest => _isGuest;

    public static void EnterGuestMode(string username)
    {
        _isGuest = true;
        _guestUsername = username;
        _currentSession = null;
    }

    public static async Task InitializeAsync()
    {
        var options = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = false
        };
        
        Client = new Supabase.Client(SupabaseConfig.Url, SupabaseConfig.AnonKey, options);
        await Client.InitializeAsync();
        
        // Try to restore session
        await TryRestoreSessionAsync();
    }

    public static async Task<(bool success, string message)> SignUpAsync(string email, string password, string username)
    {
        try
        {
            if (Client == null) await InitializeAsync();
            
            var options = new SignUpOptions
            {
                Data = new Dictionary<string, object>
                {
                    { "username", username }
                }
            };

            var response = await Client!.Auth.SignUp(email, password, options);
            if (response?.User != null)
            {
                _currentSession = response;
                await SaveSessionAsync();
                return (true, "Account created! Check your email to confirm.");
            }
            return (false, "Failed to create account.");
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("email_not_confirmed"))
                return (false, "Email not confirmed. Please check your inbox or disable email confirmation in Supabase Dashboard.");
            return (false, ex.Message);
        }
    }

    public static async Task<(bool success, string message)> SignInAsync(string email, string password)
    {
        try
        {
            if (Client == null) await InitializeAsync();
            
            var response = await Client!.Auth.SignIn(email, password);
            if (response?.User != null)
            {
                _currentSession = response;
                await SaveSessionAsync();
                return (true, $"Welcome back, {Username}!");
            }
            return (false, "Invalid credentials.");
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("email_not_confirmed"))
                return (false, "Email not confirmed. Please check your inbox or disable email confirmation in Supabase Dashboard.");
            return (false, ex.Message);
        }
    }

    public static async Task SignOutAsync()
    {
        if (Client != null)
        {
            await Client.Auth.SignOut();
        }
        _currentSession = null;
        
        if (File.Exists(SessionPath))
            File.Delete(SessionPath);
    }

    private static async Task TryRestoreSessionAsync()
    {
        try
        {
            if (!File.Exists(SessionPath)) return;
            
            var json = await File.ReadAllTextAsync(SessionPath);
            var sessionData = JsonSerializer.Deserialize<SessionData>(json);
            
            if (sessionData != null && !string.IsNullOrEmpty(sessionData.AccessToken) && !string.IsNullOrEmpty(sessionData.RefreshToken))
            {
                // Correct way to restore session in supabase-csharp
                await Client!.Auth.SetSession(sessionData.AccessToken, sessionData.RefreshToken);
                
                var user = Client.Auth.CurrentUser;
                if (user != null)
                {
                    _currentSession = Client.Auth.CurrentSession;
                    Console.WriteLine($"[Auth] Session restored for {Username}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Failed to restore session: {ex.Message}");
        }
    }

    private static async Task SaveSessionAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            
            var sessionData = new SessionData
            {
                AccessToken = _currentSession?.AccessToken,
                RefreshToken = _currentSession?.RefreshToken,
                UserId = _currentSession?.User?.Id
            };
            
            var json = JsonSerializer.Serialize(sessionData);
            await File.WriteAllTextAsync(SessionPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Failed to save session: {ex.Message}");
        }
    }

    private class SessionData
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? UserId { get; set; }
    }
}
