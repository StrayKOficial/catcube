using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Numerics;
using CatCube.Rendering;
using CatCube.Entities;
using CatCube.World;
using CatCube.Input;
using CatCube.Network;
using CatCube.Shared;
using CatCube.Engine;
using CatCube.Engine.Scripting;
using EnginePart = CatCube.Engine.Part; // Alias to avoid conflict
using NetCoreAudio;
using GamePlayer = CatCube.Entities.Player;
using AudioPlayer = NetCoreAudio.Player;
using Silk.NET.OpenGL.Extensions.ImGui;
using ImGuiNET;

namespace CatCube;

public class Game : IDisposable
{
    private readonly GL _gl;
    private readonly IInputContext _input;
    private readonly IWindow _window;
    
    private Renderer? _renderer;
    private ImGuiController? _imGuiController;
    // Local player logic remains client-side for now until Input is moved to Engine
    private GamePlayer? _player; 
    private Camera? _camera;
    private InputManager? _inputManager;
    private NetworkClient? _netClient;
    
    // Audio
    private AudioPlayer _musicPlayer = new AudioPlayer();
    private string? _currentMusicPath;
    
    // Engine
    private LuaVM? _luaVm;
    private DataModel _dataModel; // Reference to engine data model
    
    private readonly Dictionary<int, RemotePlayer> _remotePlayers = new Dictionary<int, RemotePlayer>();
    
    // Network properties
    private readonly string _serverIp;
    private readonly int _serverPort;
    private readonly string _mapName;
    private readonly string _username;
    private readonly AvatarData _avatar;
    private float _networkSyncTimer = 0f;
    private const float NetworkSyncInterval = 0.05f;

    public Game(GL gl, IInputContext input, IWindow window, string ip, int port, string mapName, string username, AvatarData avatar)
    {
        _gl = gl;
        _input = input;
        _window = window;
        _serverIp = ip;
        _serverPort = port;
        _mapName = mapName;
        _username = username;
        _avatar = avatar;
        _dataModel = DataModel.Current;
    }

    public void Initialize()
    {
        _renderer = new Renderer(_gl);
        _renderer.Initialize();
        
        _imGuiController = new ImGuiController(_gl, _window, _input);
        
        _camera = new Camera(new Vector3(0, 5, 10), _window.Size.X, _window.Size.Y);
        _inputManager = new InputManager(_input);
        
        // Initialize Engine & Lua
        _luaVm = new LuaVM();
        
        // Load Project from argument or default
        string projectPath = Path.Combine("maps", _mapName);
        LoadProject(projectPath);
    }
    
    private void LoadProject(string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            Console.WriteLine($"[Project Error] Project not found: {projectPath}");
            // Fallback to default
            Console.WriteLine("Loading Default Map...");
            try {
                if (File.Exists("maps/default.lua"))
                    _luaVm!.ExecuteFile("maps/default.lua");
            } catch {}
            return;
        }
        
        Console.WriteLine($"[Project] Loading project from {projectPath}...");
        
        // 1. Load Workspace Scripts (Construction)
        string workspaceDir = Path.Combine(projectPath, "Workspace");
        if (Directory.Exists(workspaceDir))
        {
            foreach (var file in Directory.GetFiles(workspaceDir, "*.lua"))
            {
                Console.WriteLine($"[Loader] Running {Path.GetFileName(file)}");
                _luaVm!.ExecuteFile(file);
            }
        }
        
        // 2. Load ServerScriptService (Logic)
        string scriptDir = Path.Combine(projectPath, "ServerScriptService");
        if (Directory.Exists(scriptDir))
        {
            foreach (var file in Directory.GetFiles(scriptDir, "*.lua"))
            {
                Console.WriteLine($"[Loader] Running {Path.GetFileName(file)}");
                _luaVm!.ExecuteFile(file);
            }
        }
        
        Console.WriteLine($"Workspace Children: {_dataModel.Workspace.GetChildren().Length}");
        
        // Create player at safe location (Search for SpawnLocations)
        List<SpawnLocation> spawns = new List<SpawnLocation>();
        SearchSpawns(_dataModel.Workspace, spawns);
        
        Vector3 spawnPos = new Vector3(0, 10, 80); // Default
        if (spawns.Count > 0)
        {
            var randomSpawn = spawns[new Random().Next(spawns.Count)];
            spawnPos = randomSpawn.Position + new Vector3(0, 5, 0); // Spawn slightly above
        }

        _player = new GamePlayer(_gl, spawnPos, _dataModel.Workspace.Physics, _avatar);
        
        // --- v2.3 Camera Sync Fix ---
        // Initialize camera precisely at player's location to prevent "black screens" or abyss starts
        _camera = new Camera(_player.Position + new Vector3(0, 5, 20), _window.Size.X, _window.Size.Y);
        
        // Register Player in DataModel so Lua can see it
        var playerPart = new EnginePart(); // Using alias EnginePart = CatCube.Engine.Part
        playerPart.Name = "Player";
        playerPart.Position = _player.Position;
        playerPart.Physical = false;   // IMPORTANT: Don't create an extra body
        playerPart.Transparency = 1.0f; // IMPORTANT: Don't render the proxy block
        playerPart.CanCollide = true;   // This will allow the SHARED body to collide
        playerPart.Parent = _dataModel.Workspace;
        
        // Register handle in PhysicsSpace for Touched events
        _dataModel.Workspace.Physics.RegisterBody(_player.BodyHandle, playerPart);
        
        // Attach body to part so Lua changes (Position) affect Physics
        playerPart.AttachPhysicsBody(_player.BodyHandle);
        
        // Initialize Network
        _netClient = new NetworkClient();
        _netClient.OnPlayerStateReceived += OnRemotePlayerState;
        _netClient.OnPlayerLeft += OnPlayerLeft;
        _netClient.Connect(_serverIp, _serverPort, _username, _avatar);
        
        // --- v2.6 Background Music ---
        _musicPlayer.PlaybackFinished += (sender, args) => {
            if (_currentMusicPath != null) _musicPlayer.Play(_currentMusicPath);
        };
        
        string musicPath = Path.Combine(projectPath, "music.mp3");
        if (!string.IsNullOrEmpty(musicPath) && File.Exists(musicPath))
        {
            try {
                _currentMusicPath = musicPath;
                _musicPlayer.Play(musicPath);
                Console.WriteLine($"[Audio] Playing background music: {musicPath}");
            } catch (Exception ex) {
                Console.WriteLine($"[Audio Error] Could not play music: {ex.Message}");
            }
        }
    }
    
    private void OnRemotePlayerState(int id, PlayerState state)
    {
        // Don't sync our own ghost
        if (id == _netClient?.LocalId) return;

        if (_remotePlayers.TryGetValue(id, out RemotePlayer? remote))
        {
            remote.UpdateState(new Vector3(state.X, state.Y, state.Z), state.Rotation, state.WalkCycle, state.State, state.Username, state.Avatar);
        }
        else
        {
            Console.WriteLine($"[Game] Discovering new player: {id} ({state.Username}) at ({state.X}, {state.Y}, {state.Z})");
            var newRemote = new RemotePlayer(_gl, id, new Vector3(state.X, state.Y, state.Z));
            newRemote.UpdateState(new Vector3(state.X, state.Y, state.Z), state.Rotation, state.WalkCycle, state.State, state.Username, state.Avatar);
            _remotePlayers[id] = newRemote;
        }
    }
    
    private void OnPlayerLeft(int id)
    {
        if (_remotePlayers.ContainsKey(id))
        {
            Console.WriteLine($"Player left: {id}");
            _remotePlayers[id].Dispose();
            _remotePlayers.Remove(id);
        }
    }

    public void Update(float deltaTime)
    {
        _inputManager?.Update();
        _netClient?.PollEvents();
        _imGuiController?.Update(deltaTime);
        
        if (_inputManager!.IsEscapePressed)
        {
            _window.Close();
            return;
        }
        
        // Update player movement
        _player?.Update(deltaTime, _inputManager, _camera!);
        
        // Send state
        _networkSyncTimer -= deltaTime;
        if (_networkSyncTimer <= 0 && _player != null)
        {
            _networkSyncTimer = NetworkSyncInterval;
            PlayerState myState = new PlayerState
            {
                X = _player.Position.X,
                Y = _player.Position.Y,
                Z = _player.Position.Z,
                Rotation = _player.Rotation,
                WalkCycle = _player.WalkCycle,
                State = _player.State,
                Username = _username,
                Avatar = _avatar
            };
            _netClient?.SendState(myState);
        }
        
        // Step Physics 
        _dataModel.Workspace.StepPhysics((float)deltaTime);
        
        // Fire Lua Update
        _dataModel.FireUpdate(deltaTime);
        
        // Handle Camera InputPhysics -> DataModel
        SyncPhysicsRecursive(_dataModel.Workspace);
        
        // Update remote players
        foreach(var remote in _remotePlayers.Values)
            remote.Update(deltaTime);
            
        // Update camera with collision
        _camera?.Update(deltaTime, _player!.Position, _inputManager, (origin, dir, maxDist) => 
        {
            var physics = _dataModel.Workspace.Physics;
            if (physics.Raycast(origin, dir, maxDist, out var hit))
            {
                object? hitObj = null;
                if (hit.Collidable.Mobility == BepuPhysics.Collidables.CollidableMobility.Dynamic)
                    physics.BodyToPart.TryGetValue(hit.Collidable.BodyHandle, out hitObj);
                else
                    physics.StaticToPart.TryGetValue(hit.Collidable.StaticHandle, out hitObj);

                if (hitObj is EnginePart p)
                {
                    // Ignore player and non-collidable parts
                    if (p.Name == "Player" || !p.CanCollide) return null;
                    return hit.T;
                }
            }
            return null;
        });
    }
    
    private void SyncPhysicsRecursive(Instance instance)
    {
        if (instance is EnginePart part)
        {
            part.SyncFromPhysics();
        }
        foreach(var child in instance.GetChildren())
        {
            SyncPhysicsRecursive(child);
        }
    }

    public void Render()
    {
        _gl.Enable(EnableCap.DepthTest);
        
        // --- 1. SHADOW PASS ---
        Vector3 lightPos = new Vector3(50, 150, 50); // Same as used in main pass
        _renderer!.BeginShadowPass(lightPos);
        
        // Render 3D Scene to depth map
        RenderInstance(_dataModel.Workspace);
        _player?.Render(_renderer!);
        foreach (var remote in _remotePlayers.Values)
            remote.Render(_renderer!);
            
        _renderer!.EndShadowPass(_window.Size.X, _window.Size.Y);


        // --- 2. MAIN PASS ---
        _gl.ClearColor(0.4f, 0.75f, 1.0f, 1.0f); // Roblox 2009 Bright Sky
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        
        var view = _camera!.GetViewMatrix();
        var projection = _camera.GetProjectionMatrix();
        
        _renderer!.Begin(view, projection, _camera.Position);
        
        // Render Engine Workspace (Parts created by Lua)
        RenderInstance(_dataModel.Workspace);
        
        // Render player (local)
        _player?.Render(_renderer!);
        
        // Render remote players
        foreach(var remote in _remotePlayers.Values)
            remote.Render(_renderer!);
        
        _renderer!.End();

        // --- 2D GUI Pass --- 
        // 1. Render CoreGui (Lua UI)
        RenderGuiRecursive(_dataModel.CoreGui);
        
        // 2. Render ImGui Overlay
        DrawGameUI();
        _imGuiController?.Render();
    }

    private void DrawGameUI()
    {
        // 1. Nametags
        var view = _camera!.GetViewMatrix();
        var proj = _camera!.GetProjectionMatrix();
        var vp = view * proj;
        
        float screenWidth = _window.Size.X;
        float screenHeight = _window.Size.Y;

        foreach (var remote in _remotePlayers.Values)
        {
            // Project world position to screen
            Vector3 headPos = remote.Position + new Vector3(0, 2.5f, 0); // Above head
            Vector4 clipSpace = Vector4.Transform(new Vector4(headPos, 1.0f), vp);
            
            if (clipSpace.W > 0) // Only draw if in front of camera
            {
                Vector3 ndc = new Vector3(clipSpace.X / clipSpace.W, clipSpace.Y / clipSpace.W, clipSpace.Z / clipSpace.W);
                float x = (ndc.X + 1) * 0.5f * screenWidth;
                float y = (1 - ndc.Y) * 0.5f * screenHeight;
                
                // Draw Username
                string label = remote.Username ?? $"Player {remote.Id}";
                Vector2 textSize = ImGui.CalcTextSize(label);
                
                ImGui.SetNextWindowPos(new Vector2(x - textSize.X/2, y));
                ImGui.Begin($"Tag_{remote.Id}", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs);
                ImGui.TextColored(new Vector4(1, 1, 1, 1), label);
                ImGui.End();
            }
        }

        // 2. Scoreboard (Press ';')
        if (_input.Keyboards[0].IsKeyPressed(Key.Semicolon)) 
        {
            ImGui.SetNextWindowPos(new Vector2(screenWidth / 2 - 150, 100));
            ImGui.SetNextWindowSize(new Vector2(300, 0));
            ImGui.Begin("Scoreboard", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize);
            
            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), "PLAYERS");
            ImGui.Separator();
            
            if (ImGui.BeginTable("Scores", 2))
            {
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("Ping");
                ImGui.TableHeadersRow();
                
                // Me
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text($"{_username} (You)");
                ImGui.TableNextColumn(); ImGui.Text("0 ms");
                
                // Others
                foreach (var remote in _remotePlayers.Values)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(remote.Username ?? $"Player {remote.Id}");
                    ImGui.TableNextColumn(); ImGui.Text("23 ms"); 
                }
                
                ImGui.EndTable();
            }
            ImGui.End();
        }
    }
    
    private void SearchSpawns(Instance instance, List<SpawnLocation> spawns)
    {
        if (instance is SpawnLocation sl) spawns.Add(sl);
        foreach (var child in instance.GetChildren())
            SearchSpawns(child, spawns);
    }
    
    private void RenderGuiRecursive(Instance instance)
    {
        if (instance is GuiObject gui && gui.Visible)
        {
            if (gui is Frame frame)
            {
                _renderer!.DrawRect(frame.Position, frame.Size, frame.Color, new Vector2(_window.Size.X, _window.Size.Y));
            }
        }
        
        foreach (var child in instance.GetChildren())
        {
            RenderGuiRecursive(child);
        }
    }
    
    private void RenderInstance(Instance instance)
    {
        if (instance is EnginePart part)
        {
            // Skip invisible parts
            if (part.Transparency >= 1.0f) return; 
            
            // Render Part
            // The original DrawCube call was simpler. Assuming the user wants to keep the original rendering logic
            // but add the transparency check. If the user intended to change the DrawCube signature,
            // that would be a separate instruction.
            _renderer!.DrawCube(part.Position, part.Size, part.Color);
        }
        
        // Render Children
        foreach(var child in instance.GetChildren())
        {
            RenderInstance(child);
        }
    }

    public void OnResize(int width, int height)
    {
        _camera?.UpdateAspect(width, height);
    }

    public void Dispose()
    {
        _player?.Dispose();
        // Baseplate is now part of DataModel, no manual dispose needed here
        _renderer?.Dispose();
        _netClient?.Dispose();
        
        foreach(var remote in _remotePlayers.Values)
            remote.Dispose();
    }
}
