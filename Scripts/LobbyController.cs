using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
public class LobbyController : MonoBehaviour
{
    public static LobbyController Instance { get; private set; }


    Lobby hostLobby;
    Lobby joinedLobby;
    float heartBeatTimer;
    float pollTimer;
    string playerName = "";
    bool alreadyStartedGame;

    public const string KEY_PLAYER_NAME = "PlayerName";
    public const string KEY_START_GAME = "StartGame";
    public const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";

    public static bool IsHost { get; private set; }
    public static string RelayJoinCode { get; private set; }
    public static bool IsGameStarted { get; private set; }


    #region EVENTS

    public event EventHandler<LobbyEventArgs> OnJoinedLobby;
    public event EventHandler<LobbyEventArgs> OnJoinedLobbyUpdate;
    //public event EventHandler<LobbyEventArgs> OnKickedFromLobby;
    //public event EventHandler<LobbyEventArgs> OnLobbyGameModeChanged;
    public event EventHandler OnLobbyStartGame;

    public class LobbyEventArgs : EventArgs
    {
        public Lobby lobby;
    }
    #endregion

    private void Awake()
    {
        if (Instance == null) { Instance = this; } else { Destroy(this); }
        IsGameStarted = false;
    }

    private void Start()
    {
        Authenticate();
    }
    private void Update()
    {
        if (joinedLobby == null) return;
        HeartBeatHandler();
        LobbyPollHandler();
        //LobbyStartGameHandler();
    }

    #region VALIDACION DEL JUGADOR / PRIMER PASO
    string SetTemporalName()
    {
        return "Jugador" + UnityEngine.Random.Range(1, 100);
    }
    public async void Authenticate(string newPlayerName = "")
    {
        if (newPlayerName == "")
        {
            playerName = SetTemporalName();
        }
        else
        {
            playerName = newPlayerName;
        }

        InitializationOptions initializationOptions = new InitializationOptions();
        initializationOptions.SetProfile(playerName);

        await UnityServices.InitializeAsync(initializationOptions);

        AuthenticationService.Instance.SignedIn += () =>
        {
            // do nothing
            Debug.Log("Signed in! " + AuthenticationService.Instance.PlayerId);
            Debug.Log(playerName);
            RefreshLobbyList();
        };

        AuthenticationService.Instance.SwitchProfile(playerName);
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
    #endregion

    #region TICK DE LOBBIES Y POLL UPDATES
    private async void HeartBeatHandler()
    {
        if (hostLobby != null)
        {
            heartBeatTimer -= Time.deltaTime;
            if (heartBeatTimer < 0)
            {
                float heartBeatMax = 20;
                heartBeatTimer = heartBeatMax;

                await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
            }
        }
    }
    async void LobbyPollHandler()
    {
        if (joinedLobby != null)
        {

            pollTimer -= Time.deltaTime;
            if (pollTimer < 0)
            {
                float pollMax = 1.5f;
                pollTimer = pollMax;
                Lobby lob = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                joinedLobby = lob;
                OnJoinedLobbyUpdate?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });

                if (alreadyStartedGame) return;

                if (!IsLobbyHost())
                {
                    if (joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value != "")
                    {
                        JoinGameInLobby(joinedLobby.Data[KEY_RELAY_JOIN_CODE].Value);
                    }
                }

                if (IsLobbyHost())
                {
                    if (joinedLobby.Players.Count == 2)
                    {
                        StartGameInLobby();
                    }
                }
            }
        }
    }

    #endregion

    #region CREACION Y MANEJO DE LOBBIES

    public async void CreateLobby(string lobbyName = "TestLobby", bool isPrivate = true)
    {
        try
        {
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = GetPlayer(),
                //ESTO ES PARA DEFINIR GAMEMODES O CUALQUIER DATO ADICIONAL DE LOBBIES
                //Data = new Dictionary<string, DataObject>
                //{
                //    {"GameMode", new DataObject(DataObject.VisibilityOptions.Public, "Rapido") }
                //}
                Data = new Dictionary<string, DataObject>
                {
                    {KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, "") }
                }
            };

            Lobby lob = await LobbyService.Instance.CreateLobbyAsync(lobbyName, 2, options);
            if (lob != null)
            {
                hostLobby = lob;
                joinedLobby = lob;
            }
            Debug.Log("Lobby created: " + lob.Id + " " + lob.Name + " " + lob.LobbyCode);
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
            //PrintPlayers(lob);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    async void MigrateLobbyHost(int playerIndex)
    {
        try
        {
            hostLobby = await LobbyService.Instance.UpdateLobbyAsync(hostLobby.Id, new UpdateLobbyOptions
            {
                HostId = hostLobby.Players[playerIndex].Id,
            });
            joinedLobby = hostLobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    async void DeleteLobby()
    {
        try
        {
            await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    #endregion

    #region ENTRAR A LOBBIES
    //a el primer lobby publico que encuentre
    async void QuickJoinLobby()
    {
        try
        {
            await LobbyService.Instance.QuickJoinLobbyAsync();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    async void JoinLobby(int lobbyIndex)
    {
        try
        {
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync();

            await LobbyService.Instance.JoinLobbyByIdAsync(response.Results[lobbyIndex].Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }

    }

    //Entrar por LobbyCode (mejor opcion)
    public async void JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
            {
                Player = GetPlayer()
            };

            Lobby lob = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
            joinedLobby = lob;
            Debug.Log("Joined Lobby with code " + lobbyCode);
            //PrintPlayers(joinedLobby);
            OnJoinedLobby?.Invoke(this, new LobbyEventArgs { lobby = joinedLobby });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }


    #endregion

    #region LISTAR LOBBIES
    async void ListLobbies()
    {
        try
        {
            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                }
            ,
                Order = new List<QueryOrder>
                {
                    new QueryOrder(false, QueryOrder.FieldOptions.Created)
                }
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);

            Debug.Log($"Total Lobbies: {response.Results.Count}");
            response.Results.ForEach(lobby =>
            {
                Debug.Log($"LobbyID: {lobby.Id} Name: {lobby.Name} GameMode: {lobby.Data["GameMode"].Value}");
            });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    public async void RefreshLobbyList()
    {
        try
        {
            QueryLobbiesOptions options = new QueryLobbiesOptions();
            options.Count = 25;

            options.Filters = new List<QueryFilter>
            {
                new QueryFilter(
                    field: QueryFilter.FieldOptions.AvailableSlots,
                    op: QueryFilter.OpOptions.GT,
                    value: "0" )
            };

            options.Order = new List<QueryOrder>
            {
                new QueryOrder(
                    asc: false,
                    field: QueryOrder.FieldOptions.Created)
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);

            response.Results.ForEach(lobby =>
            {
                Debug.Log($"LobbyID: {lobby.Id} LobbyCode: {lobby.LobbyCode} " +
                    $"Name: {lobby.Name}");
            });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    #endregion

    #region MANEJO DE JUGADOR Y EDICION DE LOBBY
    public string GetPlayerName()
    {
        return playerName;
    }
    Player GetPlayer()
    {
        //return new Player
        //{
        //    Data = new Dictionary<string, PlayerDataObject>
        //            {
        //                {KEY_PLAYER_NAME,
        //                    new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)}
        //            }
        //};

        return new Player(AuthenticationService.Instance.PlayerId, null, new Dictionary<string, PlayerDataObject> {
            { KEY_PLAYER_NAME, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName) }
        });
    }
    void PrintPlayers(Lobby lobby)
    {
        Debug.Log("Players in Lobby " + lobby.Name + " " + lobby.Data["GameMode"].Value);

        foreach (var player in lobby.Players)
        {
            Debug.Log(player.Id + " " + player.Data["PlayerName"].Value);
        }
    }

    async void UpdatePlayerName(string newPlayerName)
    {
        if (joinedLobby == null) return;
        try
        {
            playerName = newPlayerName;

            UpdatePlayerOptions options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                    {
                    {"PlayerName",
                            new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)}
                    }
            };

            Lobby lobby = await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId,
                options);
            joinedLobby = lobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    async void UpdateLobbyGameMode(string lobbyId, string gameMode)
    {
        try
        {
            hostLobby = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {"GameMode", new DataObject(DataObject.VisibilityOptions.Public, gameMode)}
                }
            });
            joinedLobby = hostLobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    async void LeaveLobby()
    {
        if (joinedLobby == null) return;
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            joinedLobby = null;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    async void KickPlayer(string playerId)
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(hostLobby.Id, playerId);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    #endregion

    #region RELAY

    async void CreateRelay()
    {
        try
        {
            Allocation aloc = await RelayService.Instance.CreateAllocationAsync(1);

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(aloc.AllocationId);

            Debug.Log(joinCode);
            RelayServerData data = AllocationUtils.ToRelayServerData(aloc, "wss");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(data);

            SetRelayJoinCode(joinCode);

            //AQUI SE CREA OTRA ITERACION DE HOST
            NetworkManager.Singleton.StartHost();
            Debug.Log("LISTO EL HOST");
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    async void JoinRelay(string joinCode)
    {
        try
        {
            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData data = AllocationUtils.ToRelayServerData(joinAlloc, "wss");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(data);
            Debug.Log("LISTO EL CLIENT");


            //AQUI SE CREA OTRA ITERACION DE CLIENT
            NetworkManager.Singleton.StartClient();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void SetRelayJoinCode(string relayJoinCode)
    {
        try
        {
            Debug.Log("SetRelayJoinCode " + relayJoinCode);

            Lobby lobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> {
                    { KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            });

            joinedLobby = lobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    public async void StartGameInLobby()
    {
        try
        {
            Lobby lobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> {
                    { KEY_START_GAME, new DataObject(DataObject.VisibilityOptions.Public, "1") }
                }
            });

            joinedLobby = lobby;
            IsHost = true;
            alreadyStartedGame = true;

            CreateRelay();
            OnLobbyStartGame?.Invoke(this, null);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public void JoinGameInLobby(string joinCode)
    {
        Debug.Log("JoinGame " + joinCode);
        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.Log("Invalid Relay code, wait");
            return;
        }

        IsHost = false;
        RelayJoinCode = joinCode;
        alreadyStartedGame = true;

        JoinRelay(RelayJoinCode);
        OnLobbyStartGame?.Invoke(this, null);
    }

    #endregion

    #region CHECKS
    public bool IsLobbyHost()
    {
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }
    public async Task<bool> CheckIfLobbyExist(string code)
    {
        try
        {
            JoinLobbyByCodeOptions joinOptions = new JoinLobbyByCodeOptions
            {
                Player = GetPlayer()
            };

            Lobby joined = await LobbyService.Instance.JoinLobbyByCodeAsync(code, joinOptions);

            // Si llegó aquí, el lobby existe y nos hemos unido; salir inmediatamente para no quedarnos dentro
            await LobbyService.Instance.RemovePlayerAsync(joined.Id, AuthenticationService.Instance.PlayerId);

            return true;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
            return false;
        }
    }
    public void GameStartBoolFlag()
    {
        IsGameStarted = true;
    }


    #endregion

}
