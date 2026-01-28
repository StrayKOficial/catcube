using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using System.Numerics;
using Silk.NET.OpenGL.Extensions.ImGui;
using ImGuiNET;
using System.Diagnostics;
using CatCube.Shared;
using CatCube.Launcher.Auth;

namespace CatCube.Launcher;

class Program
{
    private static IWindow _window = null!;
    private static GL _gl = null!;
    private static ImGuiController _imGuiController = null!;
    private static IInputContext _inputContext = null!;
    
    // State
    private static string _targetIp = "offer-innocent.gl.at.ply.gg";
    private static int _targetPort = 27908;
    private static List<string> _availableMaps = new();
    
    // UI Navigation
    private enum MenuTab { Home, Games, Servers, Avatar, Settings }
    private static MenuTab _currentTab = MenuTab.Home;
    
    // Auth State
    private static bool _isAuthInitialized = false;
    private static bool _showLoginScreen = true;
    private static string _authEmail = "";
    private static string _authPassword = "";
    private static string _authUsername = "";
    private static string _authMessage = "";
    private static bool _isRegistering = false;

    static void Main(string[] args)
    {
        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(1024, 720);
        options.Title = "CatCube Platform Launcher";
        options.WindowBorder = WindowBorder.Fixed; // Clean professional look

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _window.Run();
    }

    private static void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _inputContext = _window.CreateInput();
        _imGuiController = new ImGuiController(_gl, _window, _inputContext);
        
        RefreshMaps();
        DarkTheme();
        
        // Initialize auth in background
        Task.Run(async () => {
            await AuthService.InitializeAsync();
            _isAuthInitialized = true;
            if (AuthService.IsLoggedIn) _showLoginScreen = false;
        });
    }

    private static void RefreshMaps()
    {
        _availableMaps.Clear();
        string mapsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "maps");
        if (Directory.Exists(mapsPath))
        {
            foreach (var dir in Directory.GetDirectories(mapsPath))
            {
                _availableMaps.Add(Path.GetFileName(dir));
            }
        }
    }

    private static void OnRender(double dt)
    {
        _imGuiController.Update((float)dt);
        _gl.ClearColor(0.08f, 0.08f, 0.1f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        DrawMainLayout();

        _imGuiController.Render();
    }

    private static void DrawMainLayout()
    {
        // Show login screen if not authenticated
        if (_showLoginScreen)
        {
            DrawLoginScreen();
            return;
        }
        
        float sidebarWidth = 200;
        
        // --- Sidebar ---
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(sidebarWidth, _window.Size.Y));
        if (ImGui.Begin("Sidebar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.TextColored(new Vector4(0, 0.8f, 1, 1), "  CATCUBE PLATFORM");
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Selectable("  HOME", _currentTab == MenuTab.Home)) _currentTab = MenuTab.Home;
            if (ImGui.Selectable("  DISCOVER", _currentTab == MenuTab.Games)) _currentTab = MenuTab.Games;
            if (ImGui.Selectable("  SERVERS", _currentTab == MenuTab.Servers)) _currentTab = MenuTab.Servers;
            if (ImGui.Selectable("  AVATAR", _currentTab == MenuTab.Avatar)) 
            {
                _currentTab = MenuTab.Avatar;
                Task.Run(async () => await ProfileService.FetchProfileAsync()); 
            }
            
            ImGui.SetCursorPosY(_window.Size.Y - 40);
            ImGui.Separator();
            if (ImGui.Selectable("  EXIT", false)) _window.Close();
            
            ImGui.End();
        }

        // --- Main Content Area ---
        ImGui.SetNextWindowPos(new Vector2(sidebarWidth, 0));
        ImGui.SetNextWindowSize(new Vector2(_window.Size.X - sidebarWidth, _window.Size.Y));
        if (ImGui.Begin("Content", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            if (_currentTab == MenuTab.Home) DrawHomeTab();
            else if (_currentTab == MenuTab.Games) DrawGamesTab();
            else if (_currentTab == MenuTab.Servers) DrawServersTab();
            else if (_currentTab == MenuTab.Avatar) DrawAvatarTab();
            ImGui.End();
        }
    }

    private static void DrawHomeTab()
    {
        ImGuiExtensions.TextSizeAnd16("Home", 32);
        ImGui.TextDisabled("Welcome back to CatCube. Jump back in!");
        ImGui.Separator();
        ImGui.Spacing();

        // Banner for the Hub
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.15f, 1.0f));
        if (ImGui.BeginChild("Banner", new Vector2(0, 200), true))
        {
            ImGui.SetCursorPos(new Vector2(20, 40));
        ImGuiExtensions.TextSizeAnd16("CATCUBE MONUMENTAL HUB", 24);
            ImGui.Text("The heart of the square world. Portals to all games inside.");
            
            ImGui.SetCursorPos(new Vector2(20, 140));
            if (ImGui.Button("JOIN HUB (Playit)", new Vector2(180, 40)))
            {
                LaunchClientOnly("offer-innocent.gl.at.ply.gg", 27908, "Hub");
            }
            ImGui.SameLine();
            if (ImGui.Button("HOST OWN HUB", new Vector2(180, 40)))
            {
                LaunchServerAndClient("Hub");
            }
            ImGui.EndChild();
        }
        ImGui.PopStyleColor();
    }

    private static void DrawGamesTab()
    {
        ImGuiExtensions.TextSizeAnd16("Discover Games", 32);
        if (ImGui.Button("Refresh Maps")) RefreshMaps();
        ImGui.Separator();
        ImGui.Spacing();

        // Map Cards
        float windowWidth = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)(windowWidth / 220));
        
        if (ImGui.BeginTable("GamesTable", columns))
        {
            foreach (var map in _availableMaps)
            {
                ImGui.TableNextColumn();
                DrawGameCard(map);
            }
            ImGui.EndTable();
        }
    }

    private static void DrawGameCard(string mapName)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        if (ImGui.BeginChild(mapName, new Vector2(200, 240), true))
        {
            // Placeholder Image Box
            ImGui.GetWindowDrawList().AddRectFilled(
                ImGui.GetCursorScreenPos(), 
                ImGui.GetCursorScreenPos() + new Vector2(180, 120), 
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.4f, 0.6f, 1.0f)), 8f);
            
            ImGui.SetCursorPosY(130);
            ImGuiExtensions.TextSizeAnd16(mapName, 18);
            ImGui.TextDisabled("by strayk");
            
            ImGui.SetCursorPosY(200);
            if (ImGui.Button($"Play##{mapName}", new Vector2(180, 30)))
            {
                LaunchServerAndClient(mapName);
            }
            ImGui.EndChild();
        }
        ImGui.PopStyleVar();
    }

    private static string _targetMap = "Hub";

    private static void DrawServersTab()
    {
        ImGuiExtensions.TextSizeAnd16("Active Servers", 32);
        ImGui.TextDisabled("Join games already running on your network.");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.InputText("Server IP", ref _targetIp, 100);
        ImGui.InputInt("Port", ref _targetPort);
        ImGui.InputText("Map Name", ref _targetMap, 50);
        
        if (ImGui.Button("Direct Connect", new Vector2(150, 40)))
        {
            LaunchClientOnly(_targetIp, _targetPort, _targetMap);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Quick Connect:");
        
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.15f, 1.0f));
        if (ImGui.BeginChild("ServersList", new Vector2(0, 0), true))
        {
            // Playit Public Server
            ImGui.Text("CatCube Hub (Playit)");
            ImGui.SameLine(ImGui.GetWindowWidth() - 110);
            if (ImGui.Button("Connect##playit")) LaunchClientOnly("offer-innocent.gl.at.ply.gg", 27908, "Hub");
            
            // Local Dev Server
            ImGui.Text("Local Server (localhost:9050)");
            ImGui.SameLine(ImGui.GetWindowWidth() - 110);
            if (ImGui.Button("Connect##local")) LaunchClientOnly("127.0.0.1", 9050, "Hub");
            
            ImGui.EndChild();
        }
        ImGui.PopStyleColor();
    }

    private static void DrawAvatarTab()
    {
        ImGuiExtensions.TextSizeAnd16("Character Customization", 32);
        ImGui.TextDisabled("Customize your appearance in the CatCube multiverse.");
        ImGui.Separator();
        ImGui.Spacing();

        if (!AuthService.IsLoggedIn)
        {
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "You must be logged in to customize your avatar.");
            if (ImGui.Button("Go to Home")) _currentTab = MenuTab.Home;
            return;
        }

        var profile = ProfileService.CurrentProfile;
        if (profile == null)
        {
            ImGui.Text($"Status: {ProfileService.StatusMessage}");
            if (ImGui.Button("Retry Fetch")) Task.Run(async () => await ProfileService.FetchProfileAsync());
            return;
        }

        ImGui.BeginChild("AvatarEditor", new Vector2(0, 0), true);
        
        ImGui.Columns(2, "AvatarCols", false);
        ImGui.SetColumnWidth(0, 300);
        
        // --- Left Column: Controls ---
        ImGuiExtensions.TextSizeAnd16("Appearance", 20);
        ImGui.Spacing();
        
        // Colors
        Vector3 shirtCol = HexToVector3(profile.ShirtColor);
        if (ImGui.ColorEdit3("Shirt Color", ref shirtCol)) profile.ShirtColor = Vector3ToHex(shirtCol);
        
        Vector3 pantsCol = HexToVector3(profile.PantsColor);
        if (ImGui.ColorEdit3("Pants Color", ref pantsCol)) profile.PantsColor = Vector3ToHex(pantsCol);
        
        Vector3 skinCol = HexToVector3(profile.SkinColor);
        if (ImGui.ColorEdit3("Skin Color", ref skinCol)) profile.SkinColor = Vector3ToHex(skinCol);
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Body Type
        string[] bodyTypes = { "Standard", "Slim", "Blocky" };
        int currentBody = profile.BodyType;
        if (ImGui.Combo("Body Type", ref currentBody, bodyTypes, bodyTypes.Length)) profile.BodyType = currentBody;
        
        // Hair Style
        string[] hairStyles = { "Bald", "Short", "Long", "Spiky", "Afro" };
        int currentHair = profile.HairStyle;
        if (ImGui.Combo("Hair Style", ref currentHair, hairStyles, hairStyles.Length)) profile.HairStyle = currentHair;
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        if (ImGui.Button("Save Changes", new Vector2(200, 40)))
        {
            Task.Run(async () => await ProfileService.SaveProfileAsync(profile));
        }
        
        ImGui.NextColumn();
        
        // --- Right Column: Preview ---
        ImGuiExtensions.TextSizeAnd16("Preview", 20);
        // Here we would ideally render a 3D preview viewport
        // For now, we'll draw a 2D representation using ImGui primitives as a placeholder
        
        Vector2 p = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        
        float scale = 3.0f;
        float startX = p.X + 100;
        float startY = p.Y + 50;
        
        // Head
        uint skin = ImGui.ColorConvertFloat4ToU32(new Vector4(skinCol, 1.0f));
        drawList.AddRectFilled(new Vector2(startX + 10*scale, startY), new Vector2(startX + 40*scale, startY + 30*scale), skin);
        
        // Torso
        uint shirt = ImGui.ColorConvertFloat4ToU32(new Vector4(shirtCol, 1.0f));
        drawList.AddRectFilled(new Vector2(startX, startY + 30*scale), new Vector2(startX + 50*scale, startY + 80*scale), shirt);
        
        // Legs
        uint pants = ImGui.ColorConvertFloat4ToU32(new Vector4(pantsCol, 1.0f));
        drawList.AddRectFilled(new Vector2(startX, startY + 80*scale), new Vector2(startX + 22*scale, startY + 130*scale), pants); // Left
        drawList.AddRectFilled(new Vector2(startX + 28*scale, startY + 80*scale), new Vector2(startX + 50*scale, startY + 130*scale), pants); // Right
        
        ImGui.EndChild();
    }

    private static Vector3 HexToVector3(string hex)
    {
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length != 6) return new Vector3(1, 1, 1);
        
        float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
        float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
        float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
        return new Vector3(r, g, b);
    }

    private static string Vector3ToHex(Vector3 c)
    {
        int r = (int)(c.X * 255);
        int g = (int)(c.Y * 255);
        int b = (int)(c.Z * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static void LaunchServerAndClient(string mapName)
    {
        // Navigate from bin/Debug/net8.0 up to catcube root
        string rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string serverPath = Path.Combine(rootPath, "src", "CatCube.Server", "CatCube.Server.csproj");
        string clientPath = Path.Combine(rootPath, "src", "CatCube.Client", "CatCube.Client.csproj");

        // Start Server
        Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{serverPath}\" -- --map {mapName}",
            UseShellExecute = true,
            CreateNoWindow = false
        });

        // Start Client with the same map
        string username = AuthService.Username ?? "Guest";
        string avatarArgs = GetAvatarArgs();

        Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{clientPath}\" -- --ip 127.0.0.1 --port 9050 --map {mapName} --username {username} {avatarArgs}",
            UseShellExecute = true,
            CreateNoWindow = false
        });
    }

    private static void LaunchClientOnly(string ip, int port, string mapName)
    {
        string rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string clientPath = Path.Combine(rootPath, "src", "CatCube.Client", "CatCube.Client.csproj");
        string username = AuthService.Username ?? "Guest";
        string avatarArgs = GetAvatarArgs();

        Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{clientPath}\" -- --ip {ip} --port {port} --map {mapName} --username {username} {avatarArgs}",
            UseShellExecute = true,
            CreateNoWindow = false
        });
    }

    private static string GetAvatarArgs()
    {
        var profile = ProfileService.CurrentProfile;
        if (profile == null) return "";
        return $"--shirt \"{profile.ShirtColor}\" --pants \"{profile.PantsColor}\" --skin \"{profile.SkinColor}\" --body {profile.BodyType} --hair {profile.HairStyle}";
    }

    private static void DrawLoginScreen()
    {
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(_window.Size.X, _window.Size.Y));
        
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        
        if (ImGui.Begin("Login", flags))
        {
            // Center the login box
            Vector2 center = new Vector2(_window.Size.X / 2, _window.Size.Y / 3);
            ImGui.SetCursorPos(center - new Vector2(150, 100));
            
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
            if (ImGui.BeginChild("LoginBox", new Vector2(300, 350), true))
            {
                ImGui.SetCursorPosX(20);
                ImGuiExtensions.TextSizeAnd16("CATCUBE ACCOUNT", 24);
                ImGui.Separator();
                ImGui.Spacing();
                
                if (!_isAuthInitialized)
                {
                    ImGui.Text("Connecting to services...");
                }
                else
                {
                    ImGui.Text("Email");
                    ImGui.InputText("##email", ref _authEmail, 100);
                    
                    ImGui.Text("Password");
                    ImGui.InputText("##password", ref _authPassword, 100, ImGuiInputTextFlags.Password);
                    
                    ImGui.Spacing();
                    
                    if (!string.IsNullOrEmpty(_authMessage))
                    {
                        ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), _authMessage);
                    }
                    
                    ImGui.Spacing();
                    
                    if (!_isRegistering)
                    {
                        if (ImGui.Button("Log In", new Vector2(260, 35)))
                        {
                            _authMessage = "Signing in...";
                            Task.Run(async () => {
                                var (success, msg) = await AuthService.SignInAsync(_authEmail, _authPassword);
                                _authMessage = msg;
                                if (success) _showLoginScreen = false;
                            });
                        }
                        
                        ImGui.Spacing();
                        ImGui.Text("Don't have an account?");
                        if (ImGui.Button("Create Account", new Vector2(260, 30))) _isRegistering = true;
                        
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                        
                        if (ImGui.Button("Play as Guest", new Vector2(260, 35)))
                        {
                            string guestName = "Guest_" + new Random().Next(1000, 9999);
                            AuthService.EnterGuestMode(guestName);
                            ProfileService.InitializeGuestProfile(guestName);
                            _showLoginScreen = false;
                        }
                    }
                    else
                    {
                        ImGui.Text("Username");
                        ImGui.InputText("##username", ref _authUsername, 50);
                        
                        if (ImGui.Button("Register", new Vector2(260, 35)))
                        {
                            _authMessage = "Creating account...";
                            Task.Run(async () => {
                                var (success, msg) = await AuthService.SignUpAsync(_authEmail, _authPassword, _authUsername);
                                _authMessage = msg;
                                if (success) _showLoginScreen = false;
                            });
                        }
                        
                        ImGui.Spacing();
                        if (ImGui.Button("Back to Login", new Vector2(260, 30))) _isRegistering = false;
                    }
                }
                
                ImGui.EndChild();
            }
            ImGui.PopStyleVar();
            ImGui.End();
        }
    }

    private static void DarkTheme()
    {
        var style = ImGui.GetStyle();
        style.Colors[(int)ImGuiCol.Text]                   = new Vector4(0.95f, 0.95f, 0.95f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg]               = new Vector4(0.10f, 0.10f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg]                = new Vector4(0.14f, 0.14f, 0.16f, 1.00f);
        style.Colors[(int)ImGuiCol.Header]                 = new Vector4(0.20f, 0.20f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderHovered]          = new Vector4(0.25f, 0.25f, 0.27f, 1.00f);
        style.Colors[(int)ImGuiCol.Button]                 = new Vector4(0.15f, 0.60f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered]          = new Vector4(0.20f, 0.70f, 1.00f, 1.00f);
        
        style.WindowRounding = 0;
        style.ChildRounding = 6;
        style.FrameRounding = 4;
    }

    private static void OnClosing()
    {
        _imGuiController?.Dispose();
        _gl?.Dispose();
    }
}

public static class ImGuiExtensions
{
    public static void TextSizeAnd16(string text, float size)
    {
        // Dummy implementation since we don't have multiple fonts loaded yet
        // In a real app we would PushFont
        ImGui.Text(text);
    }
}
