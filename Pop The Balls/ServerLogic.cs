using Stormancer;
using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer.Server.Components;
using Stormancer.Diagnostics;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Threading;

namespace Pop_The_Balls
{
    internal class Main
    {
        private ISceneHost _scene;
        private IEnvironment _env;
        private ILogger _log;
        private bool _isRunning = false;
        private int _ids = 0;
        private int _playersConnected = 0;

        private ConcurrentDictionary<long, Player> _players = new ConcurrentDictionary<long, Player>();
        private ConcurrentDictionary<int, Ball> _balls = new ConcurrentDictionary<int, Ball>();
        private Random _rand = new Random();

        private string _version = "a0.3.2";

        // "main" scene constructor. The constructor is called by the server when the scene is created.
        //
        public Main(ISceneHost scene)
        {
            _scene = scene;
            _env = _scene.GetComponent<IEnvironment>();
            _log = _scene.GetComponent<ILogger>();
            _log.Debug("server", "starting configuration");

            // we configure the functions that will be called by the Connecting, Connected and Disconnected events.
            // Connecting is called when a client tries to connect to the server. Please use this event to prevent player form accessing the server.
            // Connected is called when a client is already connected.
            // Disconnected is called when a client is already disconnected.
            _scene.Connecting.Add(OnConnecting);
            _scene.Connected.Add(OnConnected);
            _scene.Disconnected.Add(OnDisconnect);

            // We configure the routes and procedure the client can use.
            // A route is used for "one-shot" messages that don't need response such as position updates.
            // Produres send a response to the client. It's better to use them when client have to wait for a response from the server such as being hit.
            // Procedures use more bandwidth than regular routes.
            //
            // In our case, the server mostly has procedures as the client always needs to wait for a response from the server since it controls the game.
            _scene.AddProcedure("play", OnPlay);
            _scene.AddProcedure("click", OnClick);
            _scene.AddProcedure("update_leaderBoard", OnUpdateLeaderBoard);

            // this route is only used by the client to disconnect from the game (not the server) because it doesn't have to wait for the server to stop playing.
            _scene.AddRoute("exit", OnExit);

            //The starting and shutdown event are called when the scene is launched and shut down. these are useful if you need to initiate the server logic or save the game state before going down.
            _scene.Starting.Add(OnStarting);
            _scene.Shuttingdown.Add(OnShutdown);

            _log.Debug("server", "configuration complete");
        }

        private Task _gameLoop;

        private Task OnStarting(dynamic arg)
        {
            _log.Debug("server", "the scene has been loaded");
            _gameLoop = RunLogic();
            return Task.FromResult(true);
        }

        private async Task OnShutdown(ShutdownArgs arg)
        {
            _log.Debug("main", "the scene shuts down");
            _isRunning = false;
            try
            {
                await _gameLoop;

            }
            catch (Exception e)
            {
                _log.Log(LogLevel.Error, "runtimeError", "an error occurred in the game loop", e);
            }
        }

        // onConnecting is called by the connecting event.
        // We use this function to prevent unwanted clients to connect to the scene.
        // In our case we want players to use the correct version and don't want more than 100 of them connected at the same time
        // This function could also be used to check the incomming client connection datas such as nicknames, etc...
        //
        private Task OnConnecting(IScenePeerClient client)
        {
            _log.Debug("main", "A new client try to connect");
            var player_dto = client.GetUserData<ConnectionDtO>();
            if (_isRunning == false)
            {
                throw new ClientException("le serveur est vérouillé.");
            }
            else if (_playersConnected >= 100)
            {
                throw new ClientException("le serveur est complet.");
            }
            else if (player_dto.version != _version)
            {
                throw new ClientException("mauvaise version");
            }
            return Task.FromResult(true);
        }

        // On connected is called by the connected event.
        // We can use this function to read to data sent by the player and store them.
        // please note that it receives the same data onConnect() so we can store infos here while only validating them on the connecting event
        //
        private Task OnConnected(IScenePeerClient client)
        {
            _playersConnected++;
            return Task.FromResult(true);
        }

        //onDiconnected is called by the disconnected event
        //it can be used to remove client data stored on the scene such as nicknames, score, etc...
        //
        private Task OnDisconnect(DisconnectedArgs arg)
        {
            _playersConnected--;
            _log.Debug("main", arg.Peer.Id.ToString() + " has disconnected (" + arg.Reason + ")");
            return Task.FromResult(true);
        }

        // OnPlay is a response procedure handling the "play" rpc request sent by a client
        // An Rpc response procedure is created by server when an rpc request is received. These are totaly asynchronous so it can handle multiple rpc at the same time.
        // Here, we receive a Connection Data Transfert Object and send back an int to tell the client whether he can play or not.
        //
        private Task OnPlay(RequestContext<IScenePeerClient> ctx)
        {
            // we use RequestContext.ReadObject<Type>() to deserialize to data received from the Client.
            string name = ctx.ReadObject<ConnectionDtO>().name.ToLower();
            bool nameAlreadyTaken = false;

            foreach (var p in _players.Values)
            {
                string temp = p.name.ToLower();
                if (temp == name)
                {
                    nameAlreadyTaken = true;
                    break;
                }
            }
            if (nameAlreadyTaken == false)
            {
                var player = new Player(name);
                _log.Debug("main", "client joined with name : " + player.name);
                _players.TryAdd(ctx.RemotePeer.Id, player);
                ctx.SendValue(0);
            }
            else
            {
                ctx.SendValue(1);
            }
            return Task.FromResult(true);
        }

        // OnClick is a reponse procedure handling the "click" rpc request sent by a client
        // We receive a Stream containing click information sent by a client and send back a stream telling the player whether he touched a ball or not and his life.
        // If a ball have been hit, the server telle very client to destory it.
        //
        private Task OnClick(RequestContext<IScenePeerClient> ctx)
        {
            Player player;
            if (_players.TryGetValue(ctx.RemotePeer.Id, out player))
            {
                //bool hitGoodBall = false;
               // bool touched = false;
                var reader = new BinaryReader(ctx.InputStream);
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                long timestamp = reader.ReadInt32();

                Ball temp;
                Ball hitBall = null;

                bool hitGoodBall = TryFindHitBall(x,y,timestamp, out hitBall);
                
                if (hitBall != null)
                {
                    if (hitGoodBall)
                    {
                        player.score++;
                        player.streak++;
                        if (player.score > player.record)
                            player.record = player.score;
                        if (player.streak >= 5)
                        {
                            player.streak = 0;
                            player.Life++;
                            if (player.Life > 3)
                                player.Life = 3;
                            ctx.SendValue(s => 
                            {
                                var writer = new BinaryWriter(s, Encoding.UTF8, false);
                                writer.Write(3);
                                writer.Write(player.Life);
                            });
                        }
                        else
                            ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(1); });
                        _scene.Broadcast("destroy_ball", s =>
                        {
                            var writer = new BinaryWriter(s, Encoding.UTF8, false);
                            writer.Write(hitBall.id);
                        }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_SEQUENCED);
                        _balls.TryRemove(hitBall.id, out temp);
                    }
                    else
                    {
                        player.streak = 0;
                        player.Life--;
                        ctx.SendValue(s => 
                        {
                            var writer = new BinaryWriter(s, Encoding.UTF8, false);
                            writer.Write(2);
                            writer.Write(_players[ctx.RemotePeer.Id].Life);
                        });
                        _scene.Broadcast("destroy_ball", s =>
                        {
                            var writer = new BinaryWriter(s, Encoding.UTF8, false);
                            writer.Write(hitBall.id);
                        }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE_SEQUENCED);
                        _balls.TryRemove(hitBall.id, out temp);
                        if (_players[ctx.RemotePeer.Id].Life <= 0)
                        {
                            _players[ctx.RemotePeer.Id].Life = 3;
                            _players[ctx.RemotePeer.Id].score = 0;
                        }
                    }
                }
                if (hitBall == null)
                    ctx.SendValue(s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(0); });
            }
            return Task.FromResult(true);
        }

        private bool TryFindHitBall(float x, float y, long timestamp, out Ball hitBall)
        {
            hitBall = null;
            foreach (Ball ball in _balls.Values)
            {
                if (ball.IsClicked(x, y, timestamp, _scene))
                {
                    hitBall = ball;
                    if (((_env.Clock - hitBall.creationTime) / hitBall.oscillationTime) % 2 == 0)
                    {
                        return true;
                        break;
                    }
                }
            }

            return false;
        }


        // onUpdateLeaderBoard is a rpc resqponse procedure handling the "update_leaderboard" rpc request sent by a client
        // We receive nothing (null) and send a leaderboard data transfert object containing the leaderboard informations to be displayed by the client.
        //
        private Task OnUpdateLeaderBoard(RequestContext<IScenePeerClient> ctx)
        {
            List<Player> playerList = _players.Values.ToList();
            LeaderBoardDtO board = new LeaderBoardDtO();

            playerList.OrderByDescending(x => x.record);

            int i = 0;
            while (i < playerList.Count && i < 5)
            {
                board.names[i] = playerList[i].name;
                board.scores[i] = playerList[i].record.ToString();
                i++;
            }
            while (i < 5)
            {
                board.names[i] = "";
                board.scores[i] = "";
                i++;
            }
            if (_players.ContainsKey(ctx.RemotePeer.Id))
            {
                board.localNbr = _players[ctx.RemotePeer.Id].score.ToString();
            }
            ctx.SendValue(board);
            return Task.FromResult(true);
        }

        // onExit is the only route of this scene.
        // it handle the data received though the "exit" route.
        //It doesn't need to send a response back has a player doesn't have to wait for server agreement to exit the game.
        //
        private void OnExit(Packet<IScenePeerClient> packet)
        {
            if (_players.ContainsKey(packet.Connection.Id))
            {
                Player temp;
                _players.TryRemove(packet.Connection.Id, out temp);
            }
        }

        // The runLogic function contains the main logic of the scene.
        // Each given time, the server will create a ball and send its datas to every player connected.
        // When a ball lives for more than 30 seconds, the server will ask every player to destroy the corresponding ball.
        //
        private async Task RunLogic()
        {
            long lastUpdate = _env.Clock;

            _isRunning = true;
            while (_isRunning == true)
            {
                if (lastUpdate + 100 < _env.Clock)
                {
                    lastUpdate = _env.Clock;
                    Ball newBall = new Ball(_ids, _env.Clock, _rand);
                    _scene.Broadcast("create_ball", s =>
                    {
                        var writer = new BinaryWriter(s, Encoding.UTF8, false);
                        writer.Write(_ids);
                        writer.Write(newBall.x);
                        writer.Write(newBall.y);
                        writer.Write(newBall.vx);
                        writer.Write(newBall.vy);
                        writer.Write(newBall.creationTime);
                        writer.Write(newBall.oscillationTime);
                    }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                    _balls.TryAdd(_ids, newBall);
                    _ids++;
                    foreach (Ball ball in _balls.Values)
                    {
                        Ball temp;
                        if (_env.Clock > ball.creationTime + 30000)
                        {
                            _scene.Broadcast("destroy_ball", s => { var writer = new BinaryWriter(s, Encoding.UTF8, false); writer.Write(ball.id); }, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
                            _balls.TryRemove(ball.id, out temp);
                        }
                    }
                    if (_ids >= 2000000)
                    {
                        _ids = 0;
                    }
                }
                await Task.Delay(100);
            }
        }
    }
}