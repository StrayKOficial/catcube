#include "Engine.hpp"
#include "Instance.hpp"
#include "Services.hpp"
#include "Part.hpp"
#include <iostream>
#include <string>

void printHierarchy(CatCube::InstancePtr inst, int depth = 0) {
    std::string indent(depth * 2, ' ');
    std::cout << indent << "- " << inst->getClassName() << " \"" << inst->getName() << "\"" << std::endl;
    for (const auto& child : inst->getChildren()) {
        printHierarchy(child, depth + 1);
    }
}

int main(int argc, char* argv[]) {
    bool isServer = false;
    std::string clientIp = "";
    std::string mapPath = "";
    int port = 53640;
    std::string mapName = "Baseplate";
    
    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg == "--server") {
            isServer = true;
        } else if (arg == "--client" && i + 1 < argc) {
            clientIp = argv[++i];
        } else if (arg == "--map" && i + 1 < argc) {
            mapPath = argv[++i];
        } else if (arg == "--port" && i + 1 < argc) {
            port = std::stoi(argv[++i]);
        } else if (arg == "--mapname" && i + 1 < argc) {
            mapName = argv[++i];
        }
    }

    std::cout << "=== CatCube - Roblox 2009 Clone ===" << std::endl;
    if (isServer) std::cout << "MODE: SERVER" << std::endl;
    else if (!clientIp.empty()) std::cout << "MODE: CLIENT (" << clientIp << ")" << std::endl;
    else std::cout << "MODE: SINGLEPLAYER" << std::endl;

    // Create the DataModel (game root)
    auto game = std::make_shared<CatCube::DataModel>();
    
    // Create services
    auto workspace = std::make_shared<CatCube::Workspace>();
    workspace->setParent(game);
    
    auto players = std::make_shared<CatCube::Players>();
    players->setParent(game);
    
    // Baseplate (large gray platform)
    auto baseplate = std::make_shared<CatCube::Part>();
    baseplate->setName("Baseplate");
    baseplate->setPosition({0, -2, 0});
    baseplate->setSize({200, 4, 200});
    baseplate->setColor(CatCube::Color3::DarkGray());
    baseplate->setAnchored(true);
    baseplate->setParent(workspace);
    
    // SpawnLocation
    auto spawn = std::make_shared<CatCube::SpawnLocation>();
    spawn->setName("SpawnLocation");
    spawn->setPosition({0, 0.5f, 0});
    spawn->setParent(workspace);
    
    // Initialize engine
    CatCube::Engine engine;
    
    std::string title = "CatCube";
    if (isServer) title += " (SERVER)";
    else if (!clientIp.empty()) title += " (CLIENT)";
    
    if (!mapPath.empty()) {
        title = "CatCube - " + mapPath;
    }

    if (!engine.init(title, 1280, 720, isServer)) {
        std::cerr << "Failed to initialize engine!" << std::endl;
        return 1;
    }

    // Register Lua Bindings with Game instance (After engine.init so ScriptService is ready)
    CatCube::LuaBindings::registerBindings(engine.getScriptService().getState(), game);
    
    // Set world early
    engine.setWorld(workspace);

    // Prepare Local Character Identity
    std::string localPlayerName = "Guest_" + std::to_string(rand() % 9999);
    if (isServer) localPlayerName = "Host";
    else if (clientIp.empty()) localPlayerName = "Player";
    else localPlayerName = "LocalPlayer";

    engine.setLocalPlayerName(localPlayerName);

    // Load Map Script if provided (SERVER/SOLO CASE)
    if (!mapPath.empty()) {
        std::cout << "Loading map script: " << mapPath << std::endl;
        engine.getScriptService().runFile(mapPath);
        
        // Spawn immediately since we are the host/local
        engine.spawnCharacter(localPlayerName, {0, 10, 0});
    } else if (clientIp.empty()) {
        // Solo mode without map? Still spawn
        engine.spawnCharacter(localPlayerName, {0, 10, 0});
    }
    // CLIENTS: Wait for onMapReceived callback to call spawnCharacter
    
    // Networking Setup
    if (isServer) {
        engine.getNetworkService().startServer(mapName, port);
    } else if (!clientIp.empty()) {
        engine.getNetworkService().startClient(clientIp, port);
    }

    // Refresh world
    engine.setWorld(workspace);
    
    engine.run();
    
    return 0;
}
