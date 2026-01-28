using LiteNetLib;
using LiteNetLib.Utils;
using CatCube.Shared;
using System.Net;

namespace CatCube.Server;

class Program
{
    static void Main(string[] args)
    {
        string mapName = "Default";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--map" && i + 1 < args.Length)
            {
                mapName = args[i + 1];
            }
        }

        Console.WriteLine($"=== CatCube Dedicated Server [{mapName}] ===");
        
        EventBasedNetListener listener = new EventBasedNetListener();
        NetManager server = new NetManager(listener);
        
        Dictionary<int, PlayerState> players = new Dictionary<int, PlayerState>();
        NetDataWriter writer = new NetDataWriter();
        
        // Temporary storage - tuple of username + avatar
        Dictionary<IPEndPoint, (string Username, AvatarData Avatar)> connectingUsers = new Dictionary<IPEndPoint, (string, AvatarData)>();

        listener.ConnectionRequestEvent += request =>
        {
            if (server.ConnectedPeersCount < 50)
            {
                string username = "Unknown";
                AvatarData avatar = new AvatarData();
                try 
                {
                    // request.Data is NetDataReader
                    username = request.Data.GetString();
                    avatar.Deserialize(request.Data); // Read avatar after username
                }
                catch {}
                
                connectingUsers[request.RemoteEndPoint] = (username, avatar);
                request.AcceptIfKey(NetworkConfig.Key);
            }
            else
                request.Reject();
        };

        listener.PeerConnectedEvent += peer =>
        {
            int playerId = peer.EndPoint.Port;
            string username_final = "Connecting...";
            AvatarData avatar = new AvatarData { ShirtColor="#CC3333", PantsColor="#264073", SkinColor="#FFD9B8" };
            
            if (connectingUsers.TryGetValue(peer.EndPoint, out var data))
            {
                username_final = data.Username;
                avatar = data.Avatar;
                connectingUsers.Remove(peer.EndPoint); // Clean up
            }
            
            Console.WriteLine($"[Network] Player connected: {peer.EndPoint} (Username: {username_final})");
            
            PlayerState newState = new PlayerState { Id = playerId, Username = username_final, Avatar = avatar };
            players[playerId] = newState;

            // Broadcast new player to EVERYONE ELSE
            writer.Reset();
            writer.Put((byte)PacketType.PlayerState);
            newState.Serialize(writer);
            server.SendToAll(writer, DeliveryMethod.ReliableOrdered, peer);
            
            Console.WriteLine($"[Network] Total players online: {players.Count}");
            
            // Send current world state to new player
            writer.Reset();
            writer.Put((byte)PacketType.WorldState);
            writer.Put(players.Count);
            foreach(var p in players.Values)
            {
                p.Serialize(writer);
            }
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        };
        
        listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
        {
            Console.WriteLine($"Player disconnected: {peer.EndPoint}");
            int playerId = peer.EndPoint.Port;
            
            if (players.Remove(playerId))
            {
                // Notify others to remove player
                writer.Reset();
                writer.Put((byte)PacketType.PlayerLeft);
                writer.Put(playerId);
                server.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
        };
        
        // Corrected signature for newer LiteNetLib
        listener.NetworkReceiveEvent += (peer, reader, channel, deliveryMethod) =>
        {
            PacketType type = (PacketType)reader.GetByte();
            
            if (type == PacketType.PlayerState)
            {
                PlayerState state = new PlayerState();
                state.Deserialize(reader);
                state.Id = peer.EndPoint.Port; // Force ID to be correct
                
                players[state.Id] = state;
                
                // Broadcast to all OTHER players
                writer.Reset();
                writer.Put((byte)PacketType.PlayerState);
                state.Serialize(writer);
                
                server.SendToAll(writer, DeliveryMethod.Unreliable, peer); // Don't send back to sender
            }
        };

        // --- Start Server ---
        
        server.Start(NetworkConfig.Port);
        Console.WriteLine($"Server started on port {NetworkConfig.Port} hosting map {mapName}");
        
        // --- Game Loop (60 TPS) ---
        
        while (true)
        {
            server.PollEvents();
            Thread.Sleep(15); // ~60 FPS
        }
    }
}
