#include "NetworkService.hpp"

namespace CatCube {

NetworkService::NetworkService() {
}

NetworkService::~NetworkService() {
    shutdown();
}

bool NetworkService::init() {
    if (enet_initialize() != 0) {
        std::cerr << "An error occurred while initializing ENet." << std::endl;
        return false;
    }
    return true;
}

void NetworkService::shutdown() {
    if (m_host) {
        enet_host_destroy(m_host);
        m_host = nullptr;
    }
    enet_deinitialize();
}

bool NetworkService::startServer(const std::string& mapName, int port) {
    m_mapName = mapName;
    ENetAddress address;
    address.host = ENET_HOST_ANY;
    address.port = port;

    m_host = enet_host_create(&address, 32, 2, 0, 0);
    if (m_host == nullptr) {
        std::cerr << "CRITICAL: Failed to create ENet server host on port " << port << ". Check if port is bound or if ENet init failed." << std::endl;
        return false;
    }

    m_role = NetworkRole::Server;
    std::cout << "Server started on port " << port << std::endl;
    return true;
}

bool NetworkService::startClient(const std::string& addressStr, int port) {
    m_host = enet_host_create(nullptr, 1, 2, 0, 0);
    if (m_host == nullptr) {
        std::cerr << "An error occurred while trying to create an ENet client host." << std::endl;
        return false;
    }

    ENetAddress address;
    enet_address_set_host(&address, addressStr.c_str());
    address.port = port;

    m_peer = enet_host_connect(m_host, &address, 2, 0);
    if (m_peer == nullptr) {
        std::cerr << "No available peers for initiating an ENet connection." << std::endl;
        return false;
    }

    m_role = NetworkRole::Client;
    std::cout << "Connecting to " << addressStr << ":" << port << "..." << std::endl;
    return true;
}

void NetworkService::update() {
    if (!m_host) return;
    handleEvents();
}

enum PacketType : uint8_t {
    PACKET_POS = 1
};

void NetworkService::sendPosition(const Vector3& pos, float yaw) {
    if (!m_host) return;

    struct {
        uint8_t type;
        float x, y, z, yaw;
    } p;
    p.type = PACKET_POS;
    p.x = pos.x; p.y = pos.y; p.z = pos.z;
    p.yaw = yaw;

    ENetPacket* packet = enet_packet_create(&p, sizeof(p), m_role == NetworkRole::Client ? ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT : 0);
    
    if (m_role == NetworkRole::Client && m_peer) {
        enet_peer_send(m_peer, 0, packet);
    } else if (m_role == NetworkRole::Server) {
        // As a server, we are ID 0. Clients expect a relay-style packet for the host too?
        // Let's use a consistent header for all broadcasts: [Type, ID, X, Y, Z, YAW]
        struct relay_pkt { uint8_t t; uint32_t id; float x, y, z, yaw; } rp;
        rp.t = PACKET_POS;
        rp.id = 0; // Server is ID 0
        rp.x = pos.x; rp.y = pos.y; rp.z = pos.z; rp.yaw = yaw;
        
        ENetPacket* broadcastPkt = enet_packet_create(&rp, sizeof(rp), ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT);
        enet_host_broadcast(m_host, 0, broadcastPkt);
    }
}

void NetworkService::handleEvents() {
    ENetEvent event;
    while (enet_host_service(m_host, &event, 0) > 0) {
        switch (event.type) {
            case ENET_EVENT_TYPE_CONNECT:
                std::cout << "Connected: " << event.peer->address.host << ":" << event.peer->address.port << std::endl;
                if (onPlayerJoined) onPlayerJoined((uint32_t)event.peer->incomingPeerID);
                
                // If server, send Map Metadata to the new client immediately
                if (m_role == NetworkRole::Server) {
                    struct { uint8_t t; char name[64]; } mp;
                    mp.t = 254; // PACKET_METADATA
                    strncpy(mp.name, m_mapName.c_str(), 63);
                    ENetPacket* p = enet_packet_create(&mp, sizeof(mp), ENET_PACKET_FLAG_RELIABLE);
                    enet_peer_send(event.peer, 0, p);
                }
                break;

            case ENET_EVENT_TYPE_RECEIVE:
                processPacket(event.peer, event.packet);
                enet_packet_destroy(event.packet);
                break;

            case ENET_EVENT_TYPE_DISCONNECT:
                std::cout << "Disconnected." << std::endl;
                if (onPlayerLeft) onPlayerLeft((uint32_t)event.peer->incomingPeerID);
                break;
                
            default:
                break;
        }
    }
}

void NetworkService::processPacket(ENetPeer* peer, ENetPacket* packet) {
    if (packet->dataLength < sizeof(uint8_t)) return;
    
    uint8_t type = packet->data[0];
    uint32_t id = (uint32_t)peer->incomingPeerID;

    if (type == 254) { // PACKET_METADATA
        struct pkt { uint8_t t; char name[64]; } *p = (pkt*)packet->data;
        if (onMapReceived) onMapReceived(std::string(p->name));
    }
    else if (type == PACKET_POS) {
        // Simple POS (from client to server): [uint8 t, float x,y,z,yaw]
        // Relayed POS (from server to client): [uint8 t, uint32 id, float x,y,z,yaw]
        
        uint32_t senderId = id;
        void* dataPtr = packet->data + 1;
        
        if (packet->dataLength >= sizeof(uint8_t) + sizeof(uint32_t) + (4*sizeof(float))) {
            // Relayed data
            struct rpkt { uint32_t id; float x,y,z,yaw; } *p = (rpkt*)(packet->data + 1);
            senderId = p->id;
            dataPtr = (void*)(&p->x);
        }

        struct pos_data { float x,y,z,yaw; } *p = (pos_data*)dataPtr;
        if (onPositionReceived) {
            onPositionReceived(senderId, {p->x, p->y, p->z}, p->yaw);
        }

        // Server relays position TO ALL OTHER clients
        if (m_role == NetworkRole::Server) {
            struct relay_pkt { uint8_t t; uint32_t id; float x, y, z, yaw; } rp;
            rp.t = PACKET_POS;
            rp.id = id;
            rp.x = p->x; rp.y = p->y; rp.z = p->z; rp.yaw = p->yaw;
            
            ENetPacket* rpacket = enet_packet_create(&rp, sizeof(rp), ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT);
            for (size_t i = 0; i < m_host->peerCount; ++i) {
                if (&m_host->peers[i] != peer && m_host->peers[i].state == ENET_PEER_STATE_CONNECTED) {
                    enet_peer_send(&m_host->peers[i], 0, rpacket);
                }
            }
        }
    }
}

} // namespace CatCube
