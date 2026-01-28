#include "Engine.hpp"
#include <iostream>
#include <cmath>

namespace CatCube {

Engine::Engine() {
}

Engine::~Engine() {
    shutdown();
}

bool Engine::init(const std::string& title, int width, int height, bool headless) {
    m_width = width;
    m_height = height;
    m_headless = headless;

    if (m_headless) {
        std::cout << "Engine: Running in HEADLESS mode (Server)." << std::endl;
    }

    // Initialize SDL
    uint32_t sdlFlags = SDL_INIT_TIMER | SDL_INIT_EVENTS;
    if (!m_headless) sdlFlags |= SDL_INIT_VIDEO;

    if (SDL_Init(sdlFlags) != 0) {
        std::cerr << "SDL_Init failed: " << SDL_GetError() << std::endl;
        return false;
    }

    if (!m_headless) {
        // Set OpenGL attributes
        SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 2);
        SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 1);
        SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
        SDL_GL_SetAttribute(SDL_GL_DEPTH_SIZE, 24);

        // Create window
        m_window = SDL_CreateWindow(
            title.c_str(),
            SDL_WINDOWPOS_CENTERED,
            SDL_WINDOWPOS_CENTERED,
            m_width,
            m_height,
            SDL_WINDOW_SHOWN | SDL_WINDOW_RESIZABLE | SDL_WINDOW_OPENGL
        );

        if (!m_window) {
            std::cerr << "SDL_CreateWindow failed: " << SDL_GetError() << std::endl;
            return false;
        }

        // Create OpenGL context
        m_glContext = SDL_GL_CreateContext(m_window);
        if (!m_glContext) {
            std::cerr << "SDL_GL_CreateContext failed: " << SDL_GetError() << std::endl;
            return false;
        }

        // Enable VSync
        SDL_GL_SetSwapInterval(1);

        // Initialize renderer
        m_renderer.init(m_width, m_height);
    }
    
    // Initialize physics (Needs to run always)
    m_physics.init();
    
    // Setup camera
    m_camera.target = {0, 0, 0};
    m_camera.distance = 30.0f;
    m_camera.yaw = 45.0f;
    m_camera.pitch = -25.0f;
    m_camera.updateDirection();
    m_renderer.setCamera(m_camera);

    // Initialize Scripting
    m_scriptService.init();

    // Initialize Networking
    m_networkService.init();

    m_networkService.onPlayerJoined = [this](uint32_t id) {
        std::cout << "Engine: Remote player " << id << " joined." << std::endl;
        auto remoteChar = CharacterHelper::createCharacter("RemotePlayer_" + std::to_string(id), {0, 20, 0});
        if (m_world) remoteChar->setParent(m_world);
        m_remotePlayers[id] = remoteChar;
    };

    m_networkService.onPlayerLeft = [this](uint32_t id) {
        std::cout << "Engine: Remote player " << id << " left." << std::endl;
        if (m_remotePlayers.count(id)) {
            m_remotePlayers[id]->setParent(nullptr);
            m_remotePlayers.erase(id);
        }
    };

    m_networkService.onMapReceived = [this](const std::string& name) {
        std::cout << "Engine: Server is running map: " << name << ". Loading script..." << std::endl;
        std::string scriptPath = "../maps/" + name + ".lua";
        if (!m_scriptService.runFile(scriptPath)) {
             m_scriptService.runFile(name); 
        }
        
        // After map is loaded, spawn our local character if we haven't yet
        if (!m_character && !m_localPlayerName.empty()) {
            this->spawnCharacter(m_localPlayerName, {0, 10, 0});
        }

        // Force physics refresh
        this->setWorld(m_world);
    };

    m_networkService.onPositionReceived = [this](uint32_t id, Vector3 pos, float yaw) {
        (void)yaw;
        
        // If we don't know this player yet, create them (this includes the host from client perspective)
        if (m_remotePlayers.find(id) == m_remotePlayers.end()) {
            std::cout << "Engine: Synchronizing remote player " << id << std::endl;
            auto remoteChar = CharacterHelper::createCharacter("Remote_" + std::to_string(id), {0, 0, 0});
            if (m_world) remoteChar->setParent(m_world);
            m_remotePlayers[id] = remoteChar;
            // Immediate registration
            registerPhysicsRecursively(remoteChar);
        }

        auto root = std::dynamic_pointer_cast<BasePart>(m_remotePlayers[id]->getPrimaryPart());
        if (root) {
            root->setPosition(pos);
            // Visual rotation and animation update
            CharacterHelper::updateCharacterPhysics(m_remotePlayers[id], {0,0,0}, false, m_physics, 0.016f);
        }
    };
    
    // Capture mouse (lock to window center)
    SDL_SetRelativeMouseMode(SDL_TRUE);
    m_mouseCaptured = true;

    m_running = true;
    m_lastTime = SDL_GetPerformanceCounter();
    
    std::cout << "==================================" << std::endl;
    std::cout << "  CatCube Engine v0.1.0" << std::endl;
    std::cout << "  Roblox 2009 Clone" << std::endl;
    std::cout << "==================================" << std::endl;
    std::cout << "OpenGL Version: " << glGetString(GL_VERSION) << std::endl;
    std::cout << "Renderer: " << glGetString(GL_RENDERER) << std::endl;
    std::cout << std::endl;
    std::cout << "Controls:" << std::endl;
    std::cout << "  WASD: Move" << std::endl;
    std::cout << "  Space/E: Up | Q/Ctrl: Down" << std::endl;
    std::cout << "  Shift: Run" << std::endl;
    std::cout << "  Mouse: Look around" << std::endl;
    std::cout << "  Scroll: Zoom" << std::endl;
    std::cout << "  Tab: Toggle mouse lock" << std::endl;
    std::cout << "  ESC: Quit" << std::endl;
    std::cout << std::endl;
    
    if (!m_headless) {
        // Start with mouse unlocked (Roblox style)
        SDL_SetRelativeMouseMode(SDL_FALSE);
        m_mouseCaptured = false;
    }

void Engine::spawnCharacter(const std::string& name, Vector3 pos) {
    if (m_character) return; // Already spawned
    
    std::cout << "Engine: Spawning Local Character (" << name << ")..." << std::endl;
    m_character = CharacterHelper::createCharacter(name, pos);
    if (m_world) m_character->setParent(m_world);
    
    // Register to physics
    registerPhysicsRecursively(m_character);
}

void Engine::setWorld(InstancePtr world) {
    m_world = world;
    // Register all initial parts to physics
    registerPhysicsRecursively(m_world);
}

void Engine::registerPhysicsRecursively(InstancePtr instance) {
    auto basePart = std::dynamic_pointer_cast<BasePart>(instance);
    if (basePart) {
        m_physics.addPart(basePart);
    }
    
    for (const auto& child : instance->getChildren()) {
        registerPhysicsRecursively(child);
    }
}

void Engine::run() {
    while (m_running) {
        // Calculate delta time
        uint64_t currentTime = SDL_GetPerformanceCounter();
        m_deltaTime = (float)(currentTime - m_lastTime) / SDL_GetPerformanceFrequency();
        m_lastTime = currentTime;
        m_fps = 1.0f / m_deltaTime;
        
        // Cap delta time to avoid spiral of death (max 0.1s)
        if (m_deltaTime > 0.1f) m_deltaTime = 0.1f;

        processInput();
        update(m_deltaTime);
        render();
    }
    std::cout << "Engine: Loop ended. m_running = " << m_running << std::endl;
}

void Engine::processInput() {
    if (m_headless) return;
    
    m_mouseDeltaX = 0;
    m_mouseDeltaY = 0;
    
    SDL_Event event;
    while (SDL_PollEvent(&event)) {
        switch (event.type) {
            case SDL_QUIT:
                m_running = false;
                break;
            
            case SDL_WINDOWEVENT:
                if (event.window.event == SDL_WINDOWEVENT_RESIZED) {
                    m_width = event.window.data1;
                    m_height = event.window.data2;
                    m_renderer.resize(m_width, m_height);
                }
                else if (event.window.event == SDL_WINDOWEVENT_FOCUS_GAINED) {
                    if (m_mouseCaptured) {
                        SDL_SetRelativeMouseMode(SDL_TRUE);
                    }
                }
                break;
            
            case SDL_KEYDOWN:
                if (event.key.keysym.scancode < 512) {
                    m_keys[event.key.keysym.scancode] = true;
                }
                if (event.key.keysym.sym == SDLK_ESCAPE) {
                    m_running = false;
                }
                // Tab to toggle mouse capture
                if (event.key.keysym.sym == SDLK_ESCAPE) {
                    m_running = false;
                }
                break;
            
            case SDL_KEYUP:
                if (event.key.keysym.scancode < 512) {
                    m_keys[event.key.keysym.scancode] = false;
                }
                break;
            
            case SDL_MOUSEMOTION:
                m_mouseDeltaX = event.motion.xrel;
                m_mouseDeltaY = event.motion.yrel;
                m_mouseX = event.motion.x;
                m_mouseY = event.motion.y;
                break;
            
            case SDL_MOUSEBUTTONDOWN:
                if (event.button.button <= 5) {
                    m_mouseButtons[event.button.button - 1] = true;
                }
                // Right click to capture mouse (Roblox style)
                if (event.button.button == SDL_BUTTON_RIGHT) {
                    m_mouseCaptured = true;
                    SDL_SetRelativeMouseMode(SDL_TRUE);
                }
                break;
            
            case SDL_MOUSEBUTTONUP:
                if (event.button.button <= 5) {
                    m_mouseButtons[event.button.button - 1] = false;
                }
                // Release right click to free mouse
                if (event.button.button == SDL_BUTTON_RIGHT) {
                    m_mouseCaptured = false;
                    SDL_SetRelativeMouseMode(SDL_FALSE);
                }
                break;
            
            case SDL_MOUSEWHEEL:
                // Zoom with scroll
                m_camera.distance -= event.wheel.y * 2.0f;
                if (m_camera.distance < 5.0f) m_camera.distance = 5.0f;
                if (m_camera.distance > 100.0f) m_camera.distance = 100.0f;
                m_camera.updateDirection();
                m_renderer.setCamera(m_camera);
                break;
        }
    }
    
    // Mouse look - always active when captured (like Roblox gameplay)
    if (m_mouseCaptured && (m_mouseDeltaX != 0 || m_mouseDeltaY != 0)) {
        // Yaw inverted: Moving mouse LEFT (negative X) should rotate camera LEFT (decrease Yaw or increase depending on coord sys).
        // If left/right is inverted, flip the sign.
        m_camera.yaw -= m_mouseDeltaX * 0.15f; // Inverted from += 
        m_camera.pitch -= m_mouseDeltaY * 0.15f;
        
        // Clamp pitch
        if (m_camera.pitch > 89.0f) m_camera.pitch = 89.0f;
        if (m_camera.pitch < -89.0f) m_camera.pitch = -89.0f;
        
        m_camera.updateDirection();
        m_renderer.setCamera(m_camera);
    }
}

void Engine::update(float deltaTime) {
    // Update physics
    m_physics.update(deltaTime);
    
    // Update networking
    m_networkService.update();

    // Enforce mouse lock if captured (check state first to avoid lag)
    if (m_mouseCaptured && SDL_GetRelativeMouseMode() == SDL_FALSE) {
        SDL_SetRelativeMouseMode(SDL_TRUE);
    }

    // Get input state
    bool jump = (m_keys[SDL_SCANCODE_SPACE]);
    Vector3 moveDir{0, 0, 0};
    
    // Calculate camera-relative movement
    // Camera Position is Target + Offset(yaw).
    // Look Direction is Target - Position = -Offset.
    // Offset horizontal is (sin(yaw), cos(yaw)).
    // So Look Direction horizontal is (-sin(yaw), -cos(yaw)).
    
    float yawRad = m_camera.yaw * 3.14159f / 180.0f;
    Vector3 forward = {-std::sin(yawRad), 0, -std::cos(yawRad)}; // Fix: -sin for X
    Vector3 right = {std::cos(yawRad), 0, -std::sin(yawRad)};    // Right is Forward rotated -90? 
    // Forward (-sin, -cos). 
    // Right (X, Z). Dot(-sin, -cos, X, Z) = 0. -sinX - cosZ = 0.
    // Try (cos, -sin). -sin(cos) - cos(-sin) = -sincos + sincos = 0.
    // Right = (cos, 0, -sin).
    
    if (m_keys[SDL_SCANCODE_W]) moveDir = moveDir + forward;
    if (m_keys[SDL_SCANCODE_S]) moveDir = moveDir - forward;
    if (m_keys[SDL_SCANCODE_A]) moveDir = moveDir - right;
    if (m_keys[SDL_SCANCODE_D]) moveDir = moveDir + right;
    
    // Normalize move constant speed
    if (moveDir.length() > 0.1f) {
        moveDir = moveDir.normalized();
    }
    
    if (m_character) {
        // --- CONTROL CHARACTER ---
        // Apply physics movement
        CharacterHelper::updateCharacterPhysics(m_character, moveDir, jump, m_physics, deltaTime);
        
        // Broadcast position at 30Hz
        m_networkTimer += deltaTime;
        if (m_networkTimer >= 0.033f) {
            m_networkTimer = 0;
            auto root = std::dynamic_pointer_cast<BasePart>(m_character->getPrimaryPart());
            if (root) {
                m_networkService.sendPosition(root->getPosition(), 0);
            }
        }

        // Update Humanoid state
        auto humanoid = std::dynamic_pointer_cast<Humanoid>(m_character->findFirstChild("Humanoid"));
        if (humanoid) {
            humanoid->move(moveDir, jump);
        }
        
        // Camera Follow Logic (Third Person)
        auto head = m_character->findFirstChild("Head");
        if (head && head->isA("BasePart")) {
            auto headPart = std::dynamic_pointer_cast<BasePart>(head);
            Vector3 targetPos = headPart->getPosition();
            
            // Smoothly interpolate camera target? For now, snap to head.
            m_camera.target = targetPos;
            
            // TPS Logic: Position is derived from Target + Angles
            float pitchRad = m_camera.pitch * 3.14159f / 180.0f;
            float yawRad = m_camera.yaw * 3.14159f / 180.0f;
            
            // Calculate offset based on yaw/pitch
            float hDist = m_camera.distance * std::cos(pitchRad);
            float vDist = m_camera.distance * std::sin(pitchRad);
            
            // Calculate position relative to target
            // yaw 0 = South (+Z). We stand at +Z looking at -Z?
            // If yaw rotates map, camera rotates around it.
            m_camera.position.x = m_camera.target.x + std::sin(yawRad) * hDist;
            m_camera.position.z = m_camera.target.z + std::cos(yawRad) * hDist;
            m_camera.position.y = m_camera.target.y - vDist; // Pitch negative = Camera High
            
            // DO NOT call updateDirection here, as it resets target from position
            // But we do need to set up the View Matrix in Renderer, which uses LookAt(pos, target).
            // Renderer uses m_camera directly. LookAt uses pos and target. Both are set now.
        }
    } 
    else {
        // --- FREECAM (Old Logic) ---
        float speed = 15.0f;
        if (m_keys[SDL_SCANCODE_LSHIFT]) speed = 40.0f;
        
        // Move camera position directly
        if (moveDir.length() > 0) {
            m_camera.position = m_camera.position + moveDir * speed * deltaTime;
        }
        
        // Vertical movement
        if (m_keys[SDL_SCANCODE_E] || m_keys[SDL_SCANCODE_SPACE]) m_camera.position.y += speed * deltaTime;
        if (m_keys[SDL_SCANCODE_Q] || m_keys[SDL_SCANCODE_LCTRL]) m_camera.position.y -= speed * deltaTime;
        
        m_camera.updateDirection();
    }

    m_renderer.setCamera(m_camera);
}

void Engine::render() {
    if (m_headless) return;
    
    m_renderer.beginFrame();
    
    // Render world if set
    if (m_world) {
        m_renderer.renderHierarchy(m_world);
    }
    
    m_renderer.endFrame();
    
    // Swap buffers
    SDL_GL_SwapWindow(m_window);
}

void Engine::shutdown() {
    if (m_running || m_window) {
        m_running = false;
        
        if (m_glContext) {
            SDL_GL_DeleteContext(m_glContext);
            m_glContext = nullptr;
        }
        
        if (m_window) {
            SDL_DestroyWindow(m_window);
            m_window = nullptr;
        }
        
        SDL_Quit();
        
        std::cout << "CatCube Engine shutdown complete." << std::endl;
    }
}

} // namespace CatCube
