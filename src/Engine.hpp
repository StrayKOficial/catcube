#pragma once

#include <GL/glew.h>
#include <SDL2/SDL.h>
#include <SDL2/SDL_opengl.h>
#include <memory>
#include <vector>
#include <string>
#include "Instance.hpp"
#include "Renderer.hpp"
#include "PhysicsService.hpp"
#include "CharacterHelper.hpp"
#include "Model.hpp"
#include "ScriptService.hpp"
#include "LuaBindings.hpp"
#include "NetworkService.hpp"
#include <map>

namespace CatCube {
    using ModelPtr = std::shared_ptr<Model>;

class Engine {
public:
    Engine();
    ~Engine();

    bool init(const std::string& title, int width, int height, bool headless = false);
    void run();
    void shutdown();

    void setWorld(InstancePtr world);
    void setCharacter(ModelPtr character) { m_character = character; }
    void spawnCharacter(const std::string& name, Vector3 pos);
    void setLocalPlayerName(const std::string& name) { m_localPlayerName = name; }

    // Services
    ScriptService& getScriptService() { return m_scriptService; }
    NetworkService& getNetworkService() { return m_networkService; }
    PhysicsService& getPhysicsService() { return m_physics; }

private:
    void processInput();
    void update(float deltaTime);
    void render();
    void setupProjection();
    void registerPhysicsRecursively(InstancePtr instance);

    // Window and Rendering
    SDL_Window* m_window = nullptr;
    SDL_GLContext m_glContext = nullptr;
    int m_width, m_height;
    
    Renderer m_renderer;
    Camera m_camera;
    
    // Physics
    PhysicsService m_physics;
    
    // World
    InstancePtr m_world;
    bool m_running = false;
    bool m_headless = false;
    
    // Timer
    uint64_t m_lastTime;
    float m_deltaTime;
    float m_fps;
    float m_networkTimer = 0.0f;
    
    // Input
    bool m_keys[512] = {false};
    bool m_mouseButtons[5] = {false};
    int m_mouseX, m_mouseY;
    int m_mouseDeltaX, m_mouseDeltaY;
    bool m_mouseCaptured = true;
    
    // Player
    ModelPtr m_character; // Local player character
    std::string m_localPlayerName;
    
    // Scripting
    ScriptService m_scriptService;
    
    // Networking
    NetworkService m_networkService;
    std::map<uint32_t, ModelPtr> m_remotePlayers;
};

} // namespace CatCube
