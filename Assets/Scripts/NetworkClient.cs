using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections.Generic;

[System.Serializable]
public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;
    public string localID;
    public GameObject playerType;

    private List<GameObject> players = new List<GameObject>();

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);
        m_Connection = m_Driver.Connect(endpoint);
    }

    System.Collections.IEnumerator UpdateLocalPlayer()
    {
        while(this.isActiveAndEnabled)
        {
            int found = FindPlayer(localID);

            if (found != -1)
            {
                //Update the position of local player on the server
                var message = new PlayerUpdateMsg();
                message.player.id = localID;
                message.player.cubePos = players[found].transform.position;
                message.player.cubeColor = players[found].GetComponent<Renderer>().material.GetColor("_Color");
                SendToServer(JsonUtility.ToJson(message));
            }
            else
            {
                Debug.Log("Error: No Local Player");
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect(){
        Debug.Log("We are now connected to the server");

        StartCoroutine(UpdateLocalPlayer());
        //// Example to send a handshake message:
        HandshakeMsg m = new HandshakeMsg();
        m.player.id = m_Connection.InternalId.ToString();
        SendToServer(JsonUtility.ToJson(m));
    }

    void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.INITIALIZE:
                InitialMsg initMsg = JsonUtility.FromJson<InitialMsg>(recMsg);
                localID = initMsg.serverID;
                break;
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!: " + hsMsg.player.id);
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                UpdateList(suMsg.players);
                break;
            case Commands.PLAYER_DISCONNECT:
                DisconnectMsg disMsg = JsonUtility.FromJson<DisconnectMsg>(recMsg);
                Debug.Log("Disconnection message received!: " + disMsg.serverID);
                RemovePlayer(disMsg.serverID);
                break;
            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }

    private void UpdateList(List<NetworkObjects.NetworkPlayer> _players)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            int found = FindPlayer(_players[i].id);
            //New Player
            if (found == -1)
                CreatePlayer(_players[i]);
            //Existing Player
            else
            {
                players[found].transform.position = _players[i].cubePos;
                players[found].GetComponent<Renderer>().material.SetColor("_Color", _players[i].cubeColor);
            }
        }
    }

    //return index of player or -1 if not found
    private int FindPlayer(string id)
    {
        int found = -1;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].GetComponent<PlayerController>().id == id)
            {
                found = i;
            }
        }

        return found;
    }

    private void CreatePlayer(NetworkObjects.NetworkPlayer netPlayer)
    {
        GameObject player = Instantiate(playerType);
        //Set ID and client controller
        player.GetComponent<PlayerController>().id = netPlayer.id;
        player.GetComponent<PlayerController>().network = this;
        player.GetComponent<Renderer>().material.SetColor("_Color", netPlayer.cubeColor);
        player.transform.position = netPlayer.cubePos;
        players.Add(player);
        Debug.Log("Player Created: ID " + netPlayer.id);
    }

    private void RemovePlayer(string serverID)
    {
        int found = FindPlayer(serverID);
        if (found != -1)
        {
            players.RemoveAt(found);
            Debug.Log("Player Successfully Removed");
        }
        else
            Debug.Log("Player did not exist");
    }

    void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }   
    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }
}