#pragma once

#include <enet/enet.h>
#include <string>
#include <vector>
#include <iostream>
#include <functional>
#include "Part.hpp" // For Vector3

namespace CatCube {

enum class NetworkRole {
    None,
    Server,
    Client
};

struct PlayerData {
    uint32_t peerID;
    std::string name;
    Vector3 position;
    float yaw;
    // Animation state would go here
};

class NetworkService {
public:
    NetworkService();
    ~NetworkService();

    bool init();
    void shutdown();

    // Start as server
    bool startServer(const std::string& mapName, int port = 53640);
    
    // Start as client
    bool startClient(const std::string& address, int port = 53640);

    void setMapName(const std::string& name) { m_mapName = name; }

    void update();

    // Replication
    void sendPosition(const Vector3& pos, float yaw);

    NetworkRole getRole() const { return m_role; }
    bool isConnected() const { return m_host != nullptr; }

    // Callbacks for Engine
    std::function<void(uint32_t)> onPlayerJoined;
    std::function<void(uint32_t)> onPlayerLeft;
    std::function<void(uint32_t, Vector3, float)> onPositionReceived;
    std::function<void(const std::string&)> onMapReceived;

private:
    void handleEvents();
    void processPacket(ENetPeer* peer, ENetPacket* packet);

    ENetHost* m_host = nullptr;
    ENetPeer* m_peer = nullptr; // Server peer if client, or null if server
    NetworkRole m_role = NetworkRole::None;
    std::string m_mapName = "Unknown";
};

} // namespace CatCube
