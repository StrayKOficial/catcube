using Supabase;

namespace CatCube.Launcher.Auth;

public class ProfileService
{
    private static Profile? _currentProfile;
    public static Profile? CurrentProfile => _currentProfile;
    public static string StatusMessage { get; private set; } = "Ready";

    public static void InitializeGuestProfile(string username)
    {
        _currentProfile = new Profile 
        { 
            Id = "guest-id",
            Username = username,
            ShirtColor = "#CC3333",
            PantsColor = "#264073",
            SkinColor = "#FFD9B8",
            BodyType = 0,
            HairStyle = 1
        };
        StatusMessage = "Guest Profile Active";
    }

    public static async Task<Profile?> FetchProfileAsync()
    {
        StatusMessage = "Checking Auth...";
        if (AuthService.Client == null) { StatusMessage = "Error: Client is null"; return null; }
        if (AuthService.UserId == null) { StatusMessage = "Error: UserID is null"; return null; }

        try
        {
            StatusMessage = "Fetching from Database...";
            var response = await AuthService.Client
                .From<Profile>()
                .Where(x => x.Id == AuthService.UserId)
                .Single();
            
            if (response == null)
            {
                StatusMessage = "No profile found. Creating default...";
                _currentProfile = new Profile 
                { 
                    Id = AuthService.UserId ?? "0000",
                    Username = AuthService.Username ?? "Player"
                };
            }
            else
            {
                _currentProfile = response;
                StatusMessage = "Profile Loaded";
            }
            return _currentProfile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Profile] Fetch Error: {ex.Message}");
            StatusMessage = $"Fetch failed: {ex.Message}. Creating local default...";
            
            // If profile doesn't exist, create it locally with default values
            _currentProfile = new Profile 
            { 
                Id = AuthService.UserId,
                Username = AuthService.Username ?? "Player"
            };
            return _currentProfile;
        }
    }

    public static async Task<bool> SaveProfileAsync(Profile profile)
    {
        if (AuthService.Client == null) return false;

        try
        {
            _currentProfile = profile;
            
            await AuthService.Client
                .From<Profile>()
                .Upsert(profile);
                
            Console.WriteLine("[Profile] Saved successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Profile] Save Error: {ex.Message}");
            return false;
        }
    }
}
